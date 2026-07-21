using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using AmbientDirector.Api.Contracts;
using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Services.Ai;
using AmbientDirector.Api.Validation;

namespace AmbientDirector.Api.Services.Sharing;

/// <summary>
/// The two-phase share-pack import, modeled on <see cref="Images.PdfImporter"/>: an uploaded zip is held as a
/// short-lived temp under <c>&lt;images&gt;/.share-tmp</c>; <see cref="SaveTempAndInspectAsync"/> parses the
/// manifest and returns a summary (what's inside, id collisions, the light keys to remap) without committing;
/// <see cref="CommitAsync"/> then recreates the media under fresh names, mints fresh ids where taken, re-wires
/// every cross-reference and light key, validates <em>all</em> entities, and upserts them. No pack is ever
/// persisted — the temp is deleted after commit and swept after an hour.
/// </summary>
public sealed class ShareImporter
{
    // The uploaded zip; the per-file caps guard against a zip bomb (a tiny entry that expands hugely). Kept
    // under ASP.NET's 128 MB default multipart-body limit so ReadFormAsync doesn't reject it before our check.
    public const long MaxPackBytes = 100L * 1024 * 1024;
    public const long MaxRequestBytes = 110L * 1024 * 1024;
    public const string MaxPackMb = "100";
    private const long MaxImageBytes = 10L * 1024 * 1024;   // matches the ImageFileStorage upload cap
    private const long MaxAudioBytes = 50L * 1024 * 1024;

    // Structural bounds so a hand-crafted manifest can't exhaust memory before the per-file caps engage.
    private const int MaxEntities = 2000;
    private const int MaxMediaFiles = 1000;

    private static readonly TimeSpan TempTtl = TimeSpan.FromHours(1);

    // A temp id echoed to the client and returned as user input on commit — guarded BEFORE any Path.Combine
    // (the PdfImporter idiom): no '..', slash or drive letter can match.
    private static readonly Regex ValidId = new("^[a-z0-9]{12}$", RegexOptions.Compiled);

    // A bundled media file name must be a plain stored-name slug (image or audio) — the traversal guard on the
    // manifest side (output names are always server-generated regardless, so this is defence in depth).
    private static readonly Regex ValidMediaName =
        new(@"^[a-z0-9-]+\.(webp|jpe?g|png|mp3|wav|ogg)$", RegexOptions.Compiled);

    private readonly string _tempDir;
    private readonly ShareRegistry _registry;
    private readonly ImageFileStorage _images;
    private readonly SoundFileStorage _sounds;
    private readonly LocaleService _locales;

    public ShareImporter(string imagesPath, ShareRegistry registry, ImageFileStorage images,
        SoundFileStorage sounds, LocaleService locales)
    {
        _registry = registry;
        _images = images;
        _sounds = sounds;
        _locales = locales;
        _tempDir = Path.Combine(imagesPath, ".share-tmp");
        Directory.CreateDirectory(_tempDir);
    }

    // ---- Phase 1: inspect ----

    public async Task<ShareInspectResult> SaveTempAndInspectAsync(Stream zip, string? lang, CancellationToken ct = default)
    {
        SweepStaleTemps();

        var id = Guid.NewGuid().ToString("N")[..12]; // freshly generated → safe by construction
        var path = Path.Combine(_tempDir, id + ".zip");
        await using (var file = File.Create(path))
            await zip.CopyToAsync(file, ct);

        try
        {
            using var archive = OpenArchive(path);
            var manifest = await ParseManifestAsync(archive, ct);

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var collisions = new List<ShareCollision>();
            var issues = new List<ShareEntityIssue>();
            foreach (var (kind, list) in manifest.Entities)
            {
                var descriptor = _registry.Get(kind);
                counts[kind] = list?.Count ?? 0;
                foreach (var json in list ?? [])
                {
                    var entity = Deserialize(descriptor, json);
                    var entityId = descriptor.IdOf(entity);
                    var name = descriptor.NameOf(entity);
                    if (await descriptor.ExistsAsync(entityId))
                        collisions.Add(new ShareCollision(kind, entityId, name));
                    // Preview validation: run the entity's own validator on the raw pack data (a throwaway
                    // object — Validate mutates it) and surface any problem. Light/media/id fields are all
                    // slug-shaped in a pack, so this only flags genuine issues (e.g. a placeholder music id).
                    if (ValidationProblem(descriptor, entity, lang) is { } problem)
                        issues.Add(new ShareEntityIssue(kind, entityId, name, problem));
                }
            }

            var missing = new List<string>();
            foreach (var media in manifest.Media ?? [])
            {
                if (string.IsNullOrEmpty(media.Name) || !ValidMediaName.IsMatch(media.Name))
                {
                    missing.Add(media.Name ?? "");
                    continue;
                }
                if (archive.GetEntry(MediaDir(media.Kind) + media.Name) is null)
                    missing.Add(media.Name);
            }

            var lightKeys = (manifest.LightKeys ?? [])
                .Select(k => new ShareLightKeyDto(k.Key, k.Sources ?? [])).ToList();

            return new ShareInspectResult(id, manifest.Primary.Kind ?? "", manifest.Primary.Id ?? "",
                counts, collisions, lightKeys, manifest.Media?.Count ?? 0, missing, issues);
        }
        catch
        {
            TryDelete(path); // never leave a temp for a pack we rejected
            throw;
        }
    }

    // ---- Phase 2: commit ----

    public async Task<ShareCommitResult> CommitAsync(ShareCommitInput input, string? lang, CancellationToken ct = default)
    {
        var zipPath = ResolveTemp(input.TempId);
        var policy = (input.CollisionPolicy ?? "copy").Trim().ToLowerInvariant();
        if (policy is not ("copy" or "overwrite" or "skip")) policy = "copy";

        // Effective light map: empty / "skip" ⇒ null (drop the binding); a real target must be a valid slug.
        var lightMap = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, target) in input.LightKeys ?? [])
        {
            var trimmed = target?.Trim();
            if (string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, "skip", StringComparison.OrdinalIgnoreCase))
                lightMap[source] = null;
            else if (!LightValidation.IsSlug(trimmed))
                throw new ValidationException("error.share.lightKeyTarget", trimmed);
            else
                lightMap[source] = trimmed;
        }

        using var archive = OpenArchive(zipPath);
        var manifest = await ParseManifestAsync(archive, ct);

        // Deserialize every bundled entity once (id + object).
        var items = new List<(string Kind, object Entity, string OldId)>();
        foreach (var (kind, list) in manifest.Entities)
        {
            var descriptor = _registry.Get(kind);
            foreach (var json in list ?? [])
            {
                var entity = Deserialize(descriptor, json);
                items.Add((kind, entity, descriptor.IdOf(entity)));
            }
        }

        // Phase A — plan ids (dependencies and dependents both resolved from this map).
        var idMap = new Dictionary<(string, string), string>(ShareKeyComparer.Instance);
        var reserved = new HashSet<(string, string)>(ShareKeyComparer.Instance);
        var skipped = new HashSet<(string, string)>(ShareKeyComparer.Instance);
        var remapped = new List<ShareRemap>();
        foreach (var (kind, _, oldId) in items)
        {
            var descriptor = _registry.Get(kind);
            var exists = await descriptor.ExistsAsync(oldId);
            string newId;
            if (policy == "overwrite")
            {
                newId = oldId; // UpsertAsync overwrites the existing row
            }
            else if (policy == "skip" && exists)
            {
                newId = oldId; // refs resolve to the existing local entity; this one isn't re-imported
                skipped.Add((kind, oldId));
            }
            else if (!exists && !reserved.Contains((kind, oldId)))
            {
                newId = oldId; // free — keep the nice id
            }
            else
            {
                newId = await MintUniqueIdAsync(descriptor, kind, oldId, reserved);
            }
            reserved.Add((kind, newId));
            idMap[(kind, oldId)] = newId;
            if (!string.Equals(newId, oldId, StringComparison.OrdinalIgnoreCase))
                remapped.Add(new ShareRemap(kind, oldId, newId));
        }

        // Which manifest media entries are actually present in the zip (looked up by the safe composed path).
        var present = new Dictionary<string, (MediaKind Kind, ZipArchiveEntry Entry)>(StringComparer.OrdinalIgnoreCase);
        foreach (var media in manifest.Media ?? [])
        {
            if (string.IsNullOrEmpty(media.Name) || !ValidMediaName.IsMatch(media.Name))
                throw new ValidationException("error.share.invalid");
            if (archive.GetEntry(MediaDir(media.Kind) + media.Name) is { } entry)
                present[media.Name] = (media.Kind, entry);
        }

        // A required media file (a sound's audio) with no bundled bytes → hard error.
        foreach (var (kind, entity, _) in items)
            foreach (var media in _registry.Get(kind).Media(entity))
                if (media.Required && !present.ContainsKey(media.StoredName))
                    throw new ValidationException("error.share.mediaMissing", _registry.Get(kind).NameOf(entity));

        // Phase B — recreate media under fresh, server-generated names (the zip's names never reach a path).
        var mediaMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var savedImages = new List<string>();
        var savedAudio = new List<string>();
        try
        {
            foreach (var (name, info) in present)
            {
                var ext = Path.GetExtension(name).ToLowerInvariant();
                var newId = Guid.NewGuid().ToString("N")[..12];
                await using var raw = info.Entry.Open();
                if (info.Kind == MediaKind.Audio)
                {
                    var stored = await _sounds.SaveAsync(newId, ext, new CappedStream(raw, MaxAudioBytes), ct);
                    savedAudio.Add(stored);
                    mediaMap[name] = stored;
                }
                else
                {
                    var stored = await _images.SaveAsync(newId, ext, new CappedStream(raw, MaxImageBytes), ct);
                    savedImages.Add(stored);
                    mediaMap[name] = stored;
                }
            }

            // Phase C1 — set ids, rewrite, repair fixable fields, then validate EVERY entity before writing any
            // (validators are pure, so an unrepairable pack fails here having touched no store row; only the
            // just-saved media needs undoing). Sanitize repairs the placeholder cases (e.g. an invalid music
            // link) so a normal pack imports rather than hard-failing.
            var ctx = new ShareRewriteContext { IdMap = idMap, MediaMap = mediaMap, LightKeyMap = lightMap };
            var live = items.Where(it => !skipped.Contains((it.Kind, it.OldId))).ToList();
            var repaired = new List<ShareRepairNote>();
            foreach (var (kind, entity, oldId) in live)
            {
                var descriptor = _registry.Get(kind);
                var newId = idMap[(kind, oldId)];
                descriptor.SetId(entity, newId);
                descriptor.Rewrite(entity, ctx);
                if (descriptor.Sanitize(entity) is { } repairKey)
                    repaired.Add(new ShareRepairNote(kind, newId, _locales.Localize(lang, repairKey)));
                descriptor.Validate(entity);
            }

            // Phase C2 — upsert in dependency order (deps before dependents).
            var created = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (kind, entity, oldId) in live.OrderBy(it => CommitRank(it.Kind)))
            {
                await _registry.Get(kind).UpsertAsync(entity);
                if (!created.TryGetValue(kind, out var list)) created[kind] = list = [];
                list.Add(idMap[(kind, oldId)]);
            }

            TryDelete(zipPath);

            // The primary entity's new id, so the panel can open it for review.
            var primaryKind = manifest.Primary.Kind ?? "";
            idMap.TryGetValue((primaryKind, manifest.Primary.Id ?? ""), out var primaryNewId);
            return new ShareCommitResult(created, mediaMap.Count, remapped, repaired, primaryKind, primaryNewId);
        }
        catch
        {
            // Undo this commit's media writes; keep the temp so the caller can retry after fixing the mapping.
            foreach (var file in savedImages) _images.Delete(file);
            foreach (var file in savedAudio) _sounds.Delete(file);
            throw;
        }
    }

    // ---- internals ----

    private static ZipArchive OpenArchive(string path)
    {
        try
        {
            return ZipFile.OpenRead(path);
        }
        catch (InvalidDataException)
        {
            throw new ValidationException("error.share.invalid");
        }
    }

    private async Task<SharePackManifest> ParseManifestAsync(ZipArchive archive, CancellationToken ct)
    {
        var entry = archive.GetEntry(SharePack.ManifestEntry)
            ?? throw new ValidationException("error.share.invalid");

        SharePackManifest? manifest;
        try
        {
            await using var stream = entry.Open();
            manifest = await JsonSerializer.DeserializeAsync<SharePackManifest>(stream, AiJson.Options, ct);
        }
        catch (JsonException)
        {
            throw new ValidationException("error.share.invalid");
        }

        if (manifest is null || manifest.Format != SharePack.FormatId)
            throw new ValidationException("error.share.invalid");
        if (manifest.FormatVersion > SharePack.CurrentVersion)
            throw new ValidationException("error.share.version", manifest.FormatVersion);

        manifest.Entities ??= new(StringComparer.OrdinalIgnoreCase);
        var total = 0;
        foreach (var (kind, list) in manifest.Entities)
        {
            if (!_registry.Has(kind))
                throw new ValidationException("error.share.unknownKind", kind);
            total += list?.Count ?? 0;
        }
        if (total > MaxEntities || (manifest.Media?.Count ?? 0) > MaxMediaFiles)
            throw new ValidationException("error.share.invalid");

        return manifest;
    }

    // Run an entity's validator on the raw pack data; return a localized "why" if it fails, else null. Validate
    // mutates the (throwaway) entity, which is fine here — inspect discards it.
    private string? ValidationProblem(IShareDescriptor descriptor, object entity, string? lang)
    {
        try
        {
            descriptor.Validate(entity);
            return null;
        }
        catch (Exception ex) when (ex is IErrorCode coded)
        {
            return _locales.Localize(lang, coded.Code, coded.Args);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static object Deserialize(IShareDescriptor descriptor, JsonElement json)
    {
        try
        {
            return descriptor.FromJson(json);
        }
        catch (JsonException)
        {
            throw new ValidationException("error.share.invalid");
        }
    }

    private static async Task<string> MintUniqueIdAsync(
        IShareDescriptor descriptor, string kind, string baseId, HashSet<(string, string)> reserved)
    {
        for (var n = 2; ; n++)
        {
            var candidate = $"{baseId}-{n}";
            if (!reserved.Contains((kind, candidate)) && !await descriptor.ExistsAsync(candidate))
                return candidate;
        }
    }

    private static string MediaDir(MediaKind kind) => kind == MediaKind.Audio ? SharePack.AudioDir : SharePack.ImagesDir;

    private static int CommitRank(string kind)
    {
        var index = Array.IndexOf(ShareRegistry.CommitOrder, kind.ToLowerInvariant());
        return index < 0 ? int.MaxValue : index;
    }

    private string ResolveTemp(string id)
    {
        if (!ValidId.IsMatch(id))
            throw new ValidationException("error.share.notFound");
        var path = Path.Combine(_tempDir, id + ".zip");
        if (!File.Exists(path))
            throw new ValidationException("error.share.notFound");
        return path;
    }

    private void SweepStaleTemps()
    {
        try
        {
            var cutoff = DateTime.UtcNow - TempTtl;
            foreach (var file in Directory.EnumerateFiles(_tempDir, "*.zip"))
            {
                try { if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file); }
                catch { /* locked / racing delete — a leftover temp is harmless */ }
            }
        }
        catch { /* best-effort sweep; never fail an upload over housekeeping */ }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* a leftover temp is harmless — it's swept an hour later */ }
    }
}

// Read-only stream wrapper that throws once more than the cap has been read, so extracting a zip entry can't
// exceed the per-file cap even if the entry decompresses to far more than its compressed size (a zip bomb).
file sealed class CappedStream(Stream inner, long cap) : Stream
{
    private long _read;

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var n = await inner.ReadAsync(buffer, cancellationToken);
        Count(n);
        return n;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = inner.Read(buffer, offset, count);
        Count(n);
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private void Count(int n)
    {
        if (n <= 0) return;
        _read += n;
        if (_read > cap)
            throw new ValidationException("error.share.mediaTooLarge", cap / (1024 * 1024));
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
