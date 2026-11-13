using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using Ppinpix.Core;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Input;
using Ppinpix.Core.Interop;
using Ppinpix.Core.Windowing;

namespace Ppinpix.Capture.Windowing;

/// <summary>
/// 基于 UI Automation 的控件边界采集器。
/// 严格限制耗时（默认 35ms 预算），在后台线程执行，任何异常或超时均静默降级为窗口级识别。
/// </summary>
public static class UiaControlCollector
{
    private const int DefaultBudgetMs = 35;
    private const int MaxControls = 90;
    private const int MinControlSizePx = 10;

    /// <summary>采集鼠标所在顶层窗口内的可见 UIA 控件矩形。</summary>
    public static IReadOnlyList<WindowInfo> CollectNearCursor(
        PxRect virtualBounds,
        IReadOnlyCollection<nint>? excludeHandles = null,
        int budgetMs = DefaultBudgetMs)
    {
        var excluded = excludeHandles is null ? [] : new HashSet<nint>(excludeHandles);
        var cursor = GlobalPointer.GetPosition();
        nint rootHwnd = ResolveTargetRootWindow(cursor, excluded);
        if (rootHwnd == 0) return [];

        using var cts = new CancellationTokenSource(Math.Max(10, budgetMs));
        var task = Task.Run(() => CollectFromWindow(rootHwnd, virtualBounds, cts.Token), cts.Token);

        try
        {
            if (task.Wait(Math.Max(15, budgetMs + 10)))
            {
                return task.Result;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("UIA 控件预取超时或异常，已降级为窗口级。", ex);
        }

        return [];
    }

    private static nint ResolveTargetRootWindow(PxPoint cursor, HashSet<nint> excluded)
    {
        nint hwnd = WindowApi.WindowFromPoint(new DisplayApi.POINT(cursor.X, cursor.Y));
        if (hwnd != 0)
        {
            nint root = WindowApi.GetAncestor(hwnd, 2); // GA_ROOT
            if (root != 0) hwnd = root;
            if (!excluded.Contains(hwnd) && WindowApi.IsWindowVisible(hwnd))
            {
                return hwnd;
            }
        }

        nint fg = WindowApi.GetForegroundWindow();
        if (fg != 0 && !excluded.Contains(fg) && WindowApi.IsWindowVisible(fg))
        {
            return fg;
        }

        return 0;
    }

    private static List<WindowInfo> CollectFromWindow(nint rootHwnd, PxRect virtualBounds, CancellationToken ct)
    {
        var results = new List<WindowInfo>(32);
        var seen = new HashSet<PxRect>();
        var sw = Stopwatch.StartNew();

        try
        {
            var rootElement = AutomationElement.FromHandle(rootHwnd);
            if (rootElement is null) return results;

            var walker = TreeWalker.ControlViewWalker;
            var queue = new Queue<(AutomationElement Element, int Depth)>();
            queue.Enqueue((rootElement, 0));

            int z = 0;
            while (queue.Count > 0 && results.Count < MaxControls && !ct.IsCancellationRequested && sw.ElapsedMilliseconds < 32)
            {
                var (current, depth) = queue.Dequeue();

                AutomationElement? child;
                try
                {
                    child = walker.GetFirstChild(current);
                }
                catch
                {
                    continue;
                }

                while (child is not null && results.Count < MaxControls && !ct.IsCancellationRequested && sw.ElapsedMilliseconds < 32)
                {
                    try
                    {
                        var info = child.Current;
                        if (!info.IsOffscreen)
                        {
                            Rect rect = info.BoundingRectangle;
                            if (!rect.IsEmpty && rect.Width >= MinControlSizePx && rect.Height >= MinControlSizePx)
                            {
                                var px = PxRect.FromLtrb(
                                    (int)Math.Round(rect.Left),
                                    (int)Math.Round(rect.Top),
                                    (int)Math.Round(rect.Right),
                                    (int)Math.Round(rect.Bottom)).Intersect(virtualBounds);

                                if (!px.IsEmpty && px.Width >= MinControlSizePx && px.Height >= MinControlSizePx && seen.Add(px))
                                {
                                    results.Add(new WindowInfo(
                                        (nint)info.NativeWindowHandle,
                                        rootHwnd,
                                        px,
                                        info.Name ?? string.Empty,
                                        info.ControlType?.ProgrammaticName ?? "Control",
                                        SnapLevel.Control,
                                        depth + 1,
                                        z++));
                                }
                            }

                            if (depth < 5)
                            {
                                queue.Enqueue((child, depth + 1));
                            }
                        }

                        child = walker.GetNextSibling(child);
                    }
                    catch
                    {
                        break;
                    }
                }
            }
        }
        catch
        {
            // 部分 Electron / 游戏窗口可能不支持 UIA，静默返回已有结果。
        }

        return results;
    }
}
