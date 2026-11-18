using System.Text;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Interop;

namespace Ppinpix.Core.Windowing;

/// <summary>吸附目标的层级。</summary>
public enum SnapLevel
{
    /// <summary>顶层窗口。</summary>
    Window = 0,

    /// <summary>顶层窗口内的子窗口（按钮、输入框、面板…）。</summary>
    Child = 1,

    /// <summary>UI Automation 控件。第二阶段接入，当前仅在拿到结果时出现。</summary>
    Control = 2,
}

/// <summary>一个可被吸附的矩形目标（窗口或控件）。</summary>
public sealed record WindowInfo(
    nint Handle,
    nint RootHandle,
    PxRect Bounds,
    string Title,
    string ClassName,
    SnapLevel Level,
    int Depth,
    int ZOrder)
{
    /// <summary>用于比较的稳定标识：同一块矩形在多次枚举中应保持一致。</summary>
    public string Key => $"{Handle:X}:{Bounds.X},{Bounds.Y},{Bounds.Width},{Bounds.Height}";

    public override string ToString() => $"[{Level}] {Bounds} {ClassName} \"{Title}\"";
}

/// <summary>
/// 枚举当前屏幕上的窗口与子窗口，作为截图时的吸附候选。
/// 必须在覆盖层显示 <b>之前</b> 调用：覆盖层一旦出现，枚举结果就只有我们自己。
/// </summary>
public sealed class WindowEnumerator
{
    private const int MaxTitleLength = 256;
    private const int MaxClassLength = 256;
    private const int MinChildSizePx = 12;
    private const int MinWindowSizePx = 16;

    /// <summary>收集吸附候选，按 Z 序从上到下排列。</summary>
    /// <param name="virtualBounds">虚拟桌面边界，用于过滤屏幕外的窗口。</param>
    /// <param name="excludeHandles">需要排除的窗口（本项目自己的窗口）。</param>
    /// <param name="includeChildren">是否包含子窗口。</param>
    /// <param name="includeUiaControls">是否尝试抓取 UIA 控件（第二阶段实现）。</param>
    public IReadOnlyList<WindowInfo> Collect(
        PxRect virtualBounds,
        IReadOnlyCollection<nint>? excludeHandles = null,
        bool includeChildren = true,
        bool includeUiaControls = false,
        Func<PxRect, IReadOnlyCollection<nint>?, IReadOnlyList<WindowInfo>>? uiaCollector = null)
    {
        var excluded = excludeHandles is null ? [] : new HashSet<nint>(excludeHandles);
        var result = new List<WindowInfo>(128);
        int zOrder = 0;

        WindowApi.EnumWindowsProc topLevel = (nint hwnd, nint _) =>
        {
            var root = hwnd;
            if (!excluded.Contains(root) && TryGetWindowRect(root, out var windowRect))
            {
                string className = GetClassName(root);
                if (IsSnapCandidate(root, windowRect, virtualBounds, MinWindowSizePx, className))
                {
                    result.Add(new WindowInfo(
                        root, root, windowRect,
                        GetTitle(root), className,
                        SnapLevel.Window, 0, zOrder));

                    if (includeChildren)
                    {
                        CollectChildren(root, windowRect.Intersect(virtualBounds), excluded);
                    }
                }
            }

            zOrder++;

            return true;
        };

        WindowApi.EnumWindows(topLevel, 0);
        GC.KeepAlive(topLevel);

        result.AddRange(_children);
        _children.Clear();

        if (includeUiaControls && uiaCollector is not null)
        {
            try
            {
                var uiaTargets = uiaCollector(virtualBounds, excluded);
                var roots = result.Where(target => target.Level == SnapLevel.Window)
                    .ToDictionary(target => target.Handle, target => target.Bounds.Intersect(virtualBounds));
                foreach (var target in uiaTargets)
                {
                    // UIA 不能把已过滤的桌面宿主、隐藏窗口或其屏幕外控件重新加回来。
                    if ((target.Handle != 0 && excluded.Contains(target.Handle)) ||
                        !roots.TryGetValue(target.RootHandle, out var rootBounds)) continue;
                    var bounds = target.Bounds.Intersect(rootBounds);
                    if (!bounds.IsEmpty) result.Add(target with { Bounds = bounds });
                }
            }
            catch (Exception ex)
            {
                Log.Warn("WindowEnumerator: UIA 控件枚举失败，已降级为窗口级吸附。", ex);
            }
        }

        return result;
    }

    private readonly List<WindowInfo> _children = [];

    private void CollectChildren(nint parent, PxRect rootBounds, HashSet<nint> excluded)
    {
        int childZ = 0;
        WindowApi.EnumWindowsProc childProc = (nint hwnd, nint _) =>
        {
            childZ++;
            if (excluded.Contains(hwnd)) return true;
            if (!TryGetWindowRect(hwnd, out var rect)) return true;
            rect = rect.Intersect(rootBounds);
            string className = GetClassName(hwnd);
            if (!IsSnapCandidate(hwnd, rect, rootBounds, MinChildSizePx, className)) return true;

            _children.Add(new WindowInfo(
                hwnd, parent, rect,
                GetTitle(hwnd), className,
                SnapLevel.Child, 1, childZ));

            return true;
        };

        WindowApi.EnumChildWindows(parent, childProc, 0);
        GC.KeepAlive(childProc);
    }

    /// <summary>取窗口矩形：优先用 DWM 的“扩展边框”，可去掉 Win10/11 窗口的隐形阴影外沿。</summary>
    private static bool TryGetWindowRect(nint hwnd, out PxRect rect)
    {
        rect = PxRect.Empty;

        if (DwmApi.DwmGetWindowAttribute(
                hwnd, DwmApi.DWMWA_EXTENDED_FRAME_BOUNDS,
                out DisplayApi.RECT dwmRect, System.Runtime.InteropServices.Marshal.SizeOf<DisplayApi.RECT>()) == 0)
        {
            rect = dwmRect.ToPxRect();
            if (!rect.IsEmpty) return true;
        }

        if (!WindowApi.GetWindowRect(hwnd, out var winRect)) return false;

        rect = winRect.ToPxRect();
        return !rect.IsEmpty;
    }

    private static bool IsSnapCandidate(nint hwnd, PxRect rect, PxRect virtualBounds, int minSizePx, string className)
    {
        if (!WindowApi.IsWindowVisible(hwnd)) return false;
        if (WindowApi.IsIconic(hwnd)) return false;
        if (rect.Width < minSizePx || rect.Height < minSizePx) return false;
        if (!rect.IntersectsWith(virtualBounds)) return false;

        // 桌面宿主也会报告整屏矩形，但不是用户可见的应用窗口。
        if (className is "Progman" or "WorkerW") return false;

        // 任务栏、输入法候选框等工具窗口不参与吸附。
        long exStyle = WindowApi.GetWindowLongPtr(hwnd, WindowApi.GWL_EXSTYLE).ToInt64();
        if ((exStyle & WindowApi.WS_EX_TOOLWINDOW) != 0) return false;

        // DWM 隐藏（UWP 挂起、虚拟桌面切换残留）的窗口不可见。
        if (DwmApi.DwmGetWindowAttribute(hwnd, DwmApi.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;

        return true;
    }

    private static string GetTitle(nint hwnd)
    {
        var sb = new StringBuilder(MaxTitleLength);
        int len = WindowApi.GetWindowText(hwnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString(0, len) : string.Empty;
    }

    private static string GetClassName(nint hwnd)
    {
        var sb = new StringBuilder(MaxClassLength);
        int len = WindowApi.GetClassName(hwnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString(0, len) : string.Empty;
    }
}
