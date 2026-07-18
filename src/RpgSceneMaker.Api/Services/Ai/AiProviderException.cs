namespace RpgSceneMaker.Api.Services.Ai;

/// <summary>
/// Raised when a call to an AI backend (Anthropic / OpenAI / Gemini) fails — bad key, rate limit, upstream
/// error. The Program.cs error middleware maps it to 502 "AI provider error". Mirrors
/// SpotifyException/HueException. <see cref="Provider"/> names the backend so the message can say which one.
/// This is our own type, distinct from any vendor SDK exception.
/// </summary>
public class AiProviderException : Exception
{
    public string Provider { get; }

    /// <param name="apiKey">The BYOK key used for this call, if known. It is scrubbed out of
    /// <paramref name="message"/> (see <see cref="Scrub"/>) before the message is surfaced.</param>
    public AiProviderException(string provider, string message, string? apiKey = null)
        : base(Scrub(message, apiKey))
    {
        Provider = provider;
    }

    /// <summary>
    /// Defense in depth: the message built here wraps a vendor SDK's own exception text, and an SDK can echo
    /// the request it made — Google's generative-language API in particular carries the key in the request
    /// (<c>?key=…</c> / <c>x-goog-api-key</c>), so a transport error could surface it. This exception flows to
    /// the client-visible assistant transcript, <c>/logs/list</c> and the persisted conversation, so strip the
    /// caller's own key out before it leaves. A no-op when the key is absent or doesn't appear in the message.
    /// </summary>
    internal static string Scrub(string message, string? apiKey) =>
        string.IsNullOrEmpty(apiKey) ? message : message.Replace(apiKey, "***");
}
