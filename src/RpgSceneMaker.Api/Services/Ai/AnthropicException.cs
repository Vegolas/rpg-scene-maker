namespace RpgSceneMaker.Api.Services.Ai;

// Raised when a call to the Anthropic API fails (bad key, rate limit, upstream error); the Program.cs
// error middleware maps it to 502 "Anthropic error". Mirrors SpotifyException/HueException. Note this is
// our own type, distinct from the SDK's Anthropic.Exceptions.AnthropicException.
public class AnthropicException(string message) : Exception(message);
