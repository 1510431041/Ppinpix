using System.Text;
using Ppinpix.Core.Capture;
using Ppinpix.Ocr.Models;
using Ppinpix.Ocr.Processing;
using SkiaSharp;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace Ppinpix.Ocr.Engines;

/// <summary>基于 Windows 系统内置 Windows.Media.Ocr API 的纯离线系统 OCR 引擎（支持中文简体/英文等已装语言包）。</summary>
public sealed class WindowsOcrEngine : IOcrEngine
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OcrEngine? _engine;
    private string _loadedLangKey = string.Empty;
    private bool _disposed;

    public string Name => _engine is not null
        ? $"Windows OCR ({_engine.RecognizerLanguage.LanguageTag})"
        : "Windows OCR";

    public bool IsLoaded => _engine is not null;

    public bool IsAvailable => OcrEngine.AvailableRecognizerLanguages.Count > 0;

    public static IReadOnlyList<(string Tag, string DisplayName)> GetAvailableLanguages() =>
        OcrEngine.AvailableRecognizerLanguages
            .Select(l => (l.LanguageTag, l.DisplayName))
            .ToList();

    public async Task<IReadOnlyList<TextLine>> RecognizeAsync(ScreenFrame frame, OcrOptions options, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Pixels.IsEmpty)
        {
            return [];
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            var engine = EnsureEngine(options.Languages);
            if (engine is null)
            {
                throw new InvalidOperationException("当前系统未安装可用的 Windows OCR 语言包。");
            }

            int maxDim = Math.Min(options.MaxSidePx > 0 ? options.MaxSidePx : 2600, (int)OcrEngine.MaxImageDimension);
            using SKBitmap fullBmp = ImagePreprocessor.ToSkBitmap(frame);
            var (ocrBmp, scaleX, scaleY, owns) = ImagePreprocessor.PrepareForOcr(fullBmp, maxDim);
            try
            {
                byte[] tightBgra = CopySkBitmapBgra(ocrBmp);
                var ibuf = CryptographicBuffer.CreateFromByteArray(tightBgra);
                using var swBmp = SoftwareBitmap.CreateCopyFromBuffer(
                    ibuf,
                    BitmapPixelFormat.Bgra8,
                    ocrBmp.Width,
                    ocrBmp.Height,
                    BitmapAlphaMode.Premultiplied);

                var winResult = await engine.RecognizeAsync(swBmp).AsTask(ct).ConfigureAwait(false);
                if (winResult?.Lines is null || winResult.Lines.Count == 0)
                {
                    return [];
                }

                var result = new List<TextLine>(winResult.Lines.Count);
                foreach (var winLine in winResult.Lines)
                {
                    ct.ThrowIfCancellationRequested();
                    if (winLine.Words.Count == 0) continue;

                    var sb = new StringBuilder();
                    var chars = new List<CharBox>();
                    double minX = double.MaxValue, minY = double.MaxValue;
                    double maxX = double.MinValue, maxY = double.MinValue;
                    string? prevWord = null;

                    foreach (var w in winLine.Words)
                    {
                        string wt = w.Text ?? string.Empty;
                        if (wt.Length == 0) continue;

                        if (prevWord is not null && NeedsSpaceBetween(prevWord, wt))
                        {
                            sb.Append(' ');
                        }
                        sb.Append(wt);
                        prevWord = wt;

                        double x = w.BoundingRect.X * scaleX;
                        double y = w.BoundingRect.Y * scaleY;
                        double width = Math.Max(1.0, w.BoundingRect.Width * scaleX);
                        double height = Math.Max(1.0, w.BoundingRect.Height * scaleY);

                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x + width);
                        maxY = Math.Max(maxY, y + height);

                        double cw = width / wt.Length;
                        for (int i = 0; i < wt.Length; i++)
                        {
                            var cb = Quad.FromRect(x + i * cw, y, cw, height);
                            chars.Add(new CharBox(wt[i].ToString(), cb, 1.0f));
                        }
                    }

                    string text = sb.ToString().Trim();
                    if (text.Length == 0 || minX > maxX) continue;

                    result.Add(new TextLine(
                        text,
                        Quad.FromRect(minX, minY, maxX - minX, maxY - minY),
                        chars,
                        1.0f));
                }

                return result;
            }
            finally
            {
                if (owns) ocrBmp.Dispose();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Unload()
    {
        _gate.Wait();
        try
        {
            _engine = null;
            _loadedLangKey = string.Empty;
        }
        finally
        {
            _gate.Release();
        }
    }

    private OcrEngine? EnsureEngine(IReadOnlyList<string> preferredLanguages)
    {
        string key = string.Join(',', preferredLanguages);
        if (_engine is not null && _loadedLangKey == key) return _engine;

        var available = OcrEngine.AvailableRecognizerLanguages;
        Language? selected = null;

        foreach (string pref in preferredLanguages)
        {
            string normalized = pref.Trim().ToLowerInvariant();
            selected = available.FirstOrDefault(l =>
                l.LanguageTag.Equals(pref, StringComparison.OrdinalIgnoreCase) ||
                (normalized is "ch" or "zh" or "zh-cn" or "zh-hans" && l.LanguageTag.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase)) ||
                (normalized is "cht" or "zh-tw" or "zh-hant" && l.LanguageTag.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)) ||
                (normalized == "en" && l.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase)) ||
                (normalized == "ja" && l.LanguageTag.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) ||
                (normalized == "ko" && l.LanguageTag.StartsWith("ko", StringComparison.OrdinalIgnoreCase)));
            if (selected is not null) break;
        }

        selected ??= available.FirstOrDefault(l => l.LanguageTag.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase))
            ?? available.FirstOrDefault();

        _engine = selected is not null
            ? OcrEngine.TryCreateFromLanguage(selected)
            : OcrEngine.TryCreateFromUserProfileLanguages();
        _loadedLangKey = key;
        return _engine;
    }

    private static bool NeedsSpaceBetween(string prev, string next)
    {
        char a = prev[^1];
        char b = next[0];
        return IsAsciiWordChar(a) && IsAsciiWordChar(b);
    }

    private static bool IsAsciiWordChar(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c is '.' or ',' or '-' or '/' or ':' or ';';

    private static byte[] CopySkBitmapBgra(SKBitmap bmp)
    {
        int tightRow = bmp.Width * 4;
        byte[] result = new byte[tightRow * bmp.Height];
        IntPtr src = bmp.GetPixels();
        for (int y = 0; y < bmp.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(src + y * bmp.RowBytes, result, y * tightRow, tightRow);
        }
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unload();
        _gate.Dispose();
    }
}
