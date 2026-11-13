using System.Runtime.InteropServices;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Geometry;
using Ppinpix.Ocr.Barcode;
using Ppinpix.Ocr.Models;
using Ppinpix.Ocr.Processing;
using Ppinpix.Core.Settings;
using SkiaSharp;
using Xunit;
using ZXing;
using ZXing.QrCode;

namespace Ppinpix.Ocr.Tests;

public sealed class ZxingAndPreprocessTests
{
    [Fact]
    public void PrepareForOcr_LargeImage_ShouldDownscaleAndReturnInverseFactors()
    {
        using var big = new SKBitmap(3200, 1600, SKColorType.Bgra8888, SKAlphaType.Premul);
        var (scaled, sx, sy, owns) = ImagePreprocessor.PrepareForOcr(big, maxSidePx: 1600);

        try
        {
            Assert.True(owns);
            Assert.Equal(1600, scaled.Width);
            Assert.Equal(800, scaled.Height);
            Assert.Equal(2.0, sx, 3);
            Assert.Equal(2.0, sy, 3);
        }
        finally
        {
            if (owns) scaled.Dispose();
        }
    }

    [Fact]
    public void ZxingDecoder_SyntheticQrFrame_ShouldDecodePayloadOffline()
    {
        const string expectedPayload = "https://ppinpix.local/offline-qr-2026";
        var writer = new QRCodeWriter();
        var matrix = writer.encode(expectedPayload, BarcodeFormat.QR_CODE, 200, 200);

        int w = matrix.Width;
        int h = matrix.Height;
        int stride = w * 4;
        byte[] bgra = new byte[stride * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                byte v = matrix[x, y] ? (byte)0 : (byte)255;
                int off = y * stride + x * 4;
                bgra[off] = v;
                bgra[off + 1] = v;
                bgra[off + 2] = v;
                bgra[off + 3] = 255;
            }
        }

        var frame = new ScreenFrame(new PxRect(0, 0, w, h), stride, bgra);
        var decoded = ZxingDecoder.Decode(frame);

        var qr = Assert.Single(decoded);
        Assert.Equal(expectedPayload, qr.Content);
        Assert.Equal(BarcodeKind.QrCode, qr.Kind);
    }

    [Fact]
    public async Task OcrService_RapidOcr_RecognizesRenderedTextAndSupportsUnload()
    {
        const int w = 520, h = 120;
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        using var typeface = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                             ?? SKTypeface.Default;
        using var font = new SKFont(typeface, 42f);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        surface.Canvas.DrawText("PPINPIX TEST", 36, 76, SKTextAlign.Left, font, paint);
        surface.Canvas.Flush();

        using var img = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(img);
        int stride = w * 4;
        byte[] bgra = new byte[stride * h];
        for (int row = 0; row < h; row++)
        {
            Marshal.Copy(bmp.GetPixels() + row * bmp.RowBytes, bgra, row * stride, stride);
        }

        var settings = new OcrSettings
        {
            Engine = OcrEngineKind.RapidOcr,
            DetectBarcodes = false,
            UnloadAfterIdleMinutes = 0,
        };
        using var svc = new OcrService(() => settings);
        var result = await svc.RecognizeAsync(new ScreenFrame(new PxRect(0, 0, w, h), stride, bgra));

        Assert.Contains("PPINPIX", result.FullText, StringComparison.OrdinalIgnoreCase);
        Assert.True(svc.IsModelLoaded);
        svc.UnloadModels();
        Assert.False(svc.IsModelLoaded);
    }

    [SkippableFact]
    public async Task OcrService_WindowsSystem_RecognizesRenderedEnglishText()
    {
        var langs = OcrService.GetSystemOcrLanguages();
        var en = langs.FirstOrDefault(l => l.Tag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        Skip.If(string.IsNullOrEmpty(en.Tag), "当前 Windows 系统未安装英语 OCR 语言包，跳过系统 OCR 集成测试。");

        const int w = 520, h = 120;
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        using var typeface = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                             ?? SKTypeface.Default;
        using var font = new SKFont(typeface, 42f);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        surface.Canvas.DrawText("PPINPIX TEST", 36, 76, SKTextAlign.Left, font, paint);
        surface.Canvas.Flush();

        using var img = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(img);
        int stride = w * 4;
        byte[] bgra = new byte[stride * h];
        for (int row = 0; row < h; row++)
        {
            Marshal.Copy(bmp.GetPixels() + row * bmp.RowBytes, bgra, row * stride, stride);
        }

        var settings = new OcrSettings
        {
            Engine = OcrEngineKind.WindowsSystem,
            Languages = [en.Tag],
            DetectBarcodes = false,
        };
        using var svc = new OcrService(() => settings);
        var result = await svc.RecognizeAsync(new ScreenFrame(new PxRect(0, 0, w, h), stride, bgra));

        Assert.Contains("PPINPIX", result.FullText, StringComparison.OrdinalIgnoreCase);
    }
}
