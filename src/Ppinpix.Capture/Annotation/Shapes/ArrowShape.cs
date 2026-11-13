using SkiaSharp;

namespace Ppinpix.Capture.Annotation.Shapes;

/// <summary>矢量箭头标注（Shift 吸附 45° 倍数角）。</summary>
public sealed class ArrowShape : IShape
{
    public ArrowShape(double x1, double y1, double x2, double y2, ShapeStyle style, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
        Style = style;
    }

    public Guid Id { get; }

    public AnnotationTool Tool => AnnotationTool.Arrow;

    public ShapeStyle Style { get; set; }

    public double X1 { get; private set; }
    public double Y1 { get; private set; }
    public double X2 { get; private set; }
    public double Y2 { get; private set; }

    public RectF Bounds
    {
        get
        {
            var g = GeometryHelper.ComputeArrowGeometry(X1, Y1, X2, Y2, Style.LineWidth);
            double minX = Math.Min(Math.Min(X1, X2), Math.Min(g.LeftWing.X, g.RightWing.X));
            double minY = Math.Min(Math.Min(Y1, Y2), Math.Min(g.LeftWing.Y, g.RightWing.Y));
            double maxX = Math.Max(Math.Max(X1, X2), Math.Max(g.LeftWing.X, g.RightWing.X));
            double maxY = Math.Max(Math.Max(Y1, Y2), Math.Max(g.LeftWing.Y, g.RightWing.Y));
            return new RectF(minX, minY, maxX - minX, maxY - minY);
        }
    }

    public bool IsValid => GeometryHelper.Distance(X1, Y1, X2, Y2) >= 5;

    public void UpdateDrag(double x, double y, bool constrain = false)
    {
        if (constrain) (x, y) = GeometryHelper.ConstrainAngle45(X1, Y1, x, y);
        X2 = x;
        Y2 = y;
    }

    public bool HitTest(double x, double y, double tolerance = 6)
    {
        if (GeometryHelper.DistancePointToSegment(x, y, X1, Y1, X2, Y2) <= tolerance + Style.LineWidth * 0.5)
        {
            return true;
        }
        var g = GeometryHelper.ComputeArrowGeometry(X1, Y1, X2, Y2, Style.LineWidth);
        return GeometryHelper.PointInTriangle(x, y, g.Tip, g.LeftWing, g.RightWing);
    }

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
        new ArrowShape(X1, Y1, X2, Y2, Style, newId ? Guid.NewGuid() : Id);

    public void Render(SKCanvas canvas, AnnotationRenderContext ctx, double originX, double originY)
    {
        if (!IsValid) return;
        var g = GeometryHelper.ComputeArrowGeometry(X1, Y1, X2, Y2, Style.LineWidth);

        using var shaftPaint = new SKPaint
        {
            Color = GeometryHelper.ToSkColor(Style.Argb),
            StrokeWidth = (float)Math.Max(1.0, Style.LineWidth),
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
        };
        canvas.DrawLine(
            (float)(X1 - originX), (float)(Y1 - originY),
            (float)(g.ShaftEnd.X - originX), (float)(g.ShaftEnd.Y - originY),
            shaftPaint);

        using var headPaint = new SKPaint
        {
            Color = GeometryHelper.ToSkColor(Style.Argb),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };
        using var path = new SKPath();
        path.MoveTo((float)(g.Tip.X - originX), (float)(g.Tip.Y - originY));
        path.LineTo((float)(g.LeftWing.X - originX), (float)(g.LeftWing.Y - originY));
        path.LineTo((float)(g.Notch.X - originX), (float)(g.Notch.Y - originY));
        path.LineTo((float)(g.RightWing.X - originX), (float)(g.RightWing.Y - originY));
        path.Close();
        canvas.DrawPath(path, headPaint);
    }
}
