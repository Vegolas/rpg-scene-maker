using System.Text.Json;

namespace AmbientDirector.Api.Services.Sharing;

/// <summary>Whether a bundled media file is a tile/portrait image or a sound-effect audio file — picks the
/// storage (<see cref="ImageFileStorage"/> vs <see cref="SoundFileStorage"/>) and the zip sub-folder.</summary>
public enum MediaKind
{
    Image,
    Audio,
}

/// <summary>One on-disk file an entity references by its stored file name (never a path or URL).
/// <paramref name="Required"/> marks a file the entity can't work without (a sound's audio) — import hard-fails
/// (<c>error.share.mediaMissing</c>) if it's absent from the pack, rather than clearing the ref like optional art.</summary>
public readonly record struct MediaRef(MediaKind Kind, string StoredName, bool Required = false);

/// <summary>An edge to another shareable entity: the target's kind (e.g. "lightfx", "sound", "scene") and id.
/// The export closure walks these; import rewrites them to the recreated copies' ids.</summary>
public readonly record struct DepRef(string Kind, string Id);

/// <summary>One place an entity binds a bulb by <c>LightKey</c>, with a human label naming the owner so the
/// import remap UI can say which scene/light a source key came from.</summary>
public readonly record struct LightKeySite(string Key, string OwnerLabel);

/// <summary>
/// The per-kind "share descriptor": everything the exporter/importer need to know about one content type
/// without hard-coding a giant switch. External objects (not an interface on the models) — the models are
/// deliberately behaviour-free wire DTOs mirrored by the UI and mapped as EF owned-JSON, so their behaviour
/// lives in helpers like this (cf. <c>ReferenceScrubber</c>, <c>LightFxDetacher</c>, the <c>*Validation</c>
/// classes). Registered by <see cref="ShareRegistry"/>; the strongly-typed implementations derive from
/// <see cref="ShareDescriptor{T}"/>.
/// </summary>
public interface IShareDescriptor
{
    /// <summary>The kind slug used in the manifest, the <c>/share/{kind}/…</c> route, and cross-refs.</summary>
    string Kind { get; }

    /// <summary>Load the entity from its store (NOCASE id match), or null if it no longer exists.</summary>
    Task<object?> LoadAsync(string id);

    /// <summary>Whether an entity with this id already exists locally (the import collision probe).</summary>
    Task<bool> ExistsAsync(string id);

    string IdOf(object entity);
    string NameOf(object entity);

    /// <summary>Assign the id an entity is being imported under, before <see cref="Rewrite"/>.</summary>
    void SetId(object entity, string id);

    /// <summary>The on-disk files this entity owns/references (deduped by the caller across the pack).</summary>
    IEnumerable<MediaRef> Media(object entity);

    /// <summary>Edges to other entities to bundle in an export and re-wire on import.</summary>
    IEnumerable<DepRef> Dependencies(object entity);

    /// <summary>The bulb-binding sites (with owner labels) that need remapping at import.</summary>
    IEnumerable<LightKeySite> LightKeys(object entity);

    /// <summary>Serialize the entity to a JSON element for the manifest (uses the concrete type, so it is safe
    /// even though the entity is boxed as <see cref="object"/>).</summary>
    JsonElement ToJson(object entity);

    /// <summary>Deserialize an entity out of the manifest (via <c>AiJson.Options</c>).</summary>
    object FromJson(JsonElement json);

    /// <summary>Rewrite ids, media names and light keys in place using the resolved commit maps.</summary>
    void Rewrite(object entity, ShareRewriteContext ctx);

    /// <summary>Repair a field that can arrive in an unusable-but-fixable state (e.g. a placeholder music id),
    /// so a pack carrying it still imports instead of hard-failing validation. Returns a locale key describing
    /// the repair (for the import report), or null when nothing needed fixing. Runs just before
    /// <see cref="Validate"/> on commit.</summary>
    string? Sanitize(object entity);

    /// <summary>Run the type's existing validator (throws on invalid). Import validates every entity before
    /// upserting any, so a bad pack fails before it writes any row — all validators are pure (entity-only).</summary>
    void Validate(object entity);

    /// <summary>Upsert the (already validated + rewritten) entity through its store.</summary>
    Task UpsertAsync(object entity);
}
