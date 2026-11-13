using SkiaSharp;

namespace Ppinpix.Capture.Annotation.Shapes;

/// <summary>椭圆 / 正圆标注。</summary>
public sealed class EllipseShape : IShape
{
    private double _anchorX;
    private double _anchorY;

    public EllipseShape(double startX, double startY, ShapeStyle style, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        _anchorX = startX;
        _anchorY = startY;
        Rect = new RectF(startX, startY, 0, 0);
        Style = style;
    }

    public EllipseShape(RectF rect, ShapeStyle style, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        _anchorX = rect.Left;
        _anchorY = rect.Top;
        Rect = rect;
        Style = style;
    }

    public Guid Id { get; }

    public AnnotationTool Tool => AnnotationTool.Ellipse;

    public ShapeStyle Style { get; set; }

    public RectF Rect { get; private set; }

    public RectF Bounds => Rect;

    public bool IsValid => Rect.Width >= 3 && Rect.Height >= 3;

    public void UpdateDrag(double x, double y, bool constrain = false)
    {
        if (constrain) (x, y) = GeometryHelper.ConstrainSquare(_anchorX, _anchorY, x, y);
        Rect = RectF.FromPoints(_anchorX, _anchorY, x, y);
    }

    public bool HitTest(double x, double y, double tolerance = 6)
    {
        double rx = Rect.Width * 0.5;
        double ry = Rect.Height * 0.5;
        if (rx < 1 || ry < 1) return false;

        double dx = x - Rect.CenterX;
        double dy = y - Rect.CenterY;
        double hit = tolerance + Style.LineWidth * 0.5;

        if (Style.Filled)
        {
            double nx = dx / (rx + tolerance);
            double ny = dy / (ry + tolerance);
            return nx * nx + ny * ny <= 1.0;
        }

        double nxOut = dx / (rx + hit);
        double nyOut = dy / (ry + hit);
        if (nxOut * nxOut + nyOut * nyOut > 1.0) return false;

        double irx = Math.Max(0, rx - hit);
        double iry = Math.Max(0, ry - hit);
        if (irx <= 0 || iry <= 0) return true;

        double nxIn = dx / irx;
        double nyIn = dy / iry;
        return nxIn * nxIn + nyIn * nyIn >= 1.0;
    }

    public IReadOnlyList<ShapeHandle> GetHandles() => GeometryHelper.GetBoxHandles(Rect);

    public void MoveHandle(ShapeHandleKind kind, double x, double y, bool constrain = false)
    {
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
        new EllipseShape(Rect, Style, newId ? Guid.NewGuid() : Id);

    public void Render(SKCanvas canvas, AnnotationRenderContext ctx, double originX, double originY)
    {
        if (!IsValid) return;
        using var paint = GeometryHelper.CreateStrokePaint(Style);
        var skRect = SKRect.Create(
            (float)(Rect.X - originX),
            (float)(Rect.Y - originY),
            (float)Rect.Width,
            (float)Rect.Height);
        canvas.DrawOval(skRect, paint);
    }
}
