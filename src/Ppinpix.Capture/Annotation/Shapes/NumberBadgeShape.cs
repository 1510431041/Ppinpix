using SkiaSharp;

namespace Ppinpix.Capture.Annotation.Shapes;

/// <summary>自动递增序号圆圈标注（1, 2, 3…；点击即落点，按住向外拖拽可拉出指向箭头）。</summary>
public sealed class NumberBadgeShape : IShape
{
    public NumberBadgeShape(double centerX, double centerY, int number, ShapeStyle style, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        CenterX = centerX;
        CenterY = centerY;
        TailX = centerX;
        TailY = centerY;
        Number = Math.Max(1, number);
        Style = style;
    }

    private NumberBadgeShape(
        double centerX, double centerY,
        bool hasTail, double tailX, double tailY,
        int number, ShapeStyle style, Guid id)
    {
        Id = id;
        CenterX = centerX;
        CenterY = centerY;
        HasTail = hasTail;
        TailX = tailX;
        TailY = tailY;
        Number = Math.Max(1, number);
        Style = style;
    }

    public Guid Id { get; }

    public AnnotationTool Tool => AnnotationTool.NumberBadge;

    public ShapeStyle Style { get; set; }

    public double CenterX { get; private set; }
    public double CenterY { get; private set; }

    public bool HasTail { get; private set; }
    public double TailX { get; private set; }
    public double TailY { get; private set; }

    public int Number { get; set; }

    public double Radius => Math.Clamp(Style.BadgeSize, 16, 48) * 0.5;

    public RectF Bounds
    {
        get
        {
            double minX = CenterX - Radius;
            double minY = CenterY - Radius;
            double maxX = CenterX + Radius;
            double maxY = CenterY + Radius;
            if (HasTail)
            {
                minX = Math.Min(minX, TailX - 4);
                minY = Math.Min(minY, TailY - 4);
                maxX = Math.Max(maxX, TailX + 4);
                maxY = Math.Max(maxY, TailY + 4);
            }
            return new RectF(minX, minY, maxX - minX, maxY - minY);
        }
    }

    public bool IsValid => true;

    public void UpdateDrag(double x, double y, bool constrain = false)
    {
        if (constrain) (x, y) = GeometryHelper.ConstrainAngle45(CenterX, CenterY, x, y);
        if (GeometryHelper.Distance(CenterX, CenterY, x, y) > Radius + 6)
        {
            HasTail = true;
            TailX = x;
            TailY = y;
        }
        else
        {
            HasTail = false;
            TailX = CenterX;
            TailY = CenterY;
        }
    }

    public bool HitTest(double x, double y, double tolerance = 6)
    {
        if (GeometryHelper.Distance(x, y, CenterX, CenterY) <= Radius + tolerance)
        {
            return true;
        }
        if (HasTail)
        {
            return GeometryHelper.DistancePointToSegment(x, y, CenterX, CenterY, TailX, TailY) <= tolerance + 4;
        }
        return false;
    }

    public IReadOnlyList<ShapeHandle> GetHandles() =>
    [
        HasTail
            ? new(ShapeHandleKind.TailEnd, TailX, TailY)
            : new(ShapeHandleKind.TailEnd, CenterX + Radius + 8, CenterY),
    ];

    public void MoveHandle(ShapeHandleKind kind, double x, double y, bool constrain = false)
    {
        if (kind == ShapeHandleKind.TailEnd)
        {
            UpdateDrag(x, y, constrain);
        }
    }

    public void Translate(double dx, double dy)
    {
        CenterX += dx;
        CenterY += dy;
        TailX += dx;
        TailY += dy;
    }

    public IShape Clone(bool newId = false) =>
        new NumberBadgeShape(CenterX, CenterY, HasTail, TailX, TailY, Number, Style, newId ? Guid.NewGuid() : Id);

    /// <summary>计算拉出指针楔形三角形的两个底边顶点与尖端顶点。</summary>
    public bool TryGetTailTriangle(out PointF baseLeft, out PointF tip, out PointF baseRight)
    {
        baseLeft = tip = baseRight = default;
        if (!HasTail) return false;

        double dx = TailX - CenterX;
        double dy = TailY - CenterY;
        double dist = double.Hypot(dx, dy);
        if (dist <= Radius + 2) return false;

        double ux = dx / dist;
        double uy = dy / dist;
        double px = -uy;
        double py = ux;

        double baseDist = Radius * 0.72;
        double halfWidth = Radius * 0.44;
        double bx = CenterX + ux * baseDist;
        double by = CenterY + uy * baseDist;

        baseLeft = new PointF(bx + px * halfWidth, by + py * halfWidth);
        tip = new PointF(TailX, TailY);
        baseRight = new PointF(bx - px * halfWidth, by - py * halfWidth);
        return true;
    }

    public void Render(SKCanvas canvas, AnnotationRenderContext ctx, double originX, double originY)
    {
        float cx = (float)(CenterX - originX);
        float cy = (float)(CenterY - originY);
        float r = (float)Radius;

        using var shapePath = new SKPath();
        shapePath.AddCircle(cx, cy, r);

        if (TryGetTailTriangle(out var bl, out var tip, out var br))
        {
            using var tailPath = new SKPath();
            tailPath.MoveTo((float)(bl.X - originX), (float)(bl.Y - originY));
            tailPath.LineTo((float)(tip.X - originX), (float)(tip.Y - originY));
            tailPath.LineTo((float)(br.X - originX), (float)(br.Y - originY));
            tailPath.Close();

            using var combined = shapePath.Op(tailPath, SKPathOp.Union);
            if (combined is not null)
            {
                shapePath.Reset();
                shapePath.AddPath(combined);
            }
        }

        using var fillPaint = new SKPaint
        {
            Color = GeometryHelper.ToSkColor(Style.Argb),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };
        canvas.DrawPath(shapePath, fillPaint);

        // 细微外框描边，保证浅色背景下也清晰
        using var ringPaint = new SKPaint
        {
            Color = GeometryHelper.IsLightColor(Style.Argb)
                ? new SKColor(0x1B, 0x1F, 0x24, 0x60)
                : new SKColor(0xFF, 0xFF, 0xFF, 0x70),
            StrokeWidth = 1.25f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeJoin = SKStrokeJoin.Round,
        };
        canvas.DrawPath(shapePath, ringPaint);

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
