using System.Runtime.InteropServices;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Geometry;
using SkiaSharp;

namespace Ppinpix.Capture.Annotation.Shapes;

/// <summary>马赛克局部遮挡（严格从原始冻结 ScreenFrame 采样，永不受下层标注干扰）。</summary>
public sealed class MosaicShape : IShape, IDisposable
{
    private double _anchorX;
    private double _anchorY;

    private SKBitmap? _cachedPatch;
    private PxRect _cachedRegion;
    private int _cachedBlockPx;
    private nint _cachedFrameStamp;

    public MosaicShape(double startX, double startY, ShapeStyle style, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        _anchorX = startX;
        _anchorY = startY;
        Rect = new RectF(startX, startY, 0, 0);
        Style = style;
    }

    public MosaicShape(RectF rect, ShapeStyle style, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        _anchorX = rect.Left;
        _anchorY = rect.Top;
        Rect = rect;
        Style = style;
    }

    public Guid Id { get; }

    public AnnotationTool Tool => AnnotationTool.Mosaic;

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
        new MosaicShape(Rect, Style, newId ? Guid.NewGuid() : Id);

    /// <summary>生成或复用该马赛克区域相对于原图帧的像素块位图。</summary>
    public SKBitmap? GetOrBuildPatch(ScreenFrame source, out PxRect clampedRegion)
    {
        clampedRegion = Rect.ToPxRect().Intersect(source.Bounds);
        if (clampedRegion.IsEmpty) return null;

        int blockPx = Math.Clamp(Style.MosaicBlockPx, 4, 48);
        nint stamp = source.Bounds.X * 397 ^ source.Bounds.Y ^ source.Width * 17 ^ source.Height;

        if (_cachedPatch is not null &&
            _cachedRegion == clampedRegion &&
            _cachedBlockPx == blockPx &&
            _cachedFrameStamp == stamp)
        {
            return _cachedPatch;
        }

        _cachedPatch?.Dispose();
        _cachedPatch = BuildMosaicBitmap(source, clampedRegion, blockPx);
        _cachedRegion = clampedRegion;
        _cachedBlockPx = blockPx;
        _cachedFrameStamp = stamp;
        return _cachedPatch;
    }

    public void Render(SKCanvas canvas, AnnotationRenderContext ctx, double originX, double originY)
    {
        if (!IsValid) return;
        var patch = GetOrBuildPatch(ctx.SourceFrame, out var clamped);
        if (patch is null) return;

        GeometryHelper.DrawClippedPatch(canvas, patch, clamped, Rect, Style.CornerRadius, originX, originY);
    }

    /// <summary>按桌面绝对网格对齐生成块状平均色，拖动区域边缘时方块稳定不闪烁。</summary>
    private static SKBitmap BuildMosaicBitmap(ScreenFrame source, PxRect region, int blockPx)
    {
        int w = region.Width;
        int h = region.Height;
        byte[] srcBytes = source.Pixels.ToArray();
        int srcStride = source.Stride;
        int srcLeft = source.Bounds.X;
        int srcTop = source.Bounds.Y;
        int srcMaxX = source.Bounds.Right - 1;
        int srcMaxY = source.Bounds.Bottom - 1;

        byte[] dst = new byte[w * h * 4];
        int dstStride = w * 4;

        // 按桌面网格切块循环，每块只算一次平均色
        int gridLeft = FloorGrid(region.Left, blockPx);
        int gridTop = FloorGrid(region.Top, blockPx);

        for (int gy = gridTop; gy < region.Bottom; gy += blockPx)
        {
            int yStart = Math.Max(gy, region.Top);
            int yEnd = Math.Min(gy + blockPx, region.Bottom);

            for (int gx = gridLeft; gx < region.Right; gx += blockPx)
            {
                int xStart = Math.Max(gx, region.Left);
                int xEnd = Math.Min(gx + blockPx, region.Right);

                int sampleXStart = Math.Clamp(gx, source.Bounds.Left, srcMaxX + 1);
                int sampleXEnd = Math.Clamp(gx + blockPx, source.Bounds.Left, srcMaxX + 1);
                int sampleYStart = Math.Clamp(gy, source.Bounds.Top, srcMaxY + 1);
                int sampleYEnd = Math.Clamp(gy + blockPx, source.Bounds.Top, srcMaxY + 1);

                long sumB = 0, sumG = 0, sumR = 0, count = 0;
                for (int sy = sampleYStart; sy < sampleYEnd; sy++)
                {
                    int rowOffset = (sy - srcTop) * srcStride;
                    for (int sx = sampleXStart; sx < sampleXEnd; sx++)
                    {
                        int i = rowOffset + (sx - srcLeft) * 4;
                        sumB += srcBytes[i];
                        sumG += srcBytes[i + 1];
                        sumR += srcBytes[i + 2];
                        count++;
                    }
                }

                byte b = count > 0 ? (byte)(sumB / count) : (byte)0;
                byte g = count > 0 ? (byte)(sumG / count) : (byte)0;
                byte r = count > 0 ? (byte)(sumR / count) : (byte)0;

                for (int dy = yStart; dy < yEnd; dy++)
                {
                    int rowOffset = (dy - region.Top) * dstStride;
                    for (int dx = xStart; dx < xEnd; dx++)
                    {
                        int di = rowOffset + (dx - region.Left) * 4;
                        dst[di] = b;
                        dst[di + 1] = g;
                        dst[di + 2] = r;
                        dst[di + 3] = 255;
                    }
                }
            }
        }

        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bmp = new SKBitmap(info);
        Marshal.Copy(dst, 0, bmp.GetPixels(), dst.Length);
        return bmp;
    }

    private static int FloorGrid(int v, int step)
    {
        int rem = v % step;
        if (rem < 0) rem += step;
        return v - rem;
    }

    public void Dispose()
    {
        _cachedPatch?.Dispose();
        _cachedPatch = null;
    }
}
