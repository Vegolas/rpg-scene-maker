using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AmbientDirector.Tests.Integration;

[Collection("integration")]
public class SoundScrubTests
{
    // A tiny but structurally-valid 16-bit PCM mono WAV. Import/delete never decode it, so a couple of
    // silent samples are enough — this keeps the whole flow off NAudio's (Windows-only) audio device.
    private static byte[] MinimalWav()
    {
        const int sampleRate = 44100, channels = 1, bitsPerSample = 16;
        var data = new byte[8];  // 4 silent 16-bit samples
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

    [Fact]
    public async Task Deleting_a_sound_scrubs_its_id_from_scenes_case_insensitively()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // Import a sound named "Thunder" -> slug id "thunder".
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(MinimalWav());
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "thunder.wav");
        form.Add(new StringContent("Thunder"), "name");

        var import = await client.PostAsync("/sounds/import", form);
        import.EnsureSuccessStatusCode();
        var sound = await import.Content.ReadFromJsonAsync<JsonElement>();
        var soundId = sound.GetProperty("id").GetString();
        Assert.Equal("thunder", soundId);

        // A scene references it with different casing.
        (await client.PutAsJsonAsync("/scenes/storm", new
        {
            name = "Storm",
            soundEffects = new[] { "THUNDER" },
        })).EnsureSuccessStatusCode();

        // Delete the sound.
        var delete = await client.DeleteAsync($"/sounds/{soundId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        // The dangling id is scrubbed from the scene.
        var scene = await client.GetFromJsonAsync<JsonElement>("/scenes/storm");
        Assert.Empty(scene.GetProperty("soundEffects").EnumerateArray());
    }
}
