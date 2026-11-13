using Ppinpix.Core.Capture;
using Ppinpix.Ocr.Models;
using Ppinpix.Ocr.Processing;
using ZXing;
using ZXing.Common;

namespace Ppinpix.Ocr.Barcode;

/// <summary>纯离线二维码与一维条形码识别器（基于 ZXing.Net）。</summary>
public static class ZxingDecoder
{
    private static readonly List<BarcodeFormat> SupportedFormats =
    [
        BarcodeFormat.QR_CODE,
        BarcodeFormat.DATA_MATRIX,
        BarcodeFormat.AZTEC,
        BarcodeFormat.PDF_417,
        BarcodeFormat.CODE_128,
        BarcodeFormat.CODE_39,
        BarcodeFormat.EAN_13,
        BarcodeFormat.EAN_8,
        BarcodeFormat.UPC_A,
        BarcodeFormat.UPC_E,
        BarcodeFormat.ITF,
    ];

    public static IReadOnlyList<Models.Barcode> Decode(ScreenFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Pixels.IsEmpty)
        {
            return [];
        }

        byte[] bgra = ImagePreprocessor.ExtractTightBgra(frame);

        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = SupportedFormats,
            },
        };

        Result[]? results = reader.DecodeMultiple(
            bgra,
            frame.Width,
            frame.Height,
            RGBLuminanceSource.BitmapFormat.BGRA32);

        if (results is null || results.Length == 0)
        {
            return [];
        }

        var list = new List<Models.Barcode>(results.Length);
        var dedup = new HashSet<(string Text, BarcodeFormat Format)>();

        foreach (var r in results)
        {
            if (string.IsNullOrEmpty(r.Text)) continue;
            if (!dedup.Add((r.Text, r.BarcodeFormat))) continue;

            var quad = PointsToQuad(r.ResultPoints, frame.Width, frame.Height);
            list.Add(new Models.Barcode(
                r.Text,
                MapKind(r.BarcodeFormat),
                r.BarcodeFormat.ToString(),
                quad));
        }

        return list;
    }

    private static Quad PointsToQuad(ResultPoint[]? pts, int width, int height)
    {
        if (pts is null || pts.Length == 0)
        {
            return Quad.FromRect(0, 0, width, height);
        }

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in pts)
        {
            if (p is null) continue;
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }

        if (minX > maxX) return Quad.FromRect(0, 0, width, height);

        // 一维条码可能只返回基线两端点，稍微扩厚高度方便点击选中
        if (maxX - minX < 8) { minX = Math.Max(0, minX - 4); maxX = Math.Min(width, maxX + 4); }
        if (maxY - minY < 8) { minY = Math.Max(0, minY - 12); maxY = Math.Min(height, maxY + 12); }

        return Quad.FromRect(minX, minY, maxX - minX, maxY - minY);
    }

    private static BarcodeKind MapKind(BarcodeFormat format) => format switch
    {
        BarcodeFormat.QR_CODE => BarcodeKind.QrCode,
        BarcodeFormat.DATA_MATRIX => BarcodeKind.DataMatrix,
        BarcodeFormat.AZTEC => BarcodeKind.Aztec,
        BarcodeFormat.PDF_417 => BarcodeKind.Pdf417,
        _ => BarcodeKind.Linear1D,
    };
}
