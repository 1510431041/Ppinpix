using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ppinpix.Core;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Settings;
using SkiaSharp;

namespace Ppinpix.Capture.Output;

/// <summary>截图结果输出：复制到剪贴板、保存为文件、格式编解码。</summary>
public static class CaptureOutput
{
    private const int ClipboardRetryCount = 5;
    private const int ClipboardRetryDelayMs = 40;

    /// <summary>把帧转成可跨线程使用的 WPF 位图。调用方得到的对象是冻结的。</summary>
    public static BitmapSource ToBitmapSource(ScreenFrame frame)
    {
        if (frame.Bounds.IsEmpty) return BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);

        var bitmap = BitmapSource.Create(
            frame.Width,
            frame.Height,
            96, 96,
            PixelFormats.Bgra32,
            null,
            frame.Pixels.ToArray(),
            frame.Stride);

        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>把 WPF 位图转为只读内存帧（BGRA32）。</summary>
    public static ScreenFrame FromBitmapSource(BitmapSource source, PxPoint origin = default)
    {
        BitmapSource bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int w = Math.Max(1, bgra.PixelWidth);
        int h = Math.Max(1, bgra.PixelHeight);
        int stride = w * 4;
        byte[] pixels = new byte[stride * h];
        bgra.CopyPixels(pixels, stride, 0);
        return new ScreenFrame(new PxRect(origin.X, origin.Y, w, h), stride, pixels);
    }

    /// <summary>把图片文件（支持 PNG / JPG / WebP / BMP）读取为 <see cref="ScreenFrame"/>。</summary>
    public static ScreenFrame? LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            using var stream = File.OpenRead(path);
            using var skBitmap = SKBitmap.Decode(stream);
            if (skBitmap is null || skBitmap.Width <= 0 || skBitmap.Height <= 0) return null;

            using var bgra = skBitmap.ColorType == SKColorType.Bgra8888
                ? null
                : skBitmap.Copy(SKColorType.Bgra8888);

            var target = bgra ?? skBitmap;
            byte[] pixels = target.Bytes;
            int stride = target.RowBytes;
            return new ScreenFrame(new PxRect(0, 0, target.Width, target.Height), stride, pixels);
        }
        catch (Exception ex)
        {
            Log.Warn($"加载图片文件失败：{path}", ex);
            return null;
        }
    }

    /// <summary>
    /// 复制到剪贴板。同时放入 CF_BITMAP/CF_DIB（WPF 默认）与 PNG 流，
    /// 这样粘贴到 Office、画图、浏览器、聊天工具里都能拿到合适的格式。
    /// </summary>
    public static bool CopyToClipboard(ScreenFrame frame)
    {
        var bitmap = ToBitmapSource(frame);

        var data = new DataObject();
        data.SetImage(bitmap);

        MemoryStream? pngStream = null;
        try
        {
            pngStream = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(pngStream);
            pngStream.Position = 0;
            // Windows 剪贴板的 "PNG" 格式必须是原始字节流（MemoryStream），不能传 byte[]。
            // 传 byte[] 会被 WPF 走序列化包装（或 .NET 10 禁用了 BinaryFormatter 导致异常/格式损坏）。
            data.SetData("PNG", pngStream, autoConvert: false);
        }
        catch (Exception ex)
        {
            Log.Warn("附加 PNG 剪贴板格式失败，仅提供 DIB。", ex);
            pngStream?.Dispose();
            pngStream = null;
        }

        try
        {
            // 剪贴板经常被其它进程短暂占用，需要重试。
            for (int attempt = 0; attempt < ClipboardRetryCount; attempt++)
            {
                try
                {
                    if (pngStream is not null) pngStream.Position = 0;
                    Clipboard.SetDataObject(data, copy: true);
                    return true;
                }
                catch (Exception ex) when (attempt < ClipboardRetryCount - 1)
                {
                    Log.Warn($"复制到剪贴板失败，正在重试（{attempt + 1}/{ClipboardRetryCount}）。", ex);
                    Thread.Sleep(ClipboardRetryDelayMs);
                }
                catch (Exception ex)
                {
                    Log.Error("复制到剪贴板失败。", ex);
                    return false;
                }
            }

            return false;
        }
        finally
        {
            pngStream?.Dispose();
        }
    }

    /// <summary>按设置保存图片，返回完整路径。</summary>
    public static string Save(ScreenFrame frame, SaveSettings settings)
    {
        string path = BuildFilePath(settings, frame.Bounds);
        SaveToPath(frame, path, settings.Format, settings.JpegQuality);
        return path;
    }

    /// <summary>保存帧到指定路径。</summary>
    public static void SaveToPath(ScreenFrame frame, string path, ImageFormat format, int quality = 92)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (format == ImageFormat.Webp)
        {
            byte[] webp = EncodeWebp(frame, quality);
            File.WriteAllBytes(path, webp);
            return;
        }

        var bitmap = ToBitmapSource(frame);
        BitmapEncoder encoder = format switch
        {
            ImageFormat.Jpeg => new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 10, 100) },
            _ => new PngBitmapEncoder(),
        };

        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>按模板生成不重名的文件路径。</summary>
    public static string BuildFilePath(SaveSettings settings, PxRect region)
    {
        string name = ExpandTemplate(settings.FileNameTemplate, DateTime.Now, region);
        string extension = settings.Format switch
        {
            ImageFormat.Jpeg => ".jpg",
            ImageFormat.Webp => ".webp",
            _ => ".png",
        };
        string directory = string.IsNullOrWhiteSpace(settings.Directory) ? Paths.ScreenshotsDirectory : settings.Directory;

        string path = Path.Combine(directory, name + extension);
        for (int i = 2; File.Exists(path) && i < 1000; i++)
        {
            path = Path.Combine(directory, $"{name}({i}){extension}");
        }

        return path;
    }

    /// <summary>展开文件名模板中的时间与尺寸占位符。</summary>
    public static string ExpandTemplate(string template, DateTime now, PxRect region)
    {
        if (string.IsNullOrWhiteSpace(template)) template = "Ppinpix_{yyyy}{MM}{dd}_{HH}{mm}{ss}";

        return template
            .Replace("{yyyy}", now.ToString("yyyy"), StringComparison.Ordinal)
            .Replace("{yy}", now.ToString("yy"), StringComparison.Ordinal)
            .Replace("{MM}", now.ToString("MM"), StringComparison.Ordinal)
            .Replace("{dd}", now.ToString("dd"), StringComparison.Ordinal)
            .Replace("{HH}", now.ToString("HH"), StringComparison.Ordinal)
            .Replace("{mm}", now.ToString("mm"), StringComparison.Ordinal)
            .Replace("{ss}", now.ToString("ss"), StringComparison.Ordinal)
            .Replace("{w}", region.Width.ToString(), StringComparison.Ordinal)
            .Replace("{h}", region.Height.ToString(), StringComparison.Ordinal);
    }

    /// <summary>把 BGRA 帧转成 PNG 字节流（翻译发图、贴图序列化都会用到）。</summary>
    public static byte[] EncodePng(ScreenFrame frame)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(ToBitmapSource(frame)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>把 BGRA 帧编码为 WebP 字节流。</summary>
    public static byte[] EncodeWebp(ScreenFrame frame, int quality = 90)
    {
        if (frame.Bounds.IsEmpty) return [];

        var info = new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        byte[] src = frame.Pixels.ToArray();
        System.Runtime.InteropServices.Marshal.Copy(src, 0, bitmap.GetPixels(), Math.Min(src.Length, bitmap.ByteCount));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, Math.Clamp(quality, 10, 100));
        return data.ToArray();
    }
}
