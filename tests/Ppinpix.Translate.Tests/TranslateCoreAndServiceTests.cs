using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Settings;
using Ppinpix.LlmGateway;
using Ppinpix.Ocr.Models;
using Xunit;

namespace Ppinpix.Translate.Tests;

public sealed class TranslateCoreAndServiceTests
{
    [Fact]
    public void UserInitiatedToken_ShouldBeValidInitiallyAndExpireAfterLifetime()
    {
        var fresh = UserInitiatedToken.IssueFromUserAction();
        Assert.True(fresh.IsValid);
        fresh.ThrowIfExpired();

        var expired = UserInitiatedToken.IssueAtForTest(DateTimeOffset.UtcNow - TimeSpan.FromSeconds(15));
        Assert.False(expired.IsValid);
        Assert.Throws<InvalidOperationException>(() => expired.ThrowIfExpired());
    }

    [Fact]
    public void LanguageDetector_ShouldDistinguishHanKanaHangulAndLatin()
    {
        Assert.Equal("zh-Hans", LanguageDetector.DetectPrimaryLanguageCode("这是一段中文测试文本。"));
        Assert.Equal("ja", LanguageDetector.DetectPrimaryLanguageCode("これは日本語のテストです。漢字も混ざります。"));
        Assert.Equal("ko", LanguageDetector.DetectPrimaryLanguageCode("안녕하세요 반갑습니다"));
        Assert.Equal("en", LanguageDetector.DetectPrimaryLanguageCode("Hello world from Ppinpix"));

        var (_, tgtZh, _, _) = LanguageDetector.Resolve("你好世界", "auto", "auto");
        Assert.Equal("en", tgtZh);

        var (_, tgtEn, _, _) = LanguageDetector.Resolve("Screenshot tool", "auto", "auto");
        Assert.Equal("zh-Hans", tgtEn);
    }

    [Fact]
    public void PromptBuilder_ShouldSupportCustomPlaceholdersAndDefaultMessages()
    {
        var customProfile = new LlmProfile
        {
            Prompt = "From {source_lang} to {target_lang}: {text}",
        };
        var customMsgs = PromptBuilder.BuildTextTranslation(customProfile, "Hi", "English", "Simplified Chinese");
        Assert.Single(customMsgs);
        Assert.Equal("From English to Simplified Chinese: Hi", customMsgs[0].Text);

        var defaultProfile = new LlmProfile();
        var defaultMsgs = PromptBuilder.BuildTextTranslation(defaultProfile, "Hi", "English", "Simplified Chinese");
        Assert.Equal(2, defaultMsgs.Count);
        Assert.Equal("system", defaultMsgs[0].Role);
        Assert.Equal("Hi", defaultMsgs[1].Text);
    }

    [Fact]
    public void OverlayTranslationParser_ShouldHandlePartialJsonAndPlainFallback()
    {
        string partial = "```json\n[{\"id\":0,\"text\":\"第一段译文\"},{\"id\":1,\"text\":\"进行中";
        var parsed = OverlayTranslationParser.ParseIncremental(partial, 2);

        Assert.Single(parsed);
        Assert.Equal("第一段译文", parsed[0]);

        string plain = "Paragraph one\n\nParagraph two";
        var fallback = OverlayTranslationParser.ParseIncremental(plain, 2);
        Assert.Equal("Paragraph one", fallback[0]);
        Assert.Equal("Paragraph two", fallback[1]);
    }

    [Fact]
    public void InPlaceOverlayRenderer_ShouldRenderHighContrastTextOnSolidBackground()
    {
        // 创建纯白背景图像，并在 (20,20,160,60) 放入一段测试文本位置
        const int w = 220, h = 100;
        int stride = w * 4;
        byte[] px = new byte[stride * h];
        for (int i = 0; i < px.Length; i += 4)
        {
            px[i] = 250;
            px[i + 1] = 250;
            px[i + 2] = 250;
            px[i + 3] = 255;
        }
        var source = new ScreenFrame(new PxRect(0, 0, w, h), stride, px);
        var para = new Paragraph("Hello", Quad.FromRect(20, 20, 160, 50), []);

        var rendered = InPlaceOverlayRenderer.Render(source, [(para, "你好世界测试")]);

        // 段落外像素 (5, 5) 保持未更改
        rendered.GetPixel(5, 5, out byte ob, out byte og, out byte or_, out _);
        Assert.Equal((250, 250, 250), (ob, og, or_));

        // 段落内至少应出现深色字迹像素（白底黑墨极性）
        bool hasDarkInk = false;
        for (int y = 22; y < 68 && !hasDarkInk; y++)
        {
            for (int x = 22; x < 178; x++)
            {
                rendered.GetPixel(x, y, out byte b, out byte g, out byte r, out _);
                if (r < 80 && g < 80 && b < 80)
                {
                    hasDarkInk = true;
                    break;
                }
            }
        }
        Assert.True(hasDarkInk);
    }

    [Fact]
    public void InPlaceOverlayRenderer_ShouldRenderCompactSingleLineBoxWithoutSkippingFirstLine()
    {
        // Height=18 simulates typical 14-18px UI button/menu labels that previously tripped the Ascent check on line 0
        const int w = 140, h = 48;
        int stride = w * 4;
        byte[] px = new byte[stride * h];
        for (int i = 0; i < px.Length; i += 4)
        {
            px[i] = 248;
            px[i + 1] = 248;
            px[i + 2] = 248;
            px[i + 3] = 255;
        }
        var source = new ScreenFrame(new PxRect(0, 0, w, h), stride, px);
        var para = new Paragraph("Cancel", Quad.FromRect(16, 15, 70, 16), []);

        var rendered = InPlaceOverlayRenderer.Render(source, [(para, "取消")]);

        bool hasDarkInk = false;
        for (int y = 14; y < 34 && !hasDarkInk; y++)
        {
            for (int x = 14; x < 88; x++)
            {
                rendered.GetPixel(x, y, out byte b, out byte g, out byte r, out _);
                if (r < 90 && g < 90 && b < 90)
                {
                    hasDarkInk = true;
                    break;
                }
            }
        }
        Assert.True(hasDarkInk);
    }

    [Fact]
    public async Task TranslateOverlayStreamAsync_ShouldStreamIncrementalOverlayFrames()
    {
        string sse =
            "data: {\"choices\":[{\"delta\":{\"content\":\"[{\\\"id\\\":0,\\\"text\\\":\\\"设置\\\"},\"},\"finish_reason\":null}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"{\\\"id\\\":1,\\\"text\\\":\\\"取消\\\"}]\"},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: [DONE]\n\n";

        var handler = new CountingSseHandler(() =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(sse))),
            };
            resp.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return resp;
        });

        var svc = new TranslateService(ocrService: null, new LlmProviderFactory(handler));
        var profile = new LlmProfile { BaseUrl = "https://mock.translate.local/v1", Model = "m1" };

        const int w = 160, h = 90;
        byte[] px = Enumerable.Repeat((byte)255, w * 4 * h).ToArray();
        var frame = new ScreenFrame(new PxRect(0, 0, w, h), w * 4, px);
        var ocr = new OcrResult(
            [],
            [
                new Paragraph("Settings", Quad.FromRect(10, 10, 90, 22), []),
                new Paragraph("Cancel", Quad.FromRect(10, 44, 90, 22), []),
            ],
            [],
            "TestOcr",
            10);

        var updates = new List<OverlayProgress>();
        await foreach (var prog in svc.TranslateOverlayStreamAsync(
            UserInitiatedToken.IssueFromUserAction(), frame, ocr, profile, "auto", "zh-Hans"))
        {
            updates.Add(prog);
        }

        Assert.NotEmpty(updates);
        var final = updates[^1];
        Assert.True(final.IsCompleted);
        Assert.Equal("设置", final.TranslatedParagraphs[0]);
        Assert.Equal("取消", final.TranslatedParagraphs[1]);
        Assert.Equal(w, final.RenderedFrame.Width);
        Assert.Equal(h, final.RenderedFrame.Height);

        var expired = UserInitiatedToken.IssueAtForTest(DateTimeOffset.UtcNow - TimeSpan.FromSeconds(15));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in svc.TranslateOverlayStreamAsync(expired, frame, ocr, profile, "auto", "zh-Hans")) { }
        });
    }

    [Fact]
    public async Task TranslateService_ShouldEnforceTokenExpiryBeforeSendingAndStreamValidResponses()
    {
        int calls = 0;
        var handler = new CountingSseHandler(() =>
        {
            calls++;
            string sse =
                "data: {\"choices\":[{\"delta\":{\"content\":\"你好\"},\"finish_reason\":null}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"content\":\"世界\"},\"finish_reason\":\"stop\"}]}\n\n" +
                "data: [DONE]\n\n";
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(sse))),
            };
            resp.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return resp;
        });

        var svc = new TranslateService(ocrService: null, new LlmProviderFactory(handler));
        var profile = new LlmProfile { BaseUrl = "https://mock.translate.local/v1", Model = "m1" };

        var expired = UserInitiatedToken.IssueAtForTest(DateTimeOffset.UtcNow - TimeSpan.FromSeconds(20));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in svc.TranslateTextStreamAsync(expired, "Hello", profile, "auto", "auto")) { }
        });
        Assert.Equal(0, calls);

        var valid = UserInitiatedToken.IssueFromUserAction();
        string finalAcc = string.Empty;
        await foreach (var prog in svc.TranslateTextStreamAsync(valid, "Hello world", profile, "auto", "zh-Hans"))
        {
            finalAcc = prog.AccumulatedText;
        }
        Assert.Equal(1, calls);
        Assert.Equal("你好世界", finalAcc);
    }

    private sealed class CountingSseHandler(Func<HttpResponseMessage> factory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(factory());
    }
}
