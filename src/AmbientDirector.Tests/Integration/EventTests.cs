using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AmbientDirector.Tests.Integration;

[Collection("integration")]
public class EventTests
{
    [Fact]
    public async Task Put_then_get_and_list_round_trips_the_flash_and_sounds()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        (await client.PutAsJsonAsync("/events/thunder", new
        {
            name = "⚡ Thunder",
            flash = new { color = "#ffffff", brightness = 100, durationMs = 200 },
            soundEffects = new[] { "clap" },
        })).EnsureSuccessStatusCode();

        var evt = await client.GetFromJsonAsync<JsonElement>("/events/thunder");
        Assert.Equal("⚡ Thunder", evt.GetProperty("name").GetString());
        Assert.Equal("#FFFFFF", evt.GetProperty("flash").GetProperty("color").GetString());  // normalised
        Assert.Equal(200, evt.GetProperty("flash").GetProperty("durationMs").GetInt32());
        Assert.Equal("clap", evt.GetProperty("soundEffects")[0].GetString());

        var list = await client.GetFromJsonAsync<JsonElement>("/events/list");
        Assert.Contains(list.EnumerateArray(), e => e.GetProperty("id").GetString() == "thunder");
    }

    [Fact]
    public async Task Trigger_event_touching_nothing_is_200_all_skipped()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // No flash and no sounds: both parts skip, so the trigger fully succeeds without hardware.
        (await client.PutAsJsonAsync("/events/nop", new { name = "Nothing" })).EnsureSuccessStatusCode();

        var response = await client.GetAsync("/events/nop/trigger");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("fullySucceeded").GetBoolean());
        Assert.Equal("skipped", body.GetProperty("light").GetString());
        Assert.Equal("skipped", body.GetProperty("sound").GetString());
    }

    [Fact]
    public async Task Trigger_command_reachable_by_both_get_and_post()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        (await client.PutAsJsonAsync("/events/nop", new { name = "Nothing" })).EnsureSuccessStatusCode();

        var get = await client.GetAsync("/events/nop/trigger");
        var post = await client.PostAsync("/events/nop/trigger", content: null);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
    }

    [Fact]
    public async Task Triggering_a_missing_event_is_404()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/events/ghost/trigger");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("not-a-colour", 100, 200)]  // bad hex
    [InlineData("#ffffff", 200, 200)]        // brightness > 100
    [InlineData("#ffffff", 100, 0)]          // duration < 1
    public async Task Invalid_flash_is_rejected_400(string color, int brightness, int durationMs)
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync("/events/bad", new
        {
            name = "Bad",
            flash = new { color, brightness, durationMs },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_sound_scrubs_its_id_from_events_case_insensitively()
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
        var soundId = (await import.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        Assert.Equal("thunder", soundId);

        // An event references it with different casing.
        (await client.PutAsJsonAsync("/events/storm", new
        {
            name = "Storm",
            soundEffects = new[] { "THUNDER" },
        })).EnsureSuccessStatusCode();

        // Delete the sound.
        var delete = await client.DeleteAsync($"/sounds/{soundId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        // The dangling id is scrubbed from the event.
        var evt = await client.GetFromJsonAsync<JsonElement>("/events/storm");
        Assert.Empty(evt.GetProperty("soundEffects").EnumerateArray());
    }

    // A tiny but structurally-valid 16-bit PCM mono WAV. Import/delete never decode it, so a couple of
    // silent samples are enough — this keeps the whole flow off NAudio's (Windows-only) audio device.
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
