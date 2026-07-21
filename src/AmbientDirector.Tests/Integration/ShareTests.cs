using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace AmbientDirector.Tests.Integration;

/// <summary>End-to-end coverage of the shareable-pack export/import (issue #111): a round-trip that exercises
/// every rewrite rule (fresh ids on collision, recreated media, re-wired dependency refs, and the light-key
/// remap), plus the malformed / wrong-version / zip-slip guards.</summary>
[Collection("integration")]
public class ShareTests
{
    [Fact]
    public async Task Export_then_import_recreates_a_scene_with_deps_media_and_light_remap()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // ---- Arrange: a Light FX, two sounds (one with distinctive metadata), a tile image, and a scene
        //      that references all of them + two bulb bindings. ----
        (await client.PutAsJsonAsync("/lightfx/torch", new
        {
            name = "Torch",
            keyframes = new[]
            {
                new { atMs = 0, brightness = 50, color = "#FF0000" },
                new { atMs = 400, brightness = 100, color = "#FFAA00" },
            },
            loop = true,
            cycleMs = 800,
        })).EnsureSuccessStatusCode();

        var boomId = await ImportSound(client, "Boom", "boom.wav");
        var clangId = await ImportSound(client, "Clang", "clang.wav");
        Assert.Equal("boom", boomId);
        Assert.Equal("clang", clangId);

        // Give "boom" a non-default volume + category. SoundImporter forces volume 1.0, so if import wrongly
        // routed through it, this 0.42 would be lost — proving the share importer preserves metadata.
        (await client.PutAsJsonAsync("/sounds/boom", new { name = "Boom", category = "Combat", volume = 0.42, loop = false }))
            .EnsureSuccessStatusCode();

        var imageName = await UploadImage(client);

        (await client.PutAsJsonAsync("/scenes/tavern", new
        {
            name = "Tavern",
            image = imageName,
            lights = new object[]
            {
                new { lightKey = "lamp-left", power = true, color = "#FF8800", effect = new { type = "fx", fxId = "torch" } },
                new { lightKey = "lamp-right", power = true, brightness = 60 },
            },
            soundEffects = new[] { "boom", "clang" },
        })).EnsureSuccessStatusCode();

        // ---- Export ----
        var exportResp = await client.GetAsync("/share/scene/tavern/export");
        exportResp.EnsureSuccessStatusCode();
        Assert.Equal("application/zip", exportResp.Content.Headers.ContentType?.MediaType);
        var packBytes = await exportResp.Content.ReadAsByteArrayAsync();

        // ---- Inspect ----
        var inspect = await PostPack(client, packBytes);
        Assert.Equal(HttpStatusCode.OK, inspect.StatusCode);
        var summary = await inspect.Content.ReadFromJsonAsync<JsonElement>();

        var tempId = summary.GetProperty("tempId").GetString()!;
        Assert.Equal("scene", summary.GetProperty("primaryKind").GetString());
        var counts = summary.GetProperty("counts");
        Assert.Equal(1, counts.GetProperty("scene").GetInt32());
        Assert.Equal(1, counts.GetProperty("lightfx").GetInt32());
        Assert.Equal(2, counts.GetProperty("sound").GetInt32());
        Assert.Equal(3, summary.GetProperty("mediaCount").GetInt32()); // 1 image + 2 audio
        Assert.Empty(summary.GetProperty("mediaMissing").EnumerateArray());
        // Every entity already exists here → all collisions; two distinct light keys to remap.
        Assert.True(summary.GetProperty("collisions").GetArrayLength() >= 4);
        var lightKeys = summary.GetProperty("lightKeys").EnumerateArray()
            .Select(k => k.GetProperty("key").GetString()).ToList();
        Assert.Contains("lamp-left", lightKeys);
        Assert.Contains("lamp-right", lightKeys);

        // ---- Commit: map lamp-left → a bulb, skip lamp-right ----
        var commit = await client.PostAsJsonAsync("/share/import/commit", new
        {
            tempId,
            lightKeys = new Dictionary<string, string?> { ["lamp-left"] = "lamp-left", ["lamp-right"] = "" },
            collisionPolicy = (string?)null,
        });
        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);

        // ---- Assert: everything landed as fresh copies, with refs re-wired ----
        var scene = await client.GetFromJsonAsync<JsonElement>("/scenes/tavern-2"); // "tavern" was taken

        var lights = scene.GetProperty("lights").EnumerateArray().ToList();
        Assert.Single(lights);                                                       // lamp-right was skipped
        Assert.Equal("lamp-left", lights[0].GetProperty("lightKey").GetString());
        Assert.Equal("torch-2", lights[0].GetProperty("effect").GetProperty("fxId").GetString());

        var soundEffects = scene.GetProperty("soundEffects").EnumerateArray().Select(s => s.GetString()).ToList();
        Assert.Equal(new[] { "boom-2", "clang-2" }, soundEffects);

        var newImage = scene.GetProperty("image").GetString()!;
        Assert.NotEqual(imageName, newImage);                                        // recreated under a fresh name
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/images/{newImage}")).StatusCode);

        // The bundled Light FX was recreated.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/lightfx/torch-2")).StatusCode);

        // Sound metadata preserved (NOT re-imported through SoundImporter, which would reset volume to 1.0).
        var sounds = await client.GetFromJsonAsync<JsonElement>("/sounds/list");
        var boom2 = sounds.EnumerateArray().Single(s => s.GetProperty("id").GetString() == "boom-2");
        Assert.Equal(0.42, boom2.GetProperty("volume").GetDouble(), 3);
        Assert.Equal("Combat", boom2.GetProperty("category").GetString());
    }

    [Fact]
    public async Task Import_flags_a_fixable_field_in_the_preview_and_repairs_it_on_commit()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // A pack whose scene carries a placeholder music id (the starter-template shape) — invalid, but the
        // kind of thing the importer repairs rather than hard-failing on.
        var pack = BuildPack("""
            {"format":"ambient-director/share-pack","formatVersion":1,
             "primary":{"kind":"scene","id":"badmusic"},
             "entities":{"scene":[{"id":"badmusic","name":"Bad Music","light":null,"lights":[],
               "music":{"playId":"PASTE-SPOTIFY-URI-OR-LINK-HERE"},"soundEffects":[],"image":null}]},
             "lightKeys":[],"media":[]}
            """);

        // Inspect surfaces the problem up front.
        var inspect = await PostPack(client, pack);
        inspect.EnsureSuccessStatusCode();
        var summary = await inspect.Content.ReadFromJsonAsync<JsonElement>();
        var issues = summary.GetProperty("issues").EnumerateArray().ToList();
        Assert.Single(issues);
        Assert.Equal("badmusic", issues[0].GetProperty("id").GetString());
        Assert.False(string.IsNullOrWhiteSpace(issues[0].GetProperty("problem").GetString()));
        var tempId = summary.GetProperty("tempId").GetString()!;

        // Commit repairs it (clears the bad link) rather than failing, and reports the repair + the primary id.
        var commit = await client.PostAsJsonAsync("/share/import/commit",
            new { tempId, lightKeys = (object?)null, collisionPolicy = (string?)null });
        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);
        var result = await commit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("scene", result.GetProperty("primaryKind").GetString());
        Assert.Equal("badmusic", result.GetProperty("primaryId").GetString());
        var repaired = result.GetProperty("repaired").EnumerateArray().ToList();
        Assert.Single(repaired);
        Assert.Equal("scene", repaired[0].GetProperty("kind").GetString());

        // The scene imported, with the invalid music link cleared to null (fixable later in the editor).
        var scene = await client.GetFromJsonAsync<JsonElement>("/scenes/badmusic");
        Assert.Equal(JsonValueKind.Null, scene.GetProperty("music").GetProperty("playId").ValueKind);
    }

    [Fact]
    public async Task Import_inspect_rejects_a_non_zip_upload()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var resp = await PostPack(client, "this is definitely not a zip"u8.ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("error.share.invalid", await CodeOf(resp));
    }

    [Fact]
    public async Task Import_inspect_rejects_a_newer_pack_version()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var pack = BuildPack("""
            {"format":"ambient-director/share-pack","formatVersion":999,
             "primary":{"kind":"scene","id":"x"},"entities":{},"lightKeys":[],"media":[]}
            """);
        var resp = await PostPack(client, pack);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("error.share.version", await CodeOf(resp));
    }

    [Fact]
    public async Task Import_commit_rejects_a_traversal_media_name()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // A structurally-valid pack whose only flaw is a media name that tries to escape the media folder.
        var pack = BuildPack("""
            {"format":"ambient-director/share-pack","formatVersion":1,
             "primary":{"kind":"scene","id":"x"},"entities":{},"lightKeys":[],
             "media":[{"name":"../evil.png","kind":"image","role":null}]}
            """, ("media/images/evil.png", new byte[] { 1, 2, 3 }));

        // Inspect tolerates it (the bad name just shows up as "missing"); commit is where the guard fires.
        var inspect = await PostPack(client, pack);
        inspect.EnsureSuccessStatusCode();
        var tempId = (await inspect.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tempId").GetString()!;

        var commit = await client.PostAsJsonAsync("/share/import/commit", new { tempId, lightKeys = (object?)null, collisionPolicy = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, commit.StatusCode);
        Assert.Equal("error.share.invalid", await CodeOf(commit));
    }

    // ---- helpers ----

    private static async Task<string?> ImportSound(HttpClient client, string name, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(MinimalWav());
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(name), "name");
        var resp = await client.PostAsync("/sounds/import", form);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
    }

    private static async Task<string?> UploadImage(HttpClient client)
    {
        using var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 });
        part.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(part, "file", "art.png");
        var resp = await client.PostAsync("/images/upload", form);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
    }

    private static Task<HttpResponseMessage> PostPack(HttpClient client, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(part, "file", "pack.zip");
        return client.PostAsync("/share/import/inspect", form);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage resp)
    {
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static byte[] BuildPack(string manifestJson, params (string Name, byte[] Bytes)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = zip.CreateEntry("manifest.json");
            using (var s = manifest.Open()) s.Write(Encoding.UTF8.GetBytes(manifestJson));
            foreach (var (name, bytes) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var es = entry.Open();
                es.Write(bytes);
            }
        }
        return ms.ToArray();
    }

    // A tiny but structurally-valid 16-bit PCM mono WAV (mirrors SoundScrubTests) — never actually decoded.
    private static byte[] MinimalWav()
    {
        const int sampleRate = 44100, channels = 1, bitsPerSample = 16;
        var data = new byte[8];
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + data.Length);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)(channels * bitsPerSample / 8));
        w.Write((short)bitsPerSample);
        w.Write("data"u8.ToArray());
        w.Write(data.Length);
        w.Write(data);
        w.Flush();
        return ms.ToArray();
    }
}
