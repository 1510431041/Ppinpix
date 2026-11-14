using System.Runtime.InteropServices;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Geometry;
using SkiaSharp;

namespace Ppinpix.Capture.Annotation.Shapes;

/// <summary>缩放控制点标识。</summary>
public enum ShapeHandleKind
{
    None = 0,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
    Start,
    End,
    Center,
    CurveControl,
    TailEnd,
    CornerRadius,
}

/// <summary>单个控制点的物理像素位置与类型。</summary>
public readonly record struct ShapeHandle(ShapeHandleKind Kind, double X, double Y);

/// <summary>几何辅助矩形（物理像素浮点精度）。</summary>
public readonly record struct RectF(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width * 0.5;
    public double CenterY => Y + Height * 0.5;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static RectF FromPoints(double x1, double y1, double x2, double y2)
    {
        double left = Math.Min(x1, x2);
        double top = Math.Min(y1, y2);
        double right = Math.Max(x1, x2);
        double bottom = Math.Max(y1, y2);
        return new RectF(left, top, right - left, bottom - top);
    }

    public RectF Inflate(double amount) =>
        new(X - amount, Y - amount, Math.Max(0, Width + amount * 2), Math.Max(0, Height + amount * 2));

    public bool Contains(double px, double py) =>
        px >= Left && px <= Right && py >= Top && py <= Bottom;

    public bool Intersects(RectF other) =>
        Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;

    public PxRect ToPxRect() =>
        PxRect.FromLtrb(
            (int)Math.Floor(Left),
            (int)Math.Floor(Top),
            (int)Math.Ceiling(Right),
            (int)Math.Ceiling(Bottom));
}

/// <summary>只读二维点（物理像素）。</summary>
public readonly record struct PointF(double X, double Y);

/// <summary>标注渲染上下文，承载原始冻结帧以便马赛克与高斯模糊采样原图。</summary>
public sealed class AnnotationRenderContext : IDisposable
{
    private SKBitmap? _sourceBitmap;

    public AnnotationRenderContext(ScreenFrame sourceFrame)
    {
        SourceFrame = sourceFrame;
    }

    /// <summary>原始未叠加标注的桌面/画布帧。</summary>
    public ScreenFrame SourceFrame { get; }

    /// <summary>按需缓存在 Skia 中的原图位图（BGRA8888），避免每个马赛克块重复分配。</summary>
    public SKBitmap EnsureSourceBitmap()
    {
        if (_sourceBitmap is not null) return _sourceBitmap;

        var info = new SKImageInfo(SourceFrame.Width, SourceFrame.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bmp = new SKBitmap(info);
        IntPtr dst = bmp.GetPixels();
        byte[] sourceBytes = SourceFrame.Pixels.ToArray();
        int rowBytes = Math.Min(info.RowBytes, SourceFrame.Stride);
        for (int y = 0; y < SourceFrame.Height; y++)
        {
            Marshal.Copy(sourceBytes, y * SourceFrame.Stride, dst + y * info.RowBytes, rowBytes);
        }
        _sourceBitmap = bmp;
        return bmp;
    }

    public void Dispose()
    {
        _sourceBitmap?.Dispose();
        _sourceBitmap = null;
    }
}

/// <summary>所有标注图形的统一接口。所有坐标均为画布物理像素。</summary>
public interface IShape
{
    Guid Id { get; }

    AnnotationTool Tool { get; }

    ShapeStyle Style { get; set; }

    /// <summary>外接包围盒（物理像素）。</summary>
    RectF Bounds { get; }

    /// <summary>是否构成有效图形（例如零尺寸拖放不应加入撤销栈）。</summary>
    bool IsValid { get; }

    /// <summary>拖动创建/更新终点。</summary>
    void UpdateDrag(double x, double y, bool constrain = false);

    /// <summary>命中测试（用于选择与橡皮擦），tolerance 为容差像素。</summary>
    bool HitTest(double x, double y, double tolerance = 6);

    /// <summary>返回可拖动的控制点列表。</summary>
    IReadOnlyList<ShapeHandle> GetHandles();

    /// <summary>拖动指定控制点更新几何尺寸。</summary>
    void MoveHandle(ShapeHandleKind kind, double x, double y, bool constrain = false);

    /// <summary>整体平移。</summary>
    void Translate(double dx, double dy);

    /// <summary>创建深拷贝（用于撤销/重做与 Ctrl+D 复制）。</summary>
    IShape Clone(bool newId = false);

    /// <summary>使用 Skia 渲染到画布上，offset 为画布原点（例如选区左上角）。</summary>
    void Render(SKCanvas canvas, AnnotationRenderContext ctx, double originX, double originY);
}
