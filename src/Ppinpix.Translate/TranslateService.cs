using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Settings;
using Ppinpix.LlmGateway;
using Ppinpix.LlmGateway.Models;
using Ppinpix.Ocr;
using Ppinpix.Ocr.Models;
using SkiaSharp;

namespace Ppinpix.Translate;

/// <summary>翻译流式响应增量。</summary>
public sealed record TranslationProgress(
    string SourceText,
    string SourceLangCode,
    string TargetLangCode,
    string DeltaText,
    string AccumulatedText,
    bool IsCompleted);

/// <summary>原位覆盖翻译实时渲染进度。</summary>
public sealed record OverlayProgress(
    ScreenFrame RenderedFrame,
    IReadOnlyDictionary<int, string> TranslatedParagraphs,
    bool IsCompleted);

/// <summary>
/// 截图翻译核心协调器：
/// · 强制所有外发翻译方法要求 <see cref="UserInitiatedToken"/>（编译器级防后台擅自发送保护）；
/// · 支持「先本地 OCR 再发文本」与「多模态直接发图」两条管线；
/// · 支持实时流式对照输出与原位渲染回填。
/// </summary>
public sealed class TranslateService
{
    private readonly OcrService? _ocrService;
    private readonly LlmProviderFactory _providerFactory;

    public TranslateService(OcrService? ocrService = null, LlmProviderFactory? providerFactory = null)
    {
        _ocrService = ocrService;
        _providerFactory = providerFactory ?? new LlmProviderFactory();
    }

    public async Task<string> ExtractSourceTextAsync(ScreenFrame frame, CancellationToken ct = default)
    {
        if (_ocrService is null)
        {
            throw new InvalidOperationException("离线 OCR 服务未就绪。");
        }
        var ocr = await _ocrService.RecognizeAsync(frame, ct).ConfigureAwait(false);
        return ocr.FullText;
    }

    public async IAsyncEnumerable<TranslationProgress> TranslateTextStreamAsync(
        UserInitiatedToken userToken,
        string sourceText,
        LlmProfile profile,
        string configuredSourceLang,
        string configuredTargetLang,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        userToken.ThrowIfExpired();

        if (string.IsNullOrWhiteSpace(sourceText))
        {
            yield break;
        }

        var (srcCode, tgtCode, srcPrompt, tgtPrompt) = LanguageDetector.Resolve(
            sourceText, configuredSourceLang, configuredTargetLang);

        var messages = PromptBuilder.BuildTextTranslation(profile, sourceText, srcPrompt, tgtPrompt);
        var provider = _providerFactory.Create(profile.Protocol);
        var req = new LlmRequest(profile, messages);

        var acc = new StringBuilder();
        await foreach (var chunk in provider.StreamCompleteAsync(req, ct).ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(chunk.DeltaText))
            {
                acc.Append(chunk.DeltaText);
            }

            yield return new TranslationProgress(
                sourceText,
                srcCode,
                tgtCode,
                chunk.DeltaText,
                acc.ToString(),
                chunk.IsCompleted);
        }

        if (acc.Length == 0 || acc.ToString().Length > 0)
        {
            yield return new TranslationProgress(sourceText, srcCode, tgtCode, string.Empty, acc.ToString(), true);
        }
    }

    public async IAsyncEnumerable<TranslationProgress> TranslateImageDirectlyStreamAsync(
        UserInitiatedToken userToken,
        ScreenFrame frame,
        LlmProfile profile,
        string configuredTargetLang,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        userToken.ThrowIfExpired();

        byte[] png = EncodeFrameToPng(frame);
        string tgtPrompt = string.IsNullOrWhiteSpace(configuredTargetLang) || configuredTargetLang == "auto"
            ? "auto"
            : LanguageDetector.GetPromptName(configuredTargetLang);

        var messages = PromptBuilder.BuildImageTranslation(profile, png, frame.Width, frame.Height, tgtPrompt);
        var provider = _providerFactory.Create(profile.Protocol);
        var req = new LlmRequest(profile, messages);

        var acc = new StringBuilder();
        await foreach (var chunk in provider.StreamCompleteAsync(req, ct).ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(chunk.DeltaText))
            {
                acc.Append(chunk.DeltaText);
            }

            yield return new TranslationProgress(
                "[图片直接翻译]",
                "auto",
                configuredTargetLang,
                chunk.DeltaText,
                acc.ToString(),
                chunk.IsCompleted);
        }
    }

    public async IAsyncEnumerable<OverlayProgress> TranslateOverlayStreamAsync(
        UserInitiatedToken userToken,
        ScreenFrame frame,
        OcrResult ocrResult,
        LlmProfile profile,
        string configuredSourceLang,
        string configuredTargetLang,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        userToken.ThrowIfExpired();

        if (ocrResult.Paragraphs.Count == 0)
        {
            yield break;
        }

        var paragraphs = ocrResult.Paragraphs;
        string allText = ocrResult.FullText;
        var (_, _, _, tgtPrompt) = LanguageDetector.Resolve(allText, configuredSourceLang, configuredTargetLang);

        byte[]? pngImage = profile.InputMode == TranslationInputMode.ImageDirectly && profile.SupportsVision
            ? EncodeFrameToPng(frame)
            : null;
        var messages = PromptBuilder.BuildOverlayParagraphTranslation(
            paragraphs,
            tgtPrompt,
            pngImage,
            frame.Width,
            frame.Height);
        var provider = _providerFactory.Create(profile.Protocol);
        var req = new LlmRequest(profile, messages);

        var acc = new StringBuilder();
        int lastCount = 0;

        await foreach (var chunk in provider.StreamCompleteAsync(req, ct).ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(chunk.DeltaText))
            {
                acc.Append(chunk.DeltaText);
            }

            var parsed = OverlayTranslationParser.ParseIncremental(acc.ToString(), paragraphs.Count);
            if (parsed.Count > lastCount || chunk.IsCompleted)
            {
                lastCount = parsed.Count;
                var pairs = parsed
                    .OrderBy(kv => kv.Key)
                    .Select(kv => (paragraphs[kv.Key], kv.Value))
                    .ToList();

                var rendered = pairs.Count > 0
                    ? InPlaceOverlayRenderer.Render(frame, pairs)
                    : frame;

                yield return new OverlayProgress(rendered, parsed, chunk.IsCompleted);
            }
        }
    }

    public async Task<IReadOnlyList<string>> FetchModelsAsync(
        UserInitiatedToken userToken,
        LlmProfile profile,
        CancellationToken ct = default)
    {
        userToken.ThrowIfExpired();
        var provider = _providerFactory.Create(profile.Protocol);
        return await provider.ListModelsAsync(profile, ct).ConfigureAwait(false);
    }

    private static byte[] EncodeFrameToPng(ScreenFrame frame)
    {
        var info = new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bmp = new SKBitmap(info);
        byte[] raw = frame.Pixels.ToArray();
        IntPtr dst = bmp.GetPixels();
        int copyBytes = Math.Min(info.RowBytes, frame.Stride);
        for (int y = 0; y < frame.Height; y++)
        {
            Marshal.Copy(raw, y * frame.Stride, dst + y * info.RowBytes, copyBytes);
        }
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
