using System.Net;

namespace RpgSceneMaker.Tests.Http;

/// <summary>One recorded outgoing request (method, absolute URI and body text).</summary>
public sealed record CapturedRequest(HttpMethod Method, Uri Uri, string Body);

/// <summary>
/// Test double for <see cref="HttpClient"/>: hands back canned responses in FIFO order and records
/// every request (method, URI and body) so tests can assert exactly what the client sent.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();
    public List<CapturedRequest> Requests { get; } = [];

    public FakeHttpMessageHandler Enqueue(HttpStatusCode status, string body = "", string contentType = "application/json")
    {
        _responses.Enqueue(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, contentType),
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body));

        if (_responses.Count == 0)
            throw new InvalidOperationException($"FakeHttpMessageHandler ran out of canned responses for {request.Method} {request.RequestUri}");
        return _responses.Dequeue();
    }
}
