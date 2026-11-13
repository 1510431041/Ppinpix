using SkiaSharp;

namespace Ppinpix.Capture.Annotation.Shapes;

/// <summary>多行文字标注，支持字体、字号、加粗与可选底框。</summary>
public sealed class TextShape : IShape
{
    private const double PadX = 8;
    private const double PadY = 4;

    private string _text = string.Empty;
    private ShapeStyle _style;
    private double _width = 24;
    private double _height = 28;

    public TextShape(double x, double y, string text, ShapeStyle style, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        X = x;
        Y = y;
        _text = text ?? string.Empty;
        _style = style;
        RecalculateMetrics();
    }

    public Guid Id { get; }

    public AnnotationTool Tool => AnnotationTool.Text;

    public ShapeStyle Style
    {
        get => _style;
        set
        {
            if (_style == value) return;
            _style = value;
            RecalculateMetrics();
        }
    }

    public double X { get; private set; }
    public double Y { get; private set; }

    public string Text
    {
        get => _text;
        set
        {
            string normalized = (value ?? string.Empty).Replace("\r\n", "\n");
            if (_text == normalized) return;
            _text = normalized;
            RecalculateMetrics();
        }
    }

    public bool IsEditing { get; set; }

    public RectF Bounds => new(X, Y, _width, _height);

    public bool IsValid => !string.IsNullOrWhiteSpace(_text);

    public void UpdateDrag(double x, double y, bool constrain = false)
    {
        X = x;
        Y = y;
    }

    public bool HitTest(double x, double y, double tolerance = 6) =>
        Bounds.Inflate(tolerance).Contains(x, y);

    public IReadOnlyList<ShapeHandle> GetHandles() => [];

    public void MoveHandle(ShapeHandleKind kind, double x, double y, bool constrain = false) { }

    public void Translate(double dx, double dy)
    {
        X += dx;
        Y += dy;
    }

    public IShape Clone(bool newId = false) =>
        new TextShape(X, Y, _text, _style, newId ? Guid.NewGuid() : Id);

    public void RecalculateMetrics()
    {
        string content = string.IsNullOrEmpty(_text) ? " " : _text;
        string[] lines = content.Split('\n');
        double lineHeight = _style.FontSize * 1.3;

        using var typeface = SKTypeface.FromFamilyName(
            _style.FontFamily,
            _style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, (float)_style.FontSize) { Edging = SKFontEdging.Antialias };

        double maxLineW = 12;
        foreach (string line in lines)
        {
            if (line.Length == 0) continue;
            float w = font.MeasureText(line);
            if (w > maxLineW) maxLineW = w;
        }

        _width = Math.Ceiling(maxLineW) + PadX * 2;
        _height = Math.Ceiling(lines.Length * lineHeight) + PadY * 2;
    }

    public void Render(SKCanvas canvas, AnnotationRenderContext ctx, double originX, double originY)
    {
        if (IsEditing || !IsValid) return;

        float left = (float)(X - originX);
        float top = (float)(Y - originY);

        if (_style.TextBackground)
        {
            var bgColor = GeometryHelper.IsLightColor(_style.Argb)
                ? new SKColor(0x1B, 0x1F, 0x24, 0xCC)
                : new SKColor(0xFF, 0xFF, 0xFF, 0xDC);
            using var bgPaint = new SKPaint
            {
                Color = bgColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
            };
            canvas.DrawRoundRect(SKRect.Create(left, top, (float)_width, (float)_height), 4f, 4f, bgPaint);
        }

        using var typeface = SKTypeface.FromFamilyName(
            _style.FontFamily,
            _style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, (float)_style.FontSize) { Edging = SKFontEdging.Antialias };
        using var paint = new SKPaint
        {
            Color = GeometryHelper.ToSkColor(_style.Argb),
            IsAntialias = true,
        };

        var metrics = font.Metrics;
        float lineHeight = (float)(_style.FontSize * 1.3);
        float yCursor = top + (float)PadY - metrics.Ascent;
        foreach (string line in _text.Split('\n'))
        {
            if (line.Length > 0)
            {
                canvas.DrawText(line, left + (float)PadX, yCursor, SKTextAlign.Left, font, paint);
            }
            yCursor += lineHeight;
        }
    }
}
