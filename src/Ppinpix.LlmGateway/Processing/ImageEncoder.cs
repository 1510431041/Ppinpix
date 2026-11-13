using Ppinpix.Core.Capture;
using SkiaSharp;

namespace Ppinpix.LlmGateway.Processing;

/// <summary>图片编码与长边 2048px 自动缩放器，避免发送超大原图导致接口超限。</summary>
public static class ImageEncoder
{
    public const int MaxEdgePx = 2048;

    public static (string Base64, string MediaType, int Width, int Height) PrepareForUpload(
        byte[] rawImageBytes,
        int width = 0,
        int height = 0)
    {
        using var bmp = SKBitmap.Decode(rawImageBytes);
        if (bmp is null)
        {
            return (Convert.ToBase64String(rawImageBytes), "image/png", width, height);
        }

        return EncodeBitmap(bmp);
    }

    public static (string Base64, string MediaType, int Width, int Height) PrepareFromBgraFrame(ScreenFrame frame)
    {
        var info = new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bmp = new SKBitmap(info);
        byte[] pixels = frame.Pixels.ToArray();
        IntPtr dst = bmp.GetPixels();
        int rowBytes = Math.Min(info.RowBytes, frame.Stride);
        for (int y = 0; y < frame.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(pixels, y * frame.Stride, dst + y * info.RowBytes, rowBytes);
        }

        return EncodeBitmap(bmp);
    }

    private static (string Base64, string MediaType, int Width, int Height) EncodeBitmap(SKBitmap bmp)
    {
        SKBitmap working = bmp;
        bool ownsWorking = false;

        try
        {
            int maxEdge = Math.Max(bmp.Width, bmp.Height);
            if (maxEdge > MaxEdgePx && bmp.Width > 0 && bmp.Height > 0)
            {
                double ratio = (double)MaxEdgePx / maxEdge;
                int targetW = Math.Max(1, (int)Math.Round(bmp.Width * ratio));
                int targetH = Math.Max(1, (int)Math.Round(bmp.Height * ratio));
                var resized = bmp.Resize(
                    new SKImageInfo(targetW, targetH, bmp.ColorType, bmp.AlphaType),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                if (resized is not null)
                {
                    working = resized;
                    ownsWorking = true;
                }
            }

            using var image = SKImage.FromBitmap(working);
            using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
            if (pngData.Size <= 2 * 1024 * 1024)
            {
                return (Convert.ToBase64String(pngData.Span), "image/png", working.Width, working.Height);
            }

            // 超过 2MB 时自动转高效 JPEG，避免部分网关请求体拒绝
            using var jpgData = image.Encode(SKEncodedImageFormat.Jpeg, 90);
            return (Convert.ToBase64String(jpgData.Span), "image/jpeg", working.Width, working.Height);
        }
        finally
        {
            if (ownsWorking) working.Dispose();
        }
    }
}
