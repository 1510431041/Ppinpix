namespace Ppinpix.Ocr.Models;

/// <summary>识别区域四顶点坐标（物理像素，顺时针：左上、右上、右下、左下）。</summary>
public readonly record struct Quad(double X1, double Y1, double X2, double Y2, double X3, double Y3, double X4, double Y4)
{
    public double MinX => Math.Min(Math.Min(X1, X2), Math.Min(X3, X4));
    public double MinY => Math.Min(Math.Min(Y1, Y2), Math.Min(Y3, Y4));
    public double MaxX => Math.Max(Math.Max(X1, X2), Math.Max(X3, X4));
    public double MaxY => Math.Max(Math.Max(Y1, Y2), Math.Max(Y3, Y4));
    public double Width => Math.Max(0, MaxX - MinX);
    public double Height => Math.Max(0, MaxY - MinY);
    public double CenterX => (MinX + MaxX) * 0.5;
    public double CenterY => (MinY + MaxY) * 0.5;

    public bool Contains(double x, double y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

    public Quad Scale(double factor) =>
        new(X1 * factor, Y1 * factor, X2 * factor, Y2 * factor,
            X3 * factor, Y3 * factor, X4 * factor, Y4 * factor);

    public static Quad FromRect(double x, double y, double width, double height) =>
        new(x, y, x + width, y, x + width, y + height, x, y + height);
}

/// <summary>单个字符及其边界框（支持悬停 I 型光标、划词精准选中）。</summary>
public sealed record CharBox(string Text, Quad Box, float Confidence = 1.0f);

/// <summary>单行识别文本。</summary>
public sealed record TextLine(
    string Text,
    Quad Box,
    IReadOnlyList<CharBox> Chars,
    float Confidence = 1.0f);

/// <summary>由相近文本行聚类得到的段落（保持列隔离，不跨栏合并）。</summary>
public sealed record Paragraph(
    string Text,
    Quad Box,
    IReadOnlyList<TextLine> Lines);

public enum BarcodeKind
{
    Unknown = 0,
    QrCode,
    DataMatrix,
    Aztec,
    Pdf417,
    Linear1D,
}

/// <summary>识别到的二维码或条形码。</summary>
public sealed record Barcode(
    string Content,
    BarcodeKind Kind,
    string RawFormat,
    Quad Box);

/// <summary>一次离线识别（OCR + 二维码/条形码）的完整结果。</summary>
public sealed record OcrResult(
    IReadOnlyList<TextLine> Lines,
    IReadOnlyList<Paragraph> Paragraphs,
    IReadOnlyList<Barcode> Barcodes,
    string EngineName,
    long ElapsedMs)
{
    public static readonly OcrResult Empty = new([], [], [], string.Empty, 0);

    public bool IsEmpty => Lines.Count == 0 && Barcodes.Count == 0;

    public string FullText => Paragraphs.Count > 0
        ? string.Join("\n\n", Paragraphs.Select(p => p.Text))
        : string.Join('\n', Lines.Select(l => l.Text));
}
