using SkiaSharp;

namespace Ppinpix.Capture.Annotation.Shapes;

/// <summary>荧光笔半透明高亮（单条路径绘制避免重叠深斑；Shift 切换直线荧光条）。</summary>
public sealed class HighlightShape : IShape
{
    private readonly List<PointF> _points = [];
    private readonly double _startX;
    private readonly double _startY;

    public HighlightShape(double startX, double startY, ShapeStyle style, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        _startX = startX;
        _startY = startY;
        _points.Add(new PointF(startX, startY));
        Style = style;
    }

    private HighlightShape(IEnumerable<PointF> points, ShapeStyle style, Guid id)
    {
        Id = id;
        _points.AddRange(points);
        _startX = _points.Count > 0 ? _points[0].X : 0;
        _startY = _points.Count > 0 ? _points[0].Y : 0;
        Style = style;
    }

    public Guid Id { get; }

    public AnnotationTool Tool => AnnotationTool.Highlight;

    public ShapeStyle Style { get; set; }

    public IReadOnlyList<PointF> Points => _points;

    public double EffectiveStrokeWidth => Math.Max(14.0, Style.LineWidth * 5.0);

    public RectF Bounds
    {
        get
        {
            if (_points.Count == 0) return new RectF(0, 0, 0, 0);
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var p in _points)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
            return new RectF(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
        }
    }

    public bool IsValid => _points.Count >= 2;

    public void UpdateDrag(double x, double y, bool constrain = false)
    {
        if (constrain)
        {
            (x, y) = GeometryHelper.ConstrainAngle45(_startX, _startY, x, y);
            _points.Clear();
            _points.Add(new PointF(_startX, _startY));
            _points.Add(new PointF(x, y));
            return;
        }

        if (_points.Count == 0)
        {
            _points.Add(new PointF(x, y));
            return;
        }
        var last = _points[^1];
        if (GeometryHelper.Distance(last.X, last.Y, x, y) >= 2.0)
        {
            _points.Add(new PointF(x, y));
        }
    }

    public bool HitTest(double x, double y, double tolerance = 6)
    {
        if (_points.Count == 0) return false;
        double hit = tolerance + EffectiveStrokeWidth * 0.5;
        if (!Bounds.Inflate(hit).Contains(x, y)) return false;

        for (int i = 1; i < _points.Count; i++)
        {
            if (GeometryHelper.DistancePointToSegment(
                    x, y, _points[i - 1].X, _points[i - 1].Y, _points[i].X, _points[i].Y) <= hit)
            {
                return true;
            }
        }
        return false;
    }

    public IReadOnlyList<ShapeHandle> GetHandles() => GeometryHelper.GetBoxHandles(Bounds);

    public void MoveHandle(ShapeHandleKind kind, double x, double y, bool constrain = false)
    {
        var oldBounds = Bounds;
        if (oldBounds.Width < 1 || oldBounds.Height < 1) return;

        var newBounds = GeometryHelper.MoveBoxHandle(oldBounds, kind, x, y, constrain, minSize: 8);
        double sx = newBounds.Width / oldBounds.Width;
        double sy = newBounds.Height / oldBounds.Height;

        for (int i = 0; i < _points.Count; i++)
        {
            var p = _points[i];
            _points[i] = new PointF(
                newBounds.X + (p.X - oldBounds.X) * sx,
                newBounds.Y + (p.Y - oldBounds.Y) * sy);
        }
    }

    public void Translate(double dx, double dy)
    {
        for (int i = 0; i < _points.Count; i++)
        {
            _points[i] = new PointF(_points[i].X + dx, _points[i].Y + dy);
        }
    }

    public IShape Clone(bool newId = false) =>
        new HighlightShape(_points, Style, newId ? Guid.NewGuid() : Id);

    public void Render(SKCanvas canvas, AnnotationRenderContext ctx, double originX, double originY)
    {
        if (!IsValid) return;
        var baseColor = GeometryHelper.ToSkColor(Style.Argb);
        using var paint = new SKPaint
        {
            Color = baseColor.WithAlpha(105),
            StrokeWidth = (float)EffectiveStrokeWidth,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        using var path = new SKPath();
        path.MoveTo((float)(_points[0].X - originX), (float)(_points[0].Y - originY));
        for (int i = 1; i < _points.Count; i++)
        {
            path.LineTo((float)(_points[i].X - originX), (float)(_points[i].Y - originY));
        }
        canvas.DrawPath(path, paint);
    }
}
