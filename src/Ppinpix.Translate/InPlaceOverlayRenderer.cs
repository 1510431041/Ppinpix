using System.Runtime.InteropServices;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Geometry;
using Ppinpix.Ocr.Models;
using SkiaSharp;

namespace Ppinpix.Translate;

/// <summary>
/// 将翻译段落原位回填渲染到截图帧上：
/// · 四周 2px 采样中位色（纯色底自动无缝融合，复杂/渐变底采用高斯模糊自适应半透托底）；
/// · 自动极性文字颜色保障（亮底深墨 / 暗底白字）；
/// · 二分查找最大可读字号并按中英文特性智能折行。
/// </summary>
public static class InPlaceOverlayRenderer
{
    public static ScreenFrame Render(
        ScreenFrame source,
        IReadOnlyList<(Paragraph Paragraph, string TranslatedText)> items)
    {
        if (source.Width <= 0 || source.Height <= 0 || source.Pixels.IsEmpty || items.Count == 0)
        {
            return source;
        }

        byte[] raw = source.Pixels.ToArray();
        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

        using var baseBmp = new SKBitmap(info);
        IntPtr dstPtr = baseBmp.GetPixels();
        int copyBytes = Math.Min(info.RowBytes, source.Stride);
        for (int y = 0; y < source.Height; y++)
        {
            Marshal.Copy(raw, y * source.Stride, dstPtr + y * info.RowBytes, copyBytes);
        }

        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.DrawBitmap(baseBmp, 0, 0);

        using var typeface = SKTypeface.FromFamilyName(
            "Microsoft YaHei UI",
            SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright) ?? SKTypeface.Default;

        foreach (var (para, translated) in items)
        {
            if (string.IsNullOrWhiteSpace(translated)) continue;

            var rect = ExpandAndClampRect(para.Box, padPx: 3, source.Width, source.Height);
            if (rect.Width < 4 || rect.Height < 4) continue;

            var (bgColor, variance) = SamplePerimeterMedian(raw, source.Stride, source.Width, source.Height, rect);

            if (variance > 30.0)
            {
                DrawBlurredBackdrop(canvas, baseBmp, rect, bgColor);
            }
            else
            {
                using var bgPaint = new SKPaint
                {
                    Color = bgColor,
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true,
                };
                canvas.DrawRoundRect(SKRect.Create(rect.X, rect.Y, rect.Width, rect.Height), 4f, 4f, bgPaint);
            }

            double lum = 0.299 * bgColor.Red + 0.587 * bgColor.Green + 0.114 * bgColor.Blue;
            SKColor ink = lum > 140
                ? new SKColor(0x1B, 0x1F, 0x24, 0xFF)
                : new SKColor(0xF2, 0xF4, 0xF7, 0xFF);

            DrawFittedText(canvas, typeface, translated.Trim(), rect, ink);
        }

        canvas.Flush();
        using var snap = surface.Snapshot();
        using var outBmp = SKBitmap.FromImage(snap);
        int outStride = source.Width * 4;
        byte[] outBytes = new byte[outStride * source.Height];
        IntPtr outPtr = outBmp.GetPixels();
        for (int y = 0; y < source.Height; y++)
        {
            Marshal.Copy(outPtr + y * outBmp.RowBytes, outBytes, y * outStride, outStride);
        }

        return new ScreenFrame(source.Bounds, outStride, outBytes);
    }

    private static void DrawBlurredBackdrop(SKCanvas canvas, SKBitmap sourceBmp, PxRect rect, SKColor tint)
    {
        int pad = 16;
        var expanded = PxRect.FromLtrb(
            Math.Max(0, rect.Left - pad),
            Math.Max(0, rect.Top - pad),
            Math.Min(sourceBmp.Width, rect.Right + pad),
            Math.Min(sourceBmp.Height, rect.Bottom + pad));

        using var subset = new SKBitmap();
        sourceBmp.ExtractSubset(subset, SKRectI.Create(expanded.X, expanded.Y, expanded.Width, expanded.Height));

        using var tmpSurface = SKSurface.Create(new SKImageInfo(expanded.Width, expanded.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var blurFilter = SKImageFilter.CreateBlur(10f, 10f, SKShaderTileMode.Clamp);
        using var blurPaint = new SKPaint { ImageFilter = blurFilter };
        tmpSurface.Canvas.DrawBitmap(subset, 0, 0, blurPaint);
        tmpSurface.Canvas.Flush();

        using var blurredSnap = tmpSurface.Snapshot();
        var srcCrop = SKRect.Create(rect.X - expanded.X, rect.Y - expanded.Y, rect.Width, rect.Height);
        var dstRect = SKRect.Create(rect.X, rect.Y, rect.Width, rect.Height);

        canvas.Save();
        canvas.ClipRoundRect(new SKRoundRect(dstRect, 4f, 4f));
        canvas.DrawImage(blurredSnap, srcCrop, dstRect);

        // 叠加微弱半透色调保障文字对比稳定
        using var tintPaint = new SKPaint { Color = tint.WithAlpha(165), Style = SKPaintStyle.Fill };
        canvas.DrawRect(dstRect, tintPaint);
        canvas.Restore();
    }

    private static (SKColor Color, double RmsDeviation) SamplePerimeterMedian(
        byte[] pixels, int stride, int imgW, int imgH, PxRect rect)
    {
        var rs = new List<byte>();
        var gs = new List<byte>();
        var bs = new List<byte>();

        void AddSample(int x, int y)
        {
            int cx = Math.Clamp(x, 0, imgW - 1);
            int cy = Math.Clamp(y, 0, imgH - 1);
            int idx = cy * stride + cx * 4;
            bs.Add(pixels[idx]);
            gs.Add(pixels[idx + 1]);
            rs.Add(pixels[idx + 2]);
        }

        int top = Math.Max(0, rect.Top - 2);
        int bottom = Math.Min(imgH - 1, rect.Bottom + 1);
        int stepX = Math.Max(1, rect.Width / 24);
        for (int x = rect.Left; x < rect.Right; x += stepX)
        {
            AddSample(x, top);
            AddSample(x, bottom);
        }

        int left = Math.Max(0, rect.Left - 2);
        int right = Math.Min(imgW - 1, rect.Right + 1);
        int stepY = Math.Max(1, rect.Height / 16);
        for (int y = rect.Top; y < rect.Bottom; y += stepY)
        {
            AddSample(left, y);
            AddSample(right, y);
        }

        if (rs.Count == 0) return (new SKColor(0x1F, 0x23, 0x29), 0);

        rs.Sort();
        gs.Sort();
        bs.Sort();
        byte mr = rs[rs.Count / 2];
        byte mg = gs[gs.Count / 2];
        byte mb = bs[bs.Count / 2];

        double sumSq = 0;
        for (int i = 0; i < rs.Count; i++)
        {
            double dr = rs[i] - mr;
            double dg = gs[i] - mg;
            double db = bs[i] - mb;
            sumSq += (dr * dr + dg * dg + db * db) / 3.0;
        }

        double rms = Math.Sqrt(sumSq / rs.Count);
        return (new SKColor(mr, mg, mb), rms);
    }

    private static void DrawFittedText(SKCanvas canvas, SKTypeface typeface, string text, PxRect rect, SKColor ink)
    {
        float padX = Math.Min(4f, Math.Max(1f, rect.Width * 0.06f));
        float padY = Math.Min(2f, Math.Max(0.5f, rect.Height * 0.08f));
        float maxW = Math.Max(10f, rect.Width - padX * 2);
        float maxH = Math.Max(8f, rect.Height - padY * 2);

        float low = Math.Clamp(Math.Min(9f, maxH * 0.72f), 6.5f, 9f);
        float high = Math.Min(48f, Math.Max(low, Math.Max(12f, maxH * 0.85f)));
        float bestSize = low;
        List<string> bestLines = WrapText(text, typeface, bestSize, maxW);

        for (int iter = 0; iter < 8; iter++)
        {
            float mid = (low + high) * 0.5f;
            var candidate = WrapText(text, typeface, mid, maxW);
            float lineHeight = mid * 1.25f;
            if (candidate.Count * lineHeight <= maxH + 1f)
            {
                bestSize = mid;
                bestLines = candidate;
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        using var font = new SKFont(typeface, bestSize) { Edging = SKFontEdging.Antialias };
        using var paint = new SKPaint { Color = ink, IsAntialias = true };
        var metrics = font.Metrics;
        float lh = bestSize * 1.25f;
        float glyphH = metrics.Descent - metrics.Ascent;
        float totalBlockH = bestLines.Count <= 1
            ? glyphH
            : (bestLines.Count - 1) * lh + glyphH;
        float topOffset = totalBlockH < rect.Height
            ? (rect.Height - totalBlockH) * 0.5f
            : padY;
        float y = rect.Top + Math.Max(padY * 0.5f, topOffset) - metrics.Ascent;

        canvas.Save();
        canvas.ClipRect(SKRect.Create(rect.X, rect.Y, rect.Width, rect.Height));
        for (int idx = 0; idx < bestLines.Count; idx++)
        {
            // metrics.Ascent 为负值，y + metrics.Ascent 为该行字形顶部坐标；首行始终尝试绘制
            if (idx > 0 && y + metrics.Ascent >= rect.Bottom) break;
            string line = bestLines[idx];
            if (line.Length > 0)
            {
                canvas.DrawText(line, rect.Left + padX, y, SKTextAlign.Left, font, paint);
            }
            y += lh;
        }
        canvas.Restore();
    }

    private static List<string> WrapText(string text, SKTypeface typeface, float fontSize, float maxWidth)
    {
        using var font = new SKFont(typeface, fontSize);
        var lines = new List<string>();

        foreach (string para in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (para.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            int pos = 0;
            while (pos < para.Length)
            {
                if (font.MeasureText(para.AsSpan(pos)) <= maxWidth)
                {
                    lines.Add(para[pos..].TrimEnd());
                    break;
                }

                // 二分确定最多放得下的长度
                int lo = 1, hi = para.Length - pos, fit = 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (font.MeasureText(para.AsSpan(pos, mid)) <= maxWidth)
                    {
                        fit = mid;
                        lo = mid + 1;
                    }
                    else
                    {
                        hi = mid - 1;
                    }
                }

                // 若切点落在英文单词中间，回退到空格处保持可读性
                int split = pos + fit;
                if (split < para.Length && IsAsciiLetter(para[split]) && IsAsciiLetter(para[split - 1]))
                {
                    int space = para.LastIndexOf(' ', split - 1, Math.Min(fit - 1, 24));
                    if (space > pos)
                    {
                        fit = space - pos + 1;
                    }
                }

                string segment = para.Substring(pos, fit).TrimEnd();
                lines.Add(segment);
                pos += fit;
                while (pos < para.Length && para[pos] == ' ') pos++;
            }
        }

        return lines;
    }

    private static bool IsAsciiLetter(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    private static PxRect ExpandAndClampRect(Quad box, int padPx, int maxW, int maxH) =>
        PxRect.FromLtrb(
            Math.Max(0, (int)Math.Floor(box.MinX) - padPx),
            Math.Max(0, (int)Math.Floor(box.MinY) - padPx),
            Math.Min(maxW, (int)Math.Ceiling(box.MaxX) + padPx),
            Math.Min(maxH, (int)Math.Ceiling(box.MaxY) + padPx));
}
