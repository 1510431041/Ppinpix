using SkiaSharp;

namespace Ppinpix.Capture.Annotation.Shapes;

/// <summary>矩形 / 填充矩形标注。</summary>
public sealed class RectangleShape : IShape
{
    private double _anchorX;
    private double _anchorY;

    public RectangleShape(double startX, double startY, ShapeStyle style, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        _anchorX = startX;
        _anchorY = startY;
        Rect = new RectF(startX, startY, 0, 0);
        Style = style;
    }

    public RectangleShape(RectF rect, ShapeStyle style, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        _anchorX = rect.Left;
        _anchorY = rect.Top;
        Rect = rect;
        Style = style;
    }

    public Guid Id { get; }

    public AnnotationTool Tool => AnnotationTool.Rectangle;

    public ShapeStyle Style { get; set; }

    public RectF Rect { get; private set; }

    public RectF Bounds => Rect;

    public double ClampedCornerRadius => GeometryHelper.ClampCornerRadius(Rect, Style.CornerRadius);

    public bool IsValid => Rect.Width >= 3 && Rect.Height >= 3;

    public void UpdateDrag(double x, double y, bool constrain = false)
    {
        if (constrain) (x, y) = GeometryHelper.ConstrainSquare(_anchorX, _anchorY, x, y);
        Rect = RectF.FromPoints(_anchorX, _anchorY, x, y);
    }

    public bool HitTest(double x, double y, double tolerance = 6)
    {
        if (Style.Filled)
        {
            return Rect.Inflate(tolerance).Contains(x, y);
        }

        double hit = tolerance + Style.LineWidth * 0.5;
        if (!Rect.Inflate(hit).Contains(x, y)) return false;

        return GeometryHelper.DistancePointToSegment(x, y, Rect.Left, Rect.Top, Rect.Right, Rect.Top) <= hit
            || GeometryHelper.DistancePointToSegment(x, y, Rect.Right, Rect.Top, Rect.Right, Rect.Bottom) <= hit
            || GeometryHelper.DistancePointToSegment(x, y, Rect.Right, Rect.Bottom, Rect.Left, Rect.Bottom) <= hit
            || GeometryHelper.DistancePointToSegment(x, y, Rect.Left, Rect.Bottom, Rect.Left, Rect.Top) <= hit;
    }

    public IReadOnlyList<ShapeHandle> GetHandles() =>
        GeometryHelper.GetBoxHandlesWithCornerRadius(Rect, Style.CornerRadius);

    public void MoveHandle(ShapeHandleKind kind, double x, double y, bool constrain = false)
    {
        if (kind == ShapeHandleKind.CornerRadius)
        {
            Style = Style with { CornerRadius = GeometryHelper.ComputeCornerRadiusFromHandle(Rect, x) };
            return;
        }
        Rect = GeometryHelper.MoveBoxHandle(Rect, kind, x, y, constrain);
        _anchorX = Rect.Left;
        _anchorY = Rect.Top;
    }

    public void Translate(double dx, double dy)
    {
        Rect = new RectF(Rect.X + dx, Rect.Y + dy, Rect.Width, Rect.Height);
        _anchorX += dx;
        _anchorY += dy;
    }

    public IShape Clone(bool newId = false) =>
        new RectangleShape(Rect, Style, newId ? Guid.NewGuid() : Id);

    public void Render(SKCanvas canvas, AnnotationRenderContext ctx, double originX, double originY)
    {
        if (!IsValid) return;
        using var paint = GeometryHelper.CreateStrokePaint(Style);
        var skRect = SKRect.Create(
            (float)(Rect.X - originX),
            (float)(Rect.Y - originY),
            (float)Rect.Width,
            (float)Rect.Height);
        float radius = (float)ClampedCornerRadius;
        if (radius > 0.5f)
        {
            canvas.DrawRoundRect(skRect, radius, radius, paint);
        }
        else
        {
            if (!Style.Filled) paint.StrokeJoin = SKStrokeJoin.Miter;
            canvas.DrawRect(skRect, paint);
        }
    }
}
