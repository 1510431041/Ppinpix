using SkiaSharp;

namespace Ppinpix.Capture.Annotation.Shapes;

/// <summary>自动递增序号圆圈标注（1, 2, 3…）。</summary>
public sealed class NumberBadgeShape : IShape
{
    public NumberBadgeShape(double centerX, double centerY, int number, ShapeStyle style, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        CenterX = centerX;
        CenterY = centerY;
        Number = Math.Max(1, number);
        Style = style;
    }

    public Guid Id { get; }

    public AnnotationTool Tool => AnnotationTool.NumberBadge;

    public ShapeStyle Style { get; set; }

    public double CenterX { get; private set; }
    public double CenterY { get; private set; }

    public int Number { get; set; }

    public double Radius => Math.Clamp(Style.BadgeSize, 16, 48) * 0.5;

    public RectF Bounds => new(CenterX - Radius, CenterY - Radius, Radius * 2, Radius * 2);

    public bool IsValid => true;

    public void UpdateDrag(double x, double y, bool constrain = false)
    {
        CenterX = x;
        CenterY = y;
    }

    public bool HitTest(double x, double y, double tolerance = 6) =>
        GeometryHelper.Distance(x, y, CenterX, CenterY) <= Radius + tolerance;

    public IReadOnlyList<ShapeHandle> GetHandles() => [];

    public void MoveHandle(ShapeHandleKind kind, double x, double y, bool constrain = false) { }

    public void Translate(double dx, double dy)
    {
        CenterX += dx;
        CenterY += dy;
    }

    public IShape Clone(bool newId = false) =>
        new NumberBadgeShape(CenterX, CenterY, Number, Style, newId ? Guid.NewGuid() : Id);

    public void Render(SKCanvas canvas, AnnotationRenderContext ctx, double originX, double originY)
    {
        float cx = (float)(CenterX - originX);
        float cy = (float)(CenterY - originY);
        float r = (float)Radius;

        using var fillPaint = new SKPaint
        {
            Color = GeometryHelper.ToSkColor(Style.Argb),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };
        canvas.DrawCircle(cx, cy, r, fillPaint);

        // 细微外框描边，保证浅色背景下也清晰
        using var ringPaint = new SKPaint
        {
            Color = GeometryHelper.IsLightColor(Style.Argb)
                ? new SKColor(0x1B, 0x1F, 0x24, 0x60)
                : new SKColor(0xFF, 0xFF, 0xFF, 0x70),
            StrokeWidth = 1.25f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };
        canvas.DrawCircle(cx, cy, r, ringPaint);

        string label = Number.ToString();
        float fontSize = r * (label.Length >= 2 ? 0.96f : 1.15f);
        using var typeface = SKTypeface.FromFamilyName(
            "Segoe UI",
            SKFontStyleWeight.Bold,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, fontSize) { Edging = SKFontEdging.Antialias };
        using var textPaint = new SKPaint
        {
            Color = GeometryHelper.IsLightColor(Style.Argb)
                ? new SKColor(0x1B, 0x1F, 0x24, 0xFF)
                : SKColors.White,
            IsAntialias = true,
        };
        var metrics = font.Metrics;
        float baselineY = cy - (metrics.Ascent + metrics.Descent) * 0.5f;
        canvas.DrawText(label, cx, baselineY, SKTextAlign.Center, font, textPaint);
    }
}
