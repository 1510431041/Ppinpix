using SkiaSharp;

namespace Ppinpix.Capture.Annotation.Shapes;

/// <summary>直线标注（Shift 吸附 45° 倍数角）。</summary>
public sealed class LineShape : IShape
{
    public LineShape(double x1, double y1, double x2, double y2, ShapeStyle style, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
        Style = style;
    }

    public Guid Id { get; }

    public AnnotationTool Tool => AnnotationTool.Line;

    public ShapeStyle Style { get; set; }

    public double X1 { get; private set; }
    public double Y1 { get; private set; }
    public double X2 { get; private set; }
    public double Y2 { get; private set; }

    public RectF Bounds => RectF.FromPoints(X1, Y1, X2, Y2);

    public bool IsValid => GeometryHelper.Distance(X1, Y1, X2, Y2) >= 3;

    public void UpdateDrag(double x, double y, bool constrain = false)
    {
        if (constrain) (x, y) = GeometryHelper.ConstrainAngle45(X1, Y1, x, y);
        X2 = x;
        Y2 = y;
    }

    public bool HitTest(double x, double y, double tolerance = 6) =>
        GeometryHelper.DistancePointToSegment(x, y, X1, Y1, X2, Y2) <= tolerance + Style.LineWidth * 0.5;

    public IReadOnlyList<ShapeHandle> GetHandles() =>
    [
        new(ShapeHandleKind.Start, X1, Y1),
        new(ShapeHandleKind.End, X2, Y2),
    ];

    public void MoveHandle(ShapeHandleKind kind, double x, double y, bool constrain = false)
    {
        if (kind == ShapeHandleKind.Start)
        {
            if (constrain) (x, y) = GeometryHelper.ConstrainAngle45(X2, Y2, x, y);
            X1 = x;
            Y1 = y;
        }
        else if (kind == ShapeHandleKind.End)
        {
            if (constrain) (x, y) = GeometryHelper.ConstrainAngle45(X1, Y1, x, y);
            X2 = x;
            Y2 = y;
        }
    }

    public void Translate(double dx, double dy)
    {
        X1 += dx;
        Y1 += dy;
        X2 += dx;
        Y2 += dy;
    }

    public IShape Clone(bool newId = false) =>
        new LineShape(X1, Y1, X2, Y2, Style, newId ? Guid.NewGuid() : Id);

    public void Render(SKCanvas canvas, AnnotationRenderContext ctx, double originX, double originY)
    {
        if (!IsValid) return;
        using var paint = GeometryHelper.CreateStrokePaint(Style with { Filled = false });
        canvas.DrawLine(
            (float)(X1 - originX), (float)(Y1 - originY),
            (float)(X2 - originX), (float)(Y2 - originY),
            paint);
    }
}
