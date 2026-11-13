using Ppinpix.Core.Geometry;
using Ppinpix.Core.Windowing;

namespace Ppinpix.Capture.Session;

/// <summary>选区的 8 个调整手柄。</summary>
public enum SelectionHandle
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
}

/// <summary>当前鼠标在选区上的操作类型。</summary>
public enum DragMode
{
    None = 0,

    /// <summary>正在拉出新选区。</summary>
    Creating,

    /// <summary>正在整体移动选区。</summary>
    Moving,

    /// <summary>正在拖动某个手柄改尺寸。</summary>
    Resizing,
}

/// <summary>
/// 选区状态（几何 + 吸附提示）。坐标全部为虚拟桌面物理像素。
/// 拖动过程中用到的锚点/原始矩形由 CaptureSession 持有，这里只保存结果与命中判定。
/// </summary>
public sealed class SelectionState
{
    /// <summary>手柄的命中半径（DIP），绘制半径为 3.5 DIP。</summary>
    public const double HandleHitRadiusDip = 7.0;

    /// <summary>当前选区；未选择时为 <see cref="PxRect.Empty"/>。</summary>
    public PxRect Rect { get; set; } = PxRect.Empty;

    public bool HasSelection => !Rect.IsEmpty;

    public DragMode Mode { get; set; } = DragMode.None;

    public SelectionHandle ActiveHandle { get; set; } = SelectionHandle.None;

    /// <summary>悬停时高亮显示的窗口/控件矩形（尚未点击确定）。</summary>
    public PxRect Highlight { get; set; } = PxRect.Empty;

    /// <summary>高亮对应的吸附目标，用于徽标显示。</summary>
    public WindowInfo? HighlightTarget { get; set; }

    /// <summary>拖拽吸附时命中的目标，用于画吸附参考线。</summary>
    public WindowInfo? SnapTarget { get; set; }

    /// <summary>滚轮切换层级时的当前层级（0 = 最具体的控件）。</summary>
    public int Level { get; set; }

    public void Clear()
    {
        Rect = PxRect.Empty;
        Mode = DragMode.None;
        ActiveHandle = SelectionHandle.None;
        Highlight = PxRect.Empty;
        HighlightTarget = null;
        SnapTarget = null;
        Level = 0;
    }

    public void ClearHighlight()
    {
        Highlight = PxRect.Empty;
        HighlightTarget = null;
    }

    /// <summary>命中测试：返回光标所在的手柄。手柄只在已有选区时生效。</summary>
    public SelectionHandle HitTestHandle(PxPoint cursor, int radiusPx)
    {
        if (!HasSelection) return SelectionHandle.None;

        foreach (var handle in AllHandles)
        {
            var center = GetHandleCenter(Rect, handle);
            if (Math.Abs(cursor.X - center.X) <= radiusPx && Math.Abs(cursor.Y - center.Y) <= radiusPx)
            {
                return handle;
            }
        }

        return SelectionHandle.None;
    }

    public static readonly SelectionHandle[] AllHandles =
    [
        SelectionHandle.TopLeft, SelectionHandle.Top, SelectionHandle.TopRight,
        SelectionHandle.Right, SelectionHandle.BottomRight, SelectionHandle.Bottom,
        SelectionHandle.BottomLeft, SelectionHandle.Left,
    ];

    public static PxPoint GetHandleCenter(PxRect rect, SelectionHandle handle) => handle switch
    {
        SelectionHandle.TopLeft => new PxPoint(rect.Left, rect.Top),
        SelectionHandle.Top => new PxPoint(rect.Left + rect.Width / 2, rect.Top),
        SelectionHandle.TopRight => new PxPoint(rect.Right, rect.Top),
        SelectionHandle.Right => new PxPoint(rect.Right, rect.Top + rect.Height / 2),
        SelectionHandle.BottomRight => new PxPoint(rect.Right, rect.Bottom),
        SelectionHandle.Bottom => new PxPoint(rect.Left + rect.Width / 2, rect.Bottom),
        SelectionHandle.BottomLeft => new PxPoint(rect.Left, rect.Bottom),
        SelectionHandle.Left => new PxPoint(rect.Left, rect.Top + rect.Height / 2),
        _ => rect.Center,
    };

    /// <summary>
    /// 按手柄调整矩形：被拖动的边跟随 <paramref name="cursor"/>，对边保持不动。
    /// 拖过头时按最小尺寸截断，不会翻转出负宽度的矩形。
    /// </summary>
    public static PxRect Resize(PxRect origin, SelectionHandle handle, PxPoint cursor, int minimumSize = 1)
    {
        int left = origin.Left, top = origin.Top, right = origin.Right, bottom = origin.Bottom;

        bool movesLeft = handle is SelectionHandle.TopLeft or SelectionHandle.Left or SelectionHandle.BottomLeft;
        bool movesRight = handle is SelectionHandle.TopRight or SelectionHandle.Right or SelectionHandle.BottomRight;
        bool movesTop = handle is SelectionHandle.TopLeft or SelectionHandle.Top or SelectionHandle.TopRight;
        bool movesBottom = handle is SelectionHandle.BottomLeft or SelectionHandle.Bottom or SelectionHandle.BottomRight;

        if (movesLeft) left = Math.Min(cursor.X, right - minimumSize);
        if (movesRight) right = Math.Max(cursor.X, left + minimumSize);
        if (movesTop) top = Math.Min(cursor.Y, bottom - minimumSize);
        if (movesBottom) bottom = Math.Max(cursor.Y, top + minimumSize);

        return PxRect.FromLtrb(left, top, right, bottom);
    }

    /// <summary>把矩形限制在给定范围内，尺寸超界时收边、位置超界时贴边。</summary>
    public static PxRect ClampRect(PxRect rect, PxRect bounds)
    {
        int width = Math.Clamp(rect.Width, 0, bounds.Width);
        int height = Math.Clamp(rect.Height, 0, bounds.Height);

        int x = Math.Clamp(rect.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - width));
        int y = Math.Clamp(rect.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - height));

        return new PxRect(x, y, width, height);
    }

    /// <summary>移动选区但保持尺寸，贴边时停在边界上。</summary>
    public static PxRect MoveTo(PxRect origin, int dx, int dy, PxRect bounds)
    {
        int x = Math.Clamp(origin.X + dx, bounds.Left, Math.Max(bounds.Left, bounds.Right - origin.Width));
        int y = Math.Clamp(origin.Y + dy, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - origin.Height));
        return new PxRect(x, y, origin.Width, origin.Height);
    }
}
