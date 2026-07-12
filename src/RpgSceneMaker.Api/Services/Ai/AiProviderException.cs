namespace RpgSceneMaker.Api.Services.Ai;

/// <summary>
/// Raised when a call to an AI backend (Anthropic / OpenAI / Gemini) fails — bad key, rate limit, upstream
/// error. The Program.cs error middleware maps it to 502 "AI provider error". Mirrors
/// SpotifyException/HueException. <see cref="Provider"/> names the backend so the message can say which one.
/// This is our own type, distinct from any vendor SDK exception.
/// </summary>
public class AiProviderException(string provider, string message) : Exception(message)
{
    public string Provider { get; } = provider;
}
