using System.Net.Sockets;
using RpgSceneMaker.Api.Services;
using RpgSceneMaker.Api.Services.Ai;

namespace RpgSceneMaker.Api.Errors;

/// <summary>
/// Maps an exception to an HTTP status and a localizable title key — shared by the error middleware
/// (Program.cs) and the scene/event activators, so the classification lives in one place. A more specific
/// code from <see cref="IErrorCode"/> wins over the title key at the call site.
/// </summary>
public static class ErrorClassifier
{
    public static (int Status, string TitleKey) Classify(Exception ex) => ex switch
    {
        ArgumentException => (StatusCodes.Status400BadRequest, "error.title.invalidRequest"),
        InvalidOperationException => (StatusCodes.Status503ServiceUnavailable, "error.title.notConfigured"),
        HueException => (StatusCodes.Status502BadGateway, "error.title.hue"),
        SpotifyException => (StatusCodes.Status502BadGateway, "error.title.spotify"),
        SoundboardException => (StatusCodes.Status503ServiceUnavailable, "error.title.soundboard"),
        AiProviderException => (StatusCodes.Status502BadGateway, "error.title.aiProvider"),
        HttpRequestException or TaskCanceledException =>
            (StatusCodes.Status502BadGateway, "error.title.spotifyUnreachable"),
        SocketException or IOException or TimeoutException =>
            (StatusCodes.Status504GatewayTimeout, "error.title.bulbUnreachable"),
        _ => (StatusCodes.Status500InternalServerError, "error.title.unexpected"),
    };

    /// <summary>The stable, <b>displayable</b> code to fold a caught failure into an <c>error:&lt;code&gt;</c>
    /// activation-status tail (rendered client-side as <c>L[code]</c>). Uses a specific arg-less
    /// <see cref="IErrorCode"/> when present; otherwise the category title key (always arg-less). An
    /// arg-bearing code is skipped because the 207 status string has no channel to carry its args.</summary>
    public static string DisplayCodeFor(Exception ex) =>
        ex is IErrorCode { Args.Count: 0 } ec ? ec.Code : Classify(ex).TitleKey;
}
