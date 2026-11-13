using System.Runtime.InteropServices;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Interop;
namespace Ppinpix.Core.Display;

/// <summary>一块显示器的不可变快照。</summary>
public sealed record MonitorInfo(
    nint Handle,
    string DeviceName,
    PxRect Bounds,
    PxRect WorkArea,
    uint Dpi,
    bool IsPrimary)
{
    public DpiScale DpiScale => new(Dpi);

    public double Scale => Dpi / 96.0;

    public override string ToString() =>
        $"{DeviceName} {Bounds} @{Dpi}dpi{(IsPrimary ? " (primary)" : string.Empty)}";
}

/// <summary>
/// 显示器服务：枚举显示器、按点/窗口定位显示器、监听显示配置变化。
/// 所有坐标均为物理像素。
/// </summary>
public sealed class DisplayService : IDisposable
{
    private readonly MessageWindow _messageWindow;
    private MonitorInfo[] _monitors = [];
    private PxRect _virtualBounds;

    public DisplayService()
    {
        _messageWindow = new MessageWindow("Ppinpix.DisplayService");
        _messageWindow.Message += OnMessage;
        Refresh();
    }

    /// <summary>显示器拓扑或 DPI 发生变化（已自动 Refresh）。在 UI 线程之外的线程上不要直接操作界面。</summary>
    public event EventHandler? DisplaysChanged;

    public IReadOnlyList<MonitorInfo> Monitors => _monitors;

    /// <summary>所有显示器外接矩形的并集（虚拟桌面边界）。</summary>
    public PxRect VirtualBounds => _virtualBounds;

    /// <summary>距离给定窗口最近的显示器；窗口句柄无效时返回主显示器。</summary>
    public MonitorInfo FromWindow(nint hwnd)
    {
        var handle = hwnd != 0 ? DisplayApi.MonitorFromWindow(hwnd, DisplayApi.MONITOR_DEFAULTTONEAREST) : 0;
        return FromHandle(handle) ?? _monitors.FirstOrDefault(m => m.IsPrimary) ?? _monitors[0];
    }

    public MonitorInfo FromPoint(PxPoint p)
    {
        var handle = DisplayApi.MonitorFromPoint(
            new DisplayApi.POINT(p.X, p.Y), DisplayApi.MONITOR_DEFAULTTONEAREST);
        return FromHandle(handle) ?? _monitors.FirstOrDefault(m => m.IsPrimary) ?? _monitors[0];
    }

    private MonitorInfo? FromHandle(nint handle)
    {
        foreach (var m in _monitors)
        {
            if (m.Handle == handle) return m;
        }
        return null;
    }

    public void Refresh()
    {
        var list = new List<MonitorInfo>(4);
        var virtualBounds = PxRect.Empty;

        DisplayApi.MonitorEnumProc callback = (nint hMonitor, nint _, ref DisplayApi.RECT _, nint _) =>
        {
            var info = new DisplayApi.MONITORINFOEX { cbSize = Marshal.SizeOf<DisplayApi.MONITORINFOEX>() };
            if (!DisplayApi.GetMonitorInfo(hMonitor, ref info)) return true;

            uint dpi = 96;
            if (DisplayApi.GetDpiForMonitor(hMonitor, DisplayApi.MDT_EFFECTIVE_DPI, out uint dpiX, out _) != 0)
            {
                dpiX = 0;
            }
            if (dpiX > 0) dpi = dpiX;

            var bounds = info.rcMonitor.ToPxRect();
            var monitor = new MonitorInfo(
                hMonitor,
                info.szDevice ?? string.Empty,
                bounds,
                info.rcWork.ToPxRect(),
                dpi,
                (info.dwFlags & DisplayApi.MONITORINFOF_PRIMARY) != 0);

            list.Add(monitor);
            virtualBounds = virtualBounds.Union(bounds);
            return true;
        };

        DisplayApi.EnumDisplayMonitors(0, 0, callback, 0);
        GC.KeepAlive(callback);

        if (list.Count == 0)
        {
            // 理论上不会发生；保底构造一个主显示器。
            uint dpi = Math.Max(96u, DisplayApi.GetDpiForSystem());
            list.Add(new MonitorInfo(0, @"\\.\DISPLAY1", new PxRect(0, 0, 1920, 1080), new PxRect(0, 0, 1920, 1040), dpi, true));
            virtualBounds = list[0].Bounds;
        }

        list.Sort(static (a, b) => (a.Bounds.X, a.Bounds.Y).CompareTo((b.Bounds.X, b.Bounds.Y)));

        bool changed = list.Count != _monitors.Length;
        if (!changed)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != _monitors[i]) { changed = true; break; }
            }
        }

        _monitors = [.. list];
        _virtualBounds = virtualBounds;

        if (changed) DisplaysChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg is WindowApi.WM_DISPLAYCHANGE or WindowApi.WM_DPICHANGED or WindowApi.WM_DEVICECHANGE)
        {
            Refresh();
        }
    }

    public void Dispose() => _messageWindow.Dispose();
}
