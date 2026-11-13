using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Display;
using Ppinpix.Core.Geometry;

namespace Ppinpix.Capture.Overlay;

/// <summary>
/// 冻结帧到 WPF 位图的转换。
/// 整块虚拟桌面只做一次像素拷贝，每块显示器通过 CroppedBitmap 取视图，避免多次整屏复制。
/// </summary>
public static class FrameBitmap
{
    private static readonly object Sync = new();
    private static ScreenFrame? _cachedFrame;
    private static BitmapSource? _cachedSource;

    /// <summary>取整个冻结帧对应的位图（同一帧重复调用只转换一次）。</summary>
    public static BitmapSource GetFullFrameSource(ScreenFrame frame)
    {
        lock (Sync)
        {
            if (ReferenceEquals(_cachedFrame, frame) && _cachedSource is not null)
            {
                return _cachedSource;
            }

            var source = BitmapSource.Create(
                frame.Width,
                frame.Height,
                96, 96,
                PixelFormats.Bgra32,
                null,
                frame.Pixels.ToArray(),
                frame.Stride);

            source.Freeze();
            _cachedFrame = frame;
            _cachedSource = source;
            return source;
        }
    }

    /// <summary>取某块显示器对应的视图（相对整帧裁剪，不复制像素）。</summary>
    public static ImageSource GetMonitorView(ScreenFrame frame, MonitorInfo monitor)
    {
        var full = GetFullFrameSource(frame);
        var local = monitor.Bounds.Intersect(frame.Bounds);
        if (local.IsEmpty) return full;

        var cropped = new CroppedBitmap(full, new Int32Rect(
            local.X - frame.Bounds.X,
            local.Y - frame.Bounds.Y,
            local.Width,
            local.Height));
        cropped.Freeze();
        return cropped;
    }
}
