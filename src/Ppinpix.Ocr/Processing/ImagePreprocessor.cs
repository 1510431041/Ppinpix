using System.Runtime.InteropServices;
using Ppinpix.Core.Capture;
using SkiaSharp;

namespace Ppinpix.Ocr.Processing;

/// <summary>将截图帧转为 Skia 位图并按长边上限做尺度缩放与反算映射。</summary>
internal static class ImagePreprocessor
{
    public const int DefaultMaxSidePx = 1600;

    public static SKBitmap ToSkBitmap(ScreenFrame frame)
    {
        var info = new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bmp = new SKBitmap(info);
        byte[] bytes = frame.Pixels.ToArray();
        IntPtr dst = bmp.GetPixels();
        int copyBytes = Math.Min(info.RowBytes, frame.Stride);
        for (int y = 0; y < frame.Height; y++)
        {
            Marshal.Copy(bytes, y * frame.Stride, dst + y * info.RowBytes, copyBytes);
        }
        return bmp;
    }

    public static (SKBitmap Bitmap, double ScaleX, double ScaleY, bool OwnsBitmap) PrepareForOcr(
        SKBitmap source,
        int maxSidePx = DefaultMaxSidePx)
    {
        int maxDim = Math.Max(source.Width, source.Height);
        if (maxSidePx <= 0 || maxDim <= maxSidePx || source.Width <= 0 || source.Height <= 0)
        {
            return (source, 1.0, 1.0, false);
        }

        double ratio = (double)maxSidePx / maxDim;
        int targetW = Math.Max(1, (int)Math.Round(source.Width * ratio));
        int targetH = Math.Max(1, (int)Math.Round(source.Height * ratio));

        var scaledInfo = new SKImageInfo(targetW, targetH, source.ColorType, source.AlphaType);
        var scaled = source.Resize(scaledInfo, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (scaled is null)
        {
            return (source, 1.0, 1.0, false);
        }

        return (scaled, (double)source.Width / targetW, (double)source.Height / targetH, true);
    }

    public static byte[] ExtractTightBgra(ScreenFrame frame)
    {
        int tightRow = frame.Width * 4;
        if (frame.Stride == tightRow)
        {
            return frame.Pixels.ToArray();
        }

        byte[] raw = frame.Pixels.ToArray();
        byte[] tight = new byte[tightRow * frame.Height];
        for (int y = 0; y < frame.Height; y++)
        {
            Buffer.BlockCopy(raw, y * frame.Stride, tight, y * tightRow, tightRow);
        }
        return tight;
    }
}
