using System.Net;
using System.Net.Http;
using System.Text;
using Ppinpix.LlmGateway.Sse;
using Xunit;

namespace Ppinpix.LlmGateway.Tests;

internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    public List<(HttpMethod Method, Uri? Url, Dictionary<string, string> Headers, string? Body)> RecordedRequests { get; } = [];

    public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in request.Headers)
        {
            headers[h.Key] = string.Join(", ", h.Value);
        }
        string? body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        RecordedRequests.Add((request.Method, request.RequestUri, headers, body));
        return await _responder(request, cancellationToken);
    }

    public static HttpResponseMessage SseResponse(string ssePayload)
    {
        var resp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(ssePayload))),
        };
        resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        return resp;
    }

    public static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
    {
        var resp = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return resp;
    }
}

public sealed class SseStreamParserTests
{
    [Fact]
    public async Task ReadEventsAsync_ShouldHandleCrlfCommentsMultilineAndDone()
    {
        string raw =
            ": keepalive heartbeat\r\n" +
            "\r\n" +
            "event: content_block_delta\r\n" +
            "data: {\"a\":1}\r\n" +
            "\r\n" +
            "data: line1\n" +
            "data: line2\n\n" +
            "data: [DONE]\n\n";

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        var events = new List<SseEvent>();
        await foreach (var ev in SseStreamParser.ReadEventsAsync(ms))
        {
            events.Add(ev);
        }

        Assert.Equal(3, events.Count);
        Assert.Equal("content_block_delta", events[0].EventType);
        Assert.Equal("{\"a\":1}", events[0].Data);
        Assert.Null(events[1].EventType);
        Assert.Equal("line1\nline2", events[1].Data);
        Assert.Equal("[DONE]", events[2].Data);
    }
}
