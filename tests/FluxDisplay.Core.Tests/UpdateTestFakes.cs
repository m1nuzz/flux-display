using System.Net;

namespace FluxDisplay.Core.Tests;

// Shared fake transport: no test may touch the real GitHub.
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _fn;

    public int Calls;

    public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn)
    {
        _fn = fn;
    }

    public FakeHandler(HttpResponseMessage response)
        : this((_, _) => Task.FromResult(response))
    {
    }

    public FakeHandler(HttpStatusCode status, string? body = null)
        : this(Make(status, body))
    {
    }

    private static HttpResponseMessage Make(HttpStatusCode status, string? body)
    {
        var response = new HttpResponseMessage(status);
        if (body is not null)
        {
            response.Content = new StringContent(body);
        }

        return response;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        return _fn(request, ct);
    }
}
