using SkiaSharp;

namespace Ppinpix.Capture.Annotation.Shapes;

/// <summary>自由曲线画笔标注。</summary>
public sealed class FreehandShape : IShape
{
    private readonly List<PointF> _points = [];

    public FreehandShape(double startX, double startY, ShapeStyle style, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        _points.Add(new PointF(startX, startY));
        Style = style;
    }

    private FreehandShape(IEnumerable<PointF> points, ShapeStyle style, Guid id)
    {
        Id = id;
        _points.AddRange(points);
        Style = style;
    }

    public Guid Id { get; }

    public AnnotationTool Tool => AnnotationTool.Freehand;

    public ShapeStyle Style { get; set; }

    public IReadOnlyList<PointF> Points => _points;

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
        double hit = tolerance + Style.LineWidth * 0.5;
        if (!Bounds.Inflate(hit).Contains(x, y)) return false;

        if (_points.Count == 1)
        {
            return GeometryHelper.Distance(x, y, _points[0].X, _points[0].Y) <= hit;
        }
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
        new FreehandShape(_points, Style, newId ? Guid.NewGuid() : Id);

    public SKPath BuildPath(double originX, double originY)
    {
        var path = new SKPath();
        if (_points.Count == 0) return path;

        path.MoveTo((float)(_points[0].X - originX), (float)(_points[0].Y - originY));
        if (_points.Count == 2)
        {
            path.LineTo((float)(_points[1].X - originX), (float)(_points[1].Y - originY));
            return path;
        }

        for (int i = 1; i < _points.Count - 1; i++)
        {
            float cx = (float)(_points[i].X - originX);
            float cy = (float)(_points[i].Y - originY);
            float nx = (float)((_points[i].X + _points[i + 1].X) * 0.5 - originX);
            float ny = (float)((_points[i].Y + _points[i + 1].Y) * 0.5 - originY);
            path.QuadTo(cx, cy, nx, ny);
        }
        var last = _points[^1];
        path.LineTo((float)(last.X - originX), (float)(last.Y - originY));
        return path;
    }

    public void Render(SKCanvas canvas, AnnotationRenderContext ctx, double originX, double originY)
    {
        if (!IsValid) return;
        using var paint = GeometryHelper.CreateStrokePaint(Style with { Filled = false, Dashed = false });
        using var path = BuildPath(originX, originY);
        canvas.DrawPath(path, paint);
    }
}
