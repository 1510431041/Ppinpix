using Ppinpix.Core.Capture;
using Ppinpix.Core.Geometry;
using SkiaSharp;

namespace Ppinpix.Capture.Annotation.Shapes;

/// <summary>高斯模糊局部遮挡（严格从原始冻结 ScreenFrame 采样，Clamp 边缘避免黑边）。</summary>
public sealed class BlurShape : IShape, IDisposable
{
    private double _anchorX;
    private double _anchorY;

    private SKBitmap? _cachedPatch;
    private PxRect _cachedRegion;
    private int _cachedRadius;

    public BlurShape(double startX, double startY, ShapeStyle style, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        _anchorX = startX;
        _anchorY = startY;
        Rect = new RectF(startX, startY, 0, 0);
        Style = style;
    }

    public BlurShape(RectF rect, ShapeStyle style, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        _anchorX = rect.Left;
        _anchorY = rect.Top;
        Rect = rect;
        Style = style;
    }

    public Guid Id { get; }

    public AnnotationTool Tool => AnnotationTool.Blur;

    public ShapeStyle Style { get; set; }

    public RectF Rect { get; private set; }

    public RectF Bounds => Rect;

    public bool IsValid => Rect.Width >= 4 && Rect.Height >= 4;

    public void UpdateDrag(double x, double y, bool constrain = false)
    {
        if (constrain) (x, y) = GeometryHelper.ConstrainSquare(_anchorX, _anchorY, x, y);
        Rect = RectF.FromPoints(_anchorX, _anchorY, x, y);
    }

    public bool HitTest(double x, double y, double tolerance = 4) =>
        Rect.Inflate(tolerance).Contains(x, y);

    public IReadOnlyList<ShapeHandle> GetHandles() => GeometryHelper.GetBoxHandles(Rect);

    public void MoveHandle(ShapeHandleKind kind, double x, double y, bool constrain = false)
    {
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
        new BlurShape(Rect, Style, newId ? Guid.NewGuid() : Id);

    public SKBitmap? GetOrBuildPatch(AnnotationRenderContext ctx, out PxRect clampedRegion)
    {
        clampedRegion = Rect.ToPxRect().Intersect(ctx.SourceFrame.Bounds);
        if (clampedRegion.IsEmpty) return null;

        int radius = Math.Clamp(Style.BlurRadius, 2, 48);
        if (_cachedPatch is not null &&
            _cachedRegion == clampedRegion &&
            _cachedRadius == radius)
        {
            return _cachedPatch;
        }

        _cachedPatch?.Dispose();
        _cachedPatch = BuildBlurBitmap(ctx.SourceFrame, ctx.EnsureSourceBitmap(), clampedRegion, radius);
        _cachedRegion = clampedRegion;
        _cachedRadius = radius;
        return _cachedPatch;
    }

    public void Render(SKCanvas canvas, AnnotationRenderContext ctx, double originX, double originY)
    {
        if (!IsValid) return;
        var patch = GetOrBuildPatch(ctx, out var clamped);
        if (patch is null) return;

        canvas.DrawBitmap(
            patch,
            (float)(clamped.X - originX),
            (float)(clamped.Y - originY));
    }

    private static SKBitmap BuildBlurBitmap(ScreenFrame source, SKBitmap sourceBmp, PxRect region, int radius)
    {
        int pad = radius * 2;
        var expanded = PxRect.FromLtrb(region.Left - pad, region.Top - pad, region.Right + pad, region.Bottom + pad)
            .Intersect(source.Bounds);

        int srcX = expanded.Left - source.Bounds.Left;
        int srcY = expanded.Top - source.Bounds.Top;
        var srcSubsetRect = SKRectI.Create(srcX, srcY, expanded.Width, expanded.Height);

        using var cropped = new SKBitmap();
        sourceBmp.ExtractSubset(cropped, srcSubsetRect);

        var blurredInfo = new SKImageInfo(expanded.Width, expanded.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(blurredInfo);
        var c = surface.Canvas;
        float sigma = Math.Max(1f, radius * 0.65f);
        using var filter = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { ImageFilter = filter };
        c.DrawBitmap(cropped, 0, 0, paint);
        c.Flush();

        using var snap = surface.Snapshot();
        int offsetX = region.Left - expanded.Left;
        int offsetY = region.Top - expanded.Top;
        var finalInfo = new SKImageInfo(region.Width, region.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var finalBmp = new SKBitmap(finalInfo);
        using var finalCanvas = new SKCanvas(finalBmp);
        finalCanvas.DrawImage(
            snap,
            SKRect.Create(offsetX, offsetY, region.Width, region.Height),
            SKRect.Create(0, 0, region.Width, region.Height));
        return finalBmp;
    }

    public void Dispose()
    {
        _cachedPatch?.Dispose();
        _cachedPatch = null;
    }
}
