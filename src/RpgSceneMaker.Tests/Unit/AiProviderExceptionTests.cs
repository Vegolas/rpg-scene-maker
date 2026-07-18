using RpgSceneMaker.Api.Services.Ai;
using Xunit;

namespace RpgSceneMaker.Tests.Unit;

public class AiProviderExceptionTests
{
    // A vendor SDK error can echo the request it made (Google's generative-language API carries the key in
    // the URL), and this exception reaches the client-visible transcript, /logs/list and the persisted
    // conversation — so the BYOK key must be scrubbed out of the surfaced message.
    [Fact]
    public void Scrubs_the_byok_key_from_the_message()
    {
        const string key = "AIzaSy-super-secret-key-123";
        var ex = new AiProviderException("gemini",
            $"Gemini API error: GET https://generativelanguage.googleapis.com/v1/models?key={key} failed (400)",
            key);

        Assert.DoesNotContain(key, ex.Message);
        Assert.Contains("***", ex.Message);
        Assert.Equal("gemini", ex.Provider);
    }

    [Fact]
    public void Leaves_the_message_intact_when_the_key_does_not_appear_in_it()
    {
        var ex = new AiProviderException("openai", "OpenAI API error (429): rate limited", "sk-unused-key");
        Assert.Equal("OpenAI API error (429): rate limited", ex.Message);
    }

    [Fact]
    public void No_key_supplied_is_a_no_op()
    {
        var ex = new AiProviderException("anthropic", "Anthropic API error: upstream boom");
        Assert.Equal("Anthropic API error: upstream boom", ex.Message);
    }
}
