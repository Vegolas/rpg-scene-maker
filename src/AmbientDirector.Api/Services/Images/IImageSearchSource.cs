using AmbientDirector.Api.Contracts;

namespace AmbientDirector.Api.Services.Images;

/// <summary>
/// An image search + import provider (currently just Scryfall). The <c>/images</c> endpoints list the
/// registered sources, search one, and import a picked image URL — delegating here. A source both maps a
/// query to <see cref="ImageResultDto"/>s and owns the fetch of its own CDN, so import streams the bytes
/// under the same host allowlist that produced the URL.
/// </summary>
public interface IImageSearchSource
{
    /// <summary>Stable id used in the <c>?source=</c> query and the response envelope (e.g. "scryfall").</summary>
    string Id { get; }

    /// <summary>Human-facing name shown in the picker (e.g. "Scryfall").</summary>
    string Name { get; }

    /// <summary>English credit/legal line for the picker (source-specific text stays English per repo convention).</summary>
    string Attribution { get; }

    /// <summary>Run a search and map the source's results to the wire shape.</summary>
    Task<ImageSearchResponseDto> SearchAsync(string query, CancellationToken ct);

    /// <summary>True only for an <c>https</c> URL on one of this source's own image hosts — the import allowlist.</summary>
    bool CanFetch(Uri url);

    /// <summary>GET the image with <see cref="HttpCompletionOption.ResponseHeadersRead"/> so the endpoint can
    /// stream the body and enforce the size cap.</summary>
    Task<HttpResponseMessage> FetchImageAsync(Uri url, CancellationToken ct);
}
