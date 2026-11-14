using SkiaSharp;

namespace Ppinpix.Capture.Annotation.Shapes;

/// <summary>聚光灯标注：将选区其余部分压暗，保留一个或多个聚光区域原始亮度。</summary>
public sealed class SpotlightShape : IShape
{
    public const byte MaskAlpha = 168;

    private double _anchorX;
    private double _anchorY;

    public SpotlightShape(double startX, double startY, ShapeStyle style, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        _anchorX = startX;
        _anchorY = startY;
        Rect = new RectF(startX, startY, 0, 0);
        Style = style;
    }

    public SpotlightShape(RectF rect, ShapeStyle style, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        _anchorX = rect.Left;
        _anchorY = rect.Top;
        Rect = rect;
        Style = style;
    }

    public Guid Id { get; }

    public AnnotationTool Tool => AnnotationTool.Spotlight;

    public ShapeStyle Style { get; set; }

    public RectF Rect { get; private set; }

    public RectF Bounds => Rect;

    public double ClampedCornerRadius => GeometryHelper.ClampCornerRadius(Rect, Style.CornerRadius);

    public bool IsValid => Rect.Width >= 4 && Rect.Height >= 4;

    public void UpdateDrag(double x, double y, bool constrain = false)
    {
        if (constrain) (x, y) = GeometryHelper.ConstrainSquare(_anchorX, _anchorY, x, y);
        Rect = RectF.FromPoints(_anchorX, _anchorY, x, y);
    }

    public bool HitTest(double x, double y, double tolerance = 4) =>
        Rect.Inflate(tolerance).Contains(x, y);

    public IReadOnlyList<ShapeHandle> GetHandles() =>
        GeometryHelper.GetBoxHandlesWithCornerRadius(Rect, Style.CornerRadius);

    public void MoveHandle(ShapeHandleKind kind, double x, double y, bool constrain = false)
    {
        if (kind == ShapeHandleKind.CornerRadius)
        {
            Style = Style with { CornerRadius = GeometryHelper.ComputeCornerRadiusFromHandle(Rect, x) };
            return;
        }
        Rect = GeometryHelper.MoveBoxHandle(Rect, kind, x, y, constrain, minSize: 8);
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
        new SpotlightShape(Rect, Style, newId ? Guid.NewGuid() : Id);

    /// <summary>单个 Spotlight 的绘制由编辑器/渲染层通过 <see cref="RenderSpotlightMask"/> 统一合成，避免多个聚光灯重叠时互相压暗。</summary>
    public void Render(SKCanvas canvas, AnnotationRenderContext ctx, double originX, double originY)
    {
    }

    /// <summary>将所有聚光灯合并并集后掏空选区暗色遮罩，保留亮区原始亮度。</summary>
    public static void RenderSpotlightMask(
        SKCanvas canvas,
        int canvasWidth,
        int canvasHeight,
        IEnumerable<SpotlightShape> spotlights,
        double originX,
        double originY)
    {
        SKPath? union = null;
        try
        {
            foreach (var spot in spotlights)
            {
                if (!spot.IsValid) continue;
                var skRect = SKRect.Create(
                    (float)(spot.Rect.X - originX),
                    (float)(spot.Rect.Y - originY),
                    (float)spot.Rect.Width,
                    (float)spot.Rect.Height);
                float radius = (float)spot.ClampedCornerRadius;

                using var current = new SKPath();
                if (radius > 0.5f)
                {
                    current.AddRoundRect(skRect, radius, radius);
                }
                else
                {
                    current.AddRect(skRect);
                }

                if (union is null)
                {
                    union = new SKPath(current);
                }
                else
                {
                    using var merged = union.Op(current, SKPathOp.Union);
                    if (merged is not null)
                    {
                        union.Dispose();
                        union = new SKPath(merged);
                    }
                }
            }

            if (union is null) return;

            canvas.Save();
            canvas.ClipPath(union, SKClipOperation.Difference, antialias: true);
            using var dimPaint = new SKPaint
            {
                Color = new SKColor(0, 0, 0, MaskAlpha),
                Style = SKPaintStyle.Fill,
            };
            canvas.DrawRect(SKRect.Create(0, 0, canvasWidth, canvasHeight), dimPaint);
            canvas.Restore();

            using var rimPaint = new SKPaint
            {
                Color = new SKColor(0xFF, 0xFF, 0xFF, 90),
                StrokeWidth = 1.25f,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true,
            };
            canvas.DrawPath(union, rimPaint);
        }
        finally
        {
            union?.Dispose();
        }
    }
}
