using System.Net;
using System.Text;

namespace InstaGrabber.Tests;

/// <summary>What a request looked like by the time it left the client.</summary>
/// <remarks>
/// Recorded eagerly rather than by holding the <see cref="HttpRequestMessage"/>, because
/// <see cref="HttpClient"/> disposes the message once the send completes.
/// </remarks>
public sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? AuthorizationScheme, string? AuthorizationValue)
{
    /// <summary>The <c>fields</c> parameter, still percent-decoded, or null if there wasn't one.</summary>
    public string? Fields
    {
        get
        {
            foreach (var pair in Uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var split = pair.Split('=', 2);
                if (split[0] == "fields")
                {
                    return split.Length == 2 ? System.Uri.UnescapeDataString(split[1]) : string.Empty;
                }
            }

            return null;
        }
    }
}

/// <summary>
/// Stands in for the network. The test project has no mocking library, so the HTTP layer is
/// faked the plain way: a handler that records what it was asked for and replays a canned
/// answer. Nothing in the suite may open a socket.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<CapturedRequest, HttpResponseMessage> _respond;

    public StubHttpMessageHandler(Func<CapturedRequest, HttpResponseMessage> respond) => _respond = respond;

    public List<CapturedRequest> Requests { get; } = [];

    public CapturedRequest? LastRequest => Requests.Count == 0 ? null : Requests[^1];

    /// <summary>Answers every request with the same status and JSON body.</summary>
    public static StubHttpMessageHandler Returning(HttpStatusCode status, string json) =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    public static StubHttpMessageHandler ReturningOk(string json) =>
        Returning(HttpStatusCode.OK, json);

    /// <summary>Fails the way a dead network does, before any response exists.</summary>
    public static StubHttpMessageHandler Throwing(Exception exception) =>
        new(_ => throw exception);

    /// <summary>Builds a client whose only reachable endpoint is this handler.</summary>
    public HttpClient AsClient() => new(this, disposeHandler: false);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var captured = new CapturedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter);

        Requests.Add(captured);
        return Task.FromResult(_respond(captured));
    }
}
