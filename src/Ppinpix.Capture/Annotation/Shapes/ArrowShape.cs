using SkiaSharp;

namespace Ppinpix.Capture.Annotation.Shapes;

/// <summary>矢量箭头标注（Shift 吸附 45° 倍数角；支持拖动中点弯曲为平滑弧线，双击复原直线）。</summary>
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
        ControlX = (x1 + x2) * 0.5;
        ControlY = (y1 + y2) * 0.5;
    }

    private ArrowShape(
        double x1, double y1,
        double x2, double y2,
        bool isCurved, double controlX, double controlY,
        ShapeStyle style, Guid id)
    {
        Id = id;
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
        IsCurved = isCurved;
        ControlX = controlX;
        ControlY = controlY;
        Style = style;
    }

    public Guid Id { get; }

    public AnnotationTool Tool => AnnotationTool.Arrow;

    public ShapeStyle Style { get; set; }

    public double X1 { get; private set; }
    public double Y1 { get; private set; }
    public double X2 { get; private set; }
    public double Y2 { get; private set; }

    /// <summary>是否已弯曲为二次贝塞尔弧线。</summary>
    public bool IsCurved { get; private set; }

    /// <summary>二次贝塞尔控制点（物理像素）。</summary>
    public double ControlX { get; private set; }
    public double ControlY { get; private set; }

    /// <summary>曲线 t=0.5 处顶点坐标（控制手柄位置）。</summary>
    public PointF CurvePeak => IsCurved
        ? new PointF(0.25 * X1 + 0.5 * ControlX + 0.25 * X2, 0.25 * Y1 + 0.5 * ControlY + 0.25 * Y2)
        : new PointF((X1 + X2) * 0.5, (Y1 + Y2) * 0.5);

    public (PointF Tip, PointF LeftWing, PointF RightWing, PointF Notch, PointF ShaftEnd) ComputeHeadGeometry()
    {
        if (!IsCurved)
        {
            return GeometryHelper.ComputeArrowGeometry(X1, Y1, X2, Y2, Style.LineWidth);
        }

        // 二次贝塞尔曲线在终点 P2 处切线方向为 (P2 - Control)；若控制点接近终点则退化采样 t=0.88 处切线
        double tx = ControlX;
        double ty = ControlY;
        if (GeometryHelper.Distance(ControlX, ControlY, X2, Y2) < 4.0)
        {
            const double t = 0.88;
            const double mt = 1.0 - t;
            tx = mt * mt * X1 + 2.0 * mt * t * ControlX + t * t * X2;
            ty = mt * mt * Y1 + 2.0 * mt * t * ControlY + t * t * Y2;
        }
        return GeometryHelper.ComputeArrowGeometry(tx, ty, X2, Y2, Style.LineWidth);
    }

    public void ResetCurve()
    {
        IsCurved = false;
        ControlX = (X1 + X2) * 0.5;
        ControlY = (Y1 + Y2) * 0.5;
    }

    public RectF Bounds
    {
        get
        {
            var g = ComputeHeadGeometry();
            double minX = Math.Min(Math.Min(X1, X2), Math.Min(g.LeftWing.X, g.RightWing.X));
            double minY = Math.Min(Math.Min(Y1, Y2), Math.Min(g.LeftWing.Y, g.RightWing.Y));
            double maxX = Math.Max(Math.Max(X1, X2), Math.Max(g.LeftWing.X, g.RightWing.X));
            double maxY = Math.Max(Math.Max(Y1, Y2), Math.Max(g.LeftWing.Y, g.RightWing.Y));
            if (IsCurved)
            {
                var peak = CurvePeak;
                minX = Math.Min(minX, peak.X);
                minY = Math.Min(minY, peak.Y);
                maxX = Math.Max(maxX, peak.X);
                maxY = Math.Max(maxY, peak.Y);
            }
            return new RectF(minX, minY, maxX - minX, maxY - minY);
        }
    }

    public bool IsValid => GeometryHelper.Distance(X1, Y1, X2, Y2) >= 5;

    public void UpdateDrag(double x, double y, bool constrain = false)
    {
        if (constrain) (x, y) = GeometryHelper.ConstrainAngle45(X1, Y1, x, y);
        X2 = x;
        Y2 = y;
        if (!IsCurved)
        {
            ControlX = (X1 + X2) * 0.5;
            ControlY = (Y1 + Y2) * 0.5;
        }
    }

    public bool HitTest(double x, double y, double tolerance = 6)
    {
        double threshold = tolerance + Style.LineWidth * 0.5;
        double dist = IsCurved
            ? GeometryHelper.DistancePointToQuadraticBezier(x, y, X1, Y1, ControlX, ControlY, X2, Y2)
            : GeometryHelper.DistancePointToSegment(x, y, X1, Y1, X2, Y2);
        if (dist <= threshold)
        {
            return true;
        }
        var g = ComputeHeadGeometry();
        return GeometryHelper.PointInTriangle(x, y, g.Tip, g.LeftWing, g.RightWing);
    }

    public IReadOnlyList<ShapeHandle> GetHandles()
    {
        var peak = CurvePeak;
        return
        [
            new(ShapeHandleKind.Start, X1, Y1),
            new(ShapeHandleKind.CurveControl, peak.X, peak.Y),
            new(ShapeHandleKind.End, X2, Y2),
        ];
    }

    public void MoveHandle(ShapeHandleKind kind, double x, double y, bool constrain = false)
    {
        if (kind == ShapeHandleKind.Start)
        {
            if (constrain) (x, y) = GeometryHelper.ConstrainAngle45(X2, Y2, x, y);
            double dx = x - X1;
            double dy = y - Y1;
            X1 = x;
            Y1 = y;
            if (IsCurved)
            {
                ControlX += dx * 0.5;
                ControlY += dy * 0.5;
            }
            else
            {
                ControlX = (X1 + X2) * 0.5;
                ControlY = (Y1 + Y2) * 0.5;
            }
        }
        else if (kind == ShapeHandleKind.End)
        {
            if (constrain) (x, y) = GeometryHelper.ConstrainAngle45(X1, Y1, x, y);
            double dx = x - X2;
            double dy = y - Y2;
            X2 = x;
            Y2 = y;
            if (IsCurved)
            {
                ControlX += dx * 0.5;
                ControlY += dy * 0.5;
            }
            else
            {
                ControlX = (X1 + X2) * 0.5;
                ControlY = (Y1 + Y2) * 0.5;
            }
        }
        else if (kind == ShapeHandleKind.CurveControl)
        {
            if (GeometryHelper.DistancePointToSegment(x, y, X1, Y1, X2, Y2) <= 4.0)
            {
                ResetCurve();
                return;
            }
            IsCurved = true;
            ControlX = 2.0 * x - 0.5 * (X1 + X2);
            ControlY = 2.0 * y - 0.5 * (Y1 + Y2);
        }
    }

    public void Translate(double dx, double dy)
    {
        X1 += dx;
        Y1 += dy;
        X2 += dx;
        Y2 += dy;
        ControlX += dx;
        ControlY += dy;
    }

    public IShape Clone(bool newId = false) =>
        new ArrowShape(X1, Y1, X2, Y2, IsCurved, ControlX, ControlY, Style, newId ? Guid.NewGuid() : Id);

    public void Render(SKCanvas canvas, AnnotationRenderContext ctx, double originX, double originY)
    {
        if (!IsValid) return;
        var g = ComputeHeadGeometry();

        using var shaftPaint = new SKPaint
        {
            Color = GeometryHelper.ToSkColor(Style.Argb),
            StrokeWidth = (float)Math.Max(1.0, Style.LineWidth),
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
        };
        if (IsCurved)
        {
            using var shaftPath = new SKPath();
            shaftPath.MoveTo((float)(X1 - originX), (float)(Y1 - originY));
            shaftPath.QuadTo(
                (float)(ControlX - originX), (float)(ControlY - originY),
                (float)(g.ShaftEnd.X - originX), (float)(g.ShaftEnd.Y - originY));
            canvas.DrawPath(shaftPath, shaftPaint);
        }
        else
        {
            canvas.DrawLine(
                (float)(X1 - originX), (float)(Y1 - originY),
                (float)(g.ShaftEnd.X - originX), (float)(g.ShaftEnd.Y - originY),
                shaftPaint);
        }

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
