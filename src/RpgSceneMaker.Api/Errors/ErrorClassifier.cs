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
        NotFoundException => (StatusCodes.Status404NotFound, "error.title.notFound"),
        ConflictException => (StatusCodes.Status409Conflict, "error.title.conflict"),
        ArgumentException => (StatusCodes.Status400BadRequest, "error.title.invalidRequest"),
        InvalidOperationException => (StatusCodes.Status503ServiceUnavailable, "error.title.notConfigured"),
        HueException => (StatusCodes.Status502BadGateway, "error.title.hue"),
        SpotifyException => (StatusCodes.Status502BadGateway, "error.title.spotify"),
        FreesoundException => (StatusCodes.Status502BadGateway, "error.title.freesound"),
        SoundboardException => (StatusCodes.Status503ServiceUnavailable, "error.title.soundboard"),
        AiProviderException => (StatusCodes.Status502BadGateway, "error.title.aiProvider"),
        ImageSourceException => (StatusCodes.Status502BadGateway, "error.title.imageSource"),
        // Generic upstream/transport failure. Spotify and Hue wrap their own into SpotifyException/HueException
        // (handled above), so this arm only catches un-wrapped HTTP-client / AI-SDK transport faults — hence a
        // provider-neutral title rather than a Spotify-specific one.
        HttpRequestException or TaskCanceledException =>
            (StatusCodes.Status502BadGateway, "error.title.upstreamUnreachable"),
        // A configured storage file/dir is missing on disk (e.g. Results.File can't find a stored image).
        // These derive from IOException, so this MORE SPECIFIC arm must precede the socket/IO/timeout arm
        // below (which means "bulb unreachable"). A missing local file is an internal/config fault, not a
        // device timeout — an honest 500 with a generic storage title rather than a misleading 504 (#90).
        FileNotFoundException or DirectoryNotFoundException =>
            (StatusCodes.Status500InternalServerError, "error.title.storage"),
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
