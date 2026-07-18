using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace RpgSceneMaker.Tests.Integration;

[Collection("integration")]
public class MusicLibraryTests
{
    // A structurally-valid 16-bit PCM mono WAV of `seconds` of silence — long enough that the importer's
    // duration measurement (real NAudio decode, no audio device) reports a non-zero length.
    private static byte[] SilentWav(double seconds = 1.0)
    {
        const int sampleRate = 44100, channels = 1, bitsPerSample = 16;
        var data = new byte[(int)(sampleRate * seconds) * channels * bitsPerSample / 8];
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + data.Length);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);              // PCM
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

    private static async Task<JsonElement> ImportAsync(HttpClient client, string name)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(SilentWav());
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", $"{name}.wav");
        form.Add(new StringContent(name), "name");
        var import = await client.PostAsync("/music/library/import", form);
        import.EnsureSuccessStatusCode();
        return await import.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Import_measures_duration_and_exposes_a_local_play_id()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var track = await ImportAsync(client, "Tavern Theme");
        var id = track.GetProperty("id").GetString();
        Assert.Equal("tavern-theme", id);
        Assert.Equal($"local:track:{id}", track.GetProperty("playId").GetString());
        Assert.True(track.GetProperty("durationMs").GetInt32() > 0);

        var list = await client.GetFromJsonAsync<JsonElement>("/music/library/tracks");
        Assert.Single(list.EnumerateArray());
    }

    [Fact]
    public async Task State_lists_local_as_an_available_source()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // Spotify is not connected in the test host, so only local is available.
        var state = await client.GetFromJsonAsync<JsonElement>("/music/state");
        var available = state.GetProperty("available").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("local", available);
        Assert.DoesNotContain("spotify", available);
    }

    [Fact]
    public async Task Deleting_a_track_scrubs_playlists_and_scene_play_ids()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var track = await ImportAsync(client, "Combat Loop");
        var id = track.GetProperty("id").GetString();
        var playId = $"local:track:{id}";

        // A playlist and a scene both reference the track.
        (await client.PutAsJsonAsync("/music/library/playlists/mix", new { name = "Mix", trackIds = new[] { id } }))
            .EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/scenes/battle", new
        {
            name = "Battle",
            music = new { source = "local", playId, volume = 0.5 },
        })).EnsureSuccessStatusCode();

        // Delete the track.
        var delete = await client.DeleteAsync($"/music/library/tracks/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        // It's gone from the library, scrubbed from the playlist, and nulled out of the scene (volume kept).
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/music/library/tracks")).EnumerateArray());

        var playlists = await client.GetFromJsonAsync<JsonElement>("/music/library/playlists");
        var mix = playlists.EnumerateArray().Single();
        Assert.Empty(mix.GetProperty("trackIds").EnumerateArray());

        var scene = await client.GetFromJsonAsync<JsonElement>("/scenes/battle");
        var music = scene.GetProperty("music");
        Assert.True(music.GetProperty("playId").ValueKind == JsonValueKind.Null);
        Assert.Equal(0.5, music.GetProperty("volume").GetDouble());
    }

    [Fact]
    public async Task Playlist_round_trips_and_deletes()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var put = await client.PutAsJsonAsync("/music/library/playlists/ambience",
            new { name = "Ambience", trackIds = new[] { "a", "b" } });
        put.EnsureSuccessStatusCode();
        var dto = await put.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("local:playlist:ambience", dto.GetProperty("playId").GetString());
        Assert.Equal(2, dto.GetProperty("trackIds").GetArrayLength());

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync("/music/library/playlists/ambience")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/music/library/playlists")).EnumerateArray());
    }

    [Fact]
    public async Task Unconnected_spotify_play_still_503s()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // Regression: a spotify: id routes to Spotify, which is unconfigured -> 503 (unchanged behaviour).
        var response = await client.GetAsync("/music/play?id=spotify:track:4uLU6hMCjMI75M1A2tKUQC");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
