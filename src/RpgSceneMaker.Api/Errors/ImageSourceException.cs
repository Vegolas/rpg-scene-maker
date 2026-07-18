namespace RpgSceneMaker.Api.Errors;

/// <summary>
/// Raised when an upstream image source (Scryfall's search API or its card-art CDN) fails — a non-success
/// HTTP status or a transport error while searching or downloading. The Program.cs error middleware maps it
/// to 502 "Image source error" via <see cref="ErrorClassifier"/>. Mirrors HueException / SpotifyException /
/// AiProviderException. Upstream message text stays English (repo convention for integration errors).
/// </summary>
public sealed class ImageSourceException(string message) : Exception(message);
