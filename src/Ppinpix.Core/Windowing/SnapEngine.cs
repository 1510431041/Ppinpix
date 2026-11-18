using Ppinpix.Core.Geometry;

namespace Ppinpix.Core.Windowing;

/// <summary>吸附结果的来源说明，用于在界面上提示“已吸附到某窗口”。</summary>
public sealed record SnapResult(PxRect Rect, WindowInfo? Target, int DeltaX, int DeltaY)
{
    public bool Snapped => Target is not null;
}

/// <summary>
/// 吸附引擎：窗口/控件边缘识别与磁吸。
/// 纯计算、无窗口消息依赖，便于单元测试。
/// </summary>
public sealed class SnapEngine
{
    /// <summary>计算吸附时最多参考的候选数量，避免在几百个控件里做全量比较。</summary>
    private const int MaxCandidates = 24;

    private readonly List<WindowInfo> _targets;
    private readonly WindowInfo[] _rootWindows;

    public SnapEngine(IEnumerable<WindowInfo> targets)
    {
        _targets = [.. targets.Where(target => !target.Bounds.IsEmpty)];
        _rootWindows = _targets.Where(target => target.Level == SnapLevel.Window)
            .OrderBy(target => target.ZOrder).ToArray();
    }

    public IReadOnlyList<WindowInfo> Targets => _targets;

    public bool IsEmpty => _targets.Count == 0;

    /// <summary>
    /// 返回包含该点的最前层窗口内的候选，按面积从小到大排列。
    /// 索引 0 = 最具体（通常是按钮/输入框），越往后越粗（面板 → 窗口）。
    /// </summary>
    public IReadOnlyList<WindowInfo> Stack(PxPoint p)
    {
        var front = FrontWindowAt(p);
        if (front is null) return [];

        var hits = new List<WindowInfo>(8);
        foreach (var t in _targets)
        {
            if (t.RootHandle == front.RootHandle && t.Bounds.Contains(p)) hits.Add(t);
        }

        if (hits.Count <= 1) return hits;

        hits.Sort(static (a, b) =>
        {
            int areaOrder = a.Bounds.AreaPx.CompareTo(b.Bounds.AreaPx);
            if (areaOrder != 0) return areaOrder;

            int depthOrder = b.Depth.CompareTo(a.Depth);
            return depthOrder != 0 ? depthOrder : a.ZOrder.CompareTo(b.ZOrder);
        });
        return hits;
    }

    private WindowInfo? FrontWindowAt(PxPoint point)
    {
        foreach (var window in _rootWindows)
        {
            if (window.Bounds.Contains(point)) return window;
        }
        return null;
    }

    private bool HasVisibleEdge(WindowInfo target, int edge, int start, int end, bool vertical)
    {
        // Right/Bottom 是半开边界，取边内的像素来判断该边是否被前方窗口遮住。
        var bounds = target.Bounds;
        edge = vertical ? Math.Clamp(edge, bounds.Left, bounds.Right - 1) : Math.Clamp(edge, bounds.Top, bounds.Bottom - 1);
        int min = vertical ? bounds.Top : bounds.Left;
        int max = vertical ? bounds.Bottom - 1 : bounds.Right - 1;
        start = Math.Clamp(start, min, max);
        end = Math.Clamp(end, min, max);
        while (start <= end)
        {
            var front = FrontWindowAt(vertical ? new PxPoint(edge, start) : new PxPoint(start, edge));
            if (front is null) return false;
            if (front.RootHandle == target.RootHandle) return true;

            // 跳过遮挡部分；移动整块选区时，只需对齐的边段有一部分可见。
            start = vertical ? front.Bounds.Bottom : front.Bounds.Right;
        }
        return false;
    }

    /// <summary>
    /// 按层级取吸附目标。<paramref name="level"/> 为 0 时取最具体的一个，
    /// 递增则向更大的容器（父窗口）方向移动；越界时返回最外层。
    /// </summary>
    public WindowInfo? Probe(PxPoint p, int level = 0)
    {
        var stack = Stack(p);
        if (stack.Count == 0) return null;

        int index = Math.Clamp(level, 0, stack.Count - 1);
        return stack[index];
    }

    /// <summary>包含该点的层级数量，用于判断滚轮还能不能继续向外切换。</summary>
    public int LevelCount(PxPoint p) => Stack(p).Count;

    /// <summary>
    /// 拖动选区时把「正在移动的角」吸附到附近窗口边缘。
    /// </summary>
    /// <param name="selection">当前选区（未吸附）。</param>
    /// <param name="movingCorner">正在跟随光标的角点。</param>
    /// <param name="thresholdPx">吸附阈值（物理像素）。</param>
    /// <param name="enabled">按住 Alt 等临时关闭吸附时传 false。</param>
    public SnapResult SnapMovingCorner(PxRect selection, PxPoint movingCorner, int thresholdPx, bool enabled = true)
    {
        if (!enabled || thresholdPx <= 0 || _targets.Count == 0)
        {
            return new SnapResult(selection, null, 0, 0);
        }

        var candidates = NearCandidates(new PxRect(movingCorner.X, movingCorner.Y, 1, 1), thresholdPx);
        if (candidates.Count == 0)
        {
            return new SnapResult(selection, null, 0, 0);
        }

        int bestDx = int.MaxValue;
        int bestDy = int.MaxValue;
        WindowInfo? bestX = null;
        WindowInfo? bestY = null;

        foreach (var t in candidates)
        {
            foreach (int edge in (ReadOnlySpan<int>)[t.Bounds.Left, t.Bounds.Right])
            {
                int d = edge - movingCorner.X;
                if (Math.Abs(d) <= thresholdPx &&
                    IsPointNearRange(movingCorner.Y, t.Bounds.Top, t.Bounds.Bottom, thresholdPx) &&
                    Math.Abs(d) < Math.Abs(bestDx) &&
                    HasVisibleEdge(t, edge, movingCorner.Y, movingCorner.Y, vertical: true))
                {
                    bestDx = d;
                    bestX = t;
                }
            }

            foreach (int edge in (ReadOnlySpan<int>)[t.Bounds.Top, t.Bounds.Bottom])
            {
                int d = edge - movingCorner.Y;
                if (Math.Abs(d) <= thresholdPx &&
                    IsPointNearRange(movingCorner.X, t.Bounds.Left, t.Bounds.Right, thresholdPx) &&
                    Math.Abs(d) < Math.Abs(bestDy) &&
                    HasVisibleEdge(t, edge, movingCorner.X, movingCorner.X, vertical: false))
                {
                    bestDy = d;
                    bestY = t;
                }
            }
        }

        int dx = bestX is null ? 0 : bestDx;
        int dy = bestY is null ? 0 : bestDy;
        if (dx == 0 && dy == 0)
        {
            return new SnapResult(selection, null, 0, 0);
        }

        // 高亮提示优先显示在 X 方向命中的目标上，没有则用 Y 方向的。
        var target = bestX ?? bestY;

        int left = selection.Left;
        int top = selection.Top;
        int right = selection.Right;
        int bottom = selection.Bottom;

        if (movingCorner.X == selection.Right) right += dx; else left += dx;
        if (movingCorner.Y == selection.Bottom) bottom += dy; else top += dy;

        return new SnapResult(PxRect.FromLtrb(left, top, right, bottom), target, dx, dy);
    }

    /// <summary>
    /// 把已确定的选区边缘吸附到附近窗口边界（用于键盘微调或粘贴后定位）。
    /// </summary>
    public SnapResult SnapRect(PxRect rect, int thresholdPx, bool enabled = true)
    {
        if (!enabled || thresholdPx <= 0 || _targets.Count == 0)
        {
            return new SnapResult(rect, null, 0, 0);
        }

        var candidates = NearCandidates(rect, thresholdPx);
        if (candidates.Count == 0)
        {
            return new SnapResult(rect, null, 0, 0);
        }

        int dx = 0, dy = 0;
        WindowInfo? target = null;
        int bestX = int.MaxValue, bestY = int.MaxValue;

        foreach (var t in candidates)
        {
            foreach ((int rectEdge, int targetEdge) in
                     (ReadOnlySpan<(int, int)>)[(rect.Left, t.Bounds.Left), (rect.Right, t.Bounds.Right)])
            {
                int d = targetEdge - rectEdge;
                if (Math.Abs(d) <= thresholdPx && Math.Abs(d) < Math.Abs(bestX) &&
                    HasVisibleEdge(t, targetEdge, rect.Top, rect.Bottom - 1, vertical: true))
                {
                    bestX = d;
                    dx = d;
                    target = t;
                }
            }

            foreach ((int rectEdge, int targetEdge) in
                     (ReadOnlySpan<(int, int)>)[(rect.Top, t.Bounds.Top), (rect.Bottom, t.Bounds.Bottom)])
            {
                int d = targetEdge - rectEdge;
                if (Math.Abs(d) <= thresholdPx && Math.Abs(d) < Math.Abs(bestY) &&
                    HasVisibleEdge(t, targetEdge, rect.Left, rect.Right - 1, vertical: false))
                {
                    bestY = d;
                    dy = d;
                    target ??= t;
                }
            }
        }

        if (dx == 0 && dy == 0) return new SnapResult(rect, null, 0, 0);
        return new SnapResult(rect.Offset(dx, dy), target, dx, dy);
    }

    /// <summary>挑出与选区足够接近的候选，并按面积升序保留最具体的一批。</summary>
    private List<WindowInfo> NearCandidates(PxRect rect, int thresholdPx)
    {
        // 矩形是半开区间，多包含一像素，保留恰好相距阈值的边。
        var expanded = rect.Inflate(thresholdPx + 1);
        var near = new List<WindowInfo>(16);

        foreach (var t in _targets)
        {
            if (t.Bounds.IntersectsWith(expanded)) near.Add(t);
        }

        if (near.Count > MaxCandidates)
        {
            near.Sort(static (a, b) => a.Bounds.AreaPx.CompareTo(b.Bounds.AreaPx));
            near.RemoveRange(MaxCandidates, near.Count - MaxCandidates);
        }

        return near;
    }

    private static bool IsPointNearRange(int value, int start, int end, int threshold) =>
        (long)value >= (long)start - threshold && (long)value <= (long)end + threshold;
}
