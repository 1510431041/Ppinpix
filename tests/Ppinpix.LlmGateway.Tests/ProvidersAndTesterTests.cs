using System.Net;
using System.Net.Http;
using System.Text;
using Ppinpix.Core;
using Ppinpix.Core.Security;
using Ppinpix.Core.Settings;
using Ppinpix.LlmGateway.Models;
using Xunit;

namespace Ppinpix.LlmGateway.Tests;

public sealed class ProvidersAndTesterTests
{
    [Fact]
    public async Task OpenAiProvider_StreamVisionAndModels_ShouldParseSseAndRedactSecrets()
    {
        const string fakeKey = "sk-openai-supersecret-987654";
        string sse =
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hello \"},\"finish_reason\":null}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"world\"},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: [DONE]\n\n";

        var mock = new MockHttpMessageHandler((req, _) =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsoluteUri.EndsWith("/models"))
            {
                return Task.FromResult(MockHttpMessageHandler.JsonResponse(
                    HttpStatusCode.OK,
                    """{"data":[{"id":"gpt-4o"},{"id":"deepseek-chat"}]}"""));
            }
            return Task.FromResult(MockHttpMessageHandler.SseResponse(sse));
        });

        var profile = new LlmProfile
        {
            Protocol = LlmProtocol.OpenAiCompatible,
            BaseUrl = "https://mock.llm.local/v1",
            Model = "gpt-4o",
            EncryptedApiKey = SecretStore.Protect(fakeKey),
            Stream = true,
            SupportsVision = true,
        };

        var factory = new LlmProviderFactory(mock);
        var provider = factory.Create(LlmProtocol.OpenAiCompatible);

        var sb = new StringBuilder();
        byte[] validPng = CreateTinyPngBytes();
        var reqMsg = new LlmRequest(profile, [new LlmMessage("user", "Translate", validPng, 4, 4)]);
        await foreach (var c in provider.StreamCompleteAsync(reqMsg))
        {
            sb.Append(c.DeltaText);
        }

        Assert.Equal("Hello world", sb.ToString());
        Assert.Equal("https://mock.llm.local/v1/chat/completions", mock.RecordedRequests[0].Url!.ToString());
        Assert.Equal($"Bearer {fakeKey}", mock.RecordedRequests[0].Headers["Authorization"]);
        Assert.Contains("data:image/png;base64,", mock.RecordedRequests[0].Body!);
        Assert.DoesNotContain(fakeKey, Log.Redact(mock.RecordedRequests[0].Headers["Authorization"]));

        var models = await provider.ListModelsAsync(profile);
        Assert.Equal(["deepseek-chat", "gpt-4o"], models);
    }

    [Fact]
    public async Task AnthropicAndGeminiProviders_ShouldUseNativeHeadersAndParseSse()
    {
        var handler = new MockHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("/messages"))
            {
                string sse =
                    "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"delta\":{\"text\":\"你好\"}}\n\n" +
                    "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n";
                return Task.FromResult(MockHttpMessageHandler.SseResponse(sse));
            }
            else
            {
                string sse =
                    "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"世界\"}]}}]}\n\n";
                return Task.FromResult(MockHttpMessageHandler.SseResponse(sse));
            }
        });

        var factory = new LlmProviderFactory(handler);

        var antProfile = new LlmProfile
        {
            Protocol = LlmProtocol.AnthropicMessages,
            BaseUrl = "https://mock.anthropic.local/v1",
            Model = "claude-sonnet-4",
            EncryptedApiKey = SecretStore.Protect("ant-key-xyz"),
        };
        var antOut = new StringBuilder();
        await foreach (var c in factory.Create(LlmProtocol.AnthropicMessages)
            .StreamCompleteAsync(new LlmRequest(antProfile, [new LlmMessage("user", "hi")])))
        {
            antOut.Append(c.DeltaText);
        }
        Assert.Equal("你好", antOut.ToString());
        Assert.Equal("ant-key-xyz", handler.RecordedRequests[0].Headers["x-api-key"]);

        var gemProfile = new LlmProfile
        {
            Protocol = LlmProtocol.Gemini,
            BaseUrl = "https://mock.gemini.local/v1beta",
            Model = "gemini-2.5-flash",
            EncryptedApiKey = SecretStore.Protect("gem-key-123"),
        };
        var gemOut = new StringBuilder();
        await foreach (var c in factory.Create(LlmProtocol.Gemini)
            .StreamCompleteAsync(new LlmRequest(gemProfile, [new LlmMessage("user", "hi")])))
        {
            gemOut.Append(c.DeltaText);
        }
        Assert.Equal("世界", gemOut.ToString());
        Assert.Equal("gem-key-123", handler.RecordedRequests[1].Headers["x-goog-api-key"]);
        Assert.DoesNotContain("gem-key-123", handler.RecordedRequests[1].Url!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{}", LlmErrorKind.AuthFailed)]
    [InlineData(HttpStatusCode.NotFound, """{"error":{"message":"model not found"}}""", LlmErrorKind.ModelNotFound)]
    [InlineData(HttpStatusCode.NotFound, "404 page not found", LlmErrorKind.EndpointNotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, "{}", LlmErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, "{}", LlmErrorKind.ServerError)]
    public async Task ConnectionTester_ShouldClassifyHttpErrorsWithoutLeakingSecrets(
        HttpStatusCode status,
        string body,
        LlmErrorKind expectedKind)
    {
        const string secret = "sk-secret-do-not-leak-8888";
        var mock = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(MockHttpMessageHandler.JsonResponse(status, body.Replace("{}", $$$"""{"error":"bad Bearer {{{secret}}}"}"""))));

        var tester = new ConnectionTester(mock);
        var profile = new LlmProfile
        {
            BaseUrl = "https://mock.llm.local/v1",
            Model = "gpt-4o",
            EncryptedApiKey = SecretStore.Protect(secret),
        };

        var res = await tester.TestAsync(profile);
        Assert.False(res.Success);
        Assert.Equal(expectedKind, res.ErrorKind);
        Assert.DoesNotContain(secret, res.Message);
    }

    [Fact]
    public async Task ConnectionTester_TimeoutException_ShouldReportTimeout()
    {
        var mock = new MockHttpMessageHandler((_, _) => throw new TaskCanceledException("Simulated HTTP client timeout"));
        var tester = new ConnectionTester(mock);
        var profile = new LlmProfile { BaseUrl = "https://mock.llm.local/v1", Model = "m1", TimeoutSeconds = 5 };

        var res = await tester.TestAsync(profile);
        Assert.False(res.Success);
        Assert.Equal(LlmErrorKind.Timeout, res.ErrorKind);
    }

    private static byte[] CreateTinyPngBytes()
    {
        using var bmp = new SkiaSharp.SKBitmap(4, 4, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
        bmp.Erase(new SkiaSharp.SKColor(0x2F, 0x80, 0xFF));
        using var img = SkiaSharp.SKImage.FromBitmap(bmp);
        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
