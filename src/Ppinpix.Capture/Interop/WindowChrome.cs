using System.Windows;
using System.Windows.Interop;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Interop;

namespace Ppinpix.Capture.Interop;

/// <summary>
/// 覆盖层/工具栏/放大镜这些无边框浮层的窗口样式与定位。
/// 全部窗口都用 SetWindowPos 以「物理像素」精确定位，不依赖 WPF 的 DIP 定位，
/// 这样在混合缩放的多显示器上才不会偏。
/// </summary>
internal static class WindowChrome
{
    /// <summary>
    /// 覆盖层：置顶、不进任务栏、不出现在 Alt+Tab。
    /// 注意这里 <b>不</b> 加 WS_EX_NOACTIVATE——覆盖层必须能拿到键盘焦点才能响应 Esc / 方向键；
    /// 工具栏才是不可激活的，这样点按钮不会把焦点从覆盖层抢走。
    /// </summary>
    public static void MakeOverlay(Window window)
    {
        ApplyExStyle(window, new WindowStyles
        {
            Topmost = true,
            ToolWindow = true,
        });
    }

    /// <summary>工具栏 / 放大镜：置顶但不抢焦点。</summary>
    public static void MakeNoActivate(Window window)
    {
        ApplyExStyle(window, new WindowStyles
        {
            Topmost = true,
            NoActivate = true,
            ToolWindow = true,
        });
    }

    /// <summary>鼠标穿透的浮层（放大镜）：完全不接收鼠标消息。WS_EX_TRANSPARENT 需配合 WS_EX_LAYERED 才生效。</summary>
    public static void MakeClickThrough(Window window)
    {
        ApplyExStyle(window, new WindowStyles
        {
            Topmost = true,
            NoActivate = true,
            ToolWindow = true,
            Transparent = true,
            Layered = true,
        });
    }

    private readonly record struct WindowStyles
    {
        public bool Topmost { get; init; }
        public bool NoActivate { get; init; }
        public bool ToolWindow { get; init; }
        public bool Transparent { get; init; }
        public bool Layered { get; init; }
    }

    private static void ApplyExStyle(Window window, WindowStyles styles)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;

        long exStyle = WindowApi.GetWindowLongPtr(handle, WindowApi.GWL_EXSTYLE).ToInt64();

        if (styles.ToolWindow) exStyle |= WindowApi.WS_EX_TOOLWINDOW;
        if (styles.NoActivate) exStyle |= WindowApi.WS_EX_NOACTIVATE;
        if (styles.Transparent) exStyle |= WindowApi.WS_EX_TRANSPARENT;
        if (styles.Layered) exStyle |= WindowApi.WS_EX_LAYERED;
        if (styles.Topmost) exStyle |= WindowApi.WS_EX_TOPMOST;

        WindowApi.SetWindowLongPtr(handle, WindowApi.GWL_EXSTYLE, unchecked((nint)exStyle));
    }

    /// <summary>按物理像素摆放窗口（左上角 + 尺寸）。</summary>
    public static void PlacePhysical(Window window, PxRect bounds)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;

        WindowApi.SetWindowPos(
            handle, WindowApi.HWND_TOPMOST,
            bounds.X, bounds.Y, bounds.Width, bounds.Height,
            WindowApi.SWP_NOACTIVATE | WindowApi.SWP_SHOWWINDOW);
    }

    /// <summary>只改位置，不改尺寸（工具栏/放大镜用，尺寸由内容决定）。</summary>
    public static void MovePhysical(Window window, PxPoint location, bool preserveZOrder = false)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;

        uint flags = WindowApi.SWP_NOSIZE | WindowApi.SWP_NOACTIVATE;
        if (preserveZOrder) flags |= WindowApi.SWP_NOZORDER;

        WindowApi.SetWindowPos(
            handle, WindowApi.HWND_TOPMOST,
            location.X, location.Y, 0, 0,
            flags);
    }

    /// <summary>把窗口提到最上层（不激活）。</summary>
    public static void BringToTopWithoutActivation(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;

        WindowApi.SetWindowPos(
            handle, WindowApi.HWND_TOPMOST, 0, 0, 0, 0,
            WindowApi.SWP_NOMOVE | WindowApi.SWP_NOSIZE | WindowApi.SWP_NOACTIVATE | WindowApi.SWP_SHOWWINDOW);
    }

    /// <summary>读取窗口当前的物理像素矩形（用于校验定位是否生效）。</summary>
    public static PxRect GetPhysicalBounds(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return PxRect.Empty;
        if (!WindowApi.GetWindowRect(handle, out var rect)) return PxRect.Empty;
        return rect.ToPxRect();
    }
}
