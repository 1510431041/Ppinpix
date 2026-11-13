using System.Windows;
using System.Windows.Media;
using Ppinpix.Core.Display;
using Ppinpix.Core.Geometry;
using Ppinpix.Theme;

namespace Ppinpix.Capture.Overlay;

/// <summary>
/// 覆盖层的绘制面：选区外遮罩 + 识别高亮 + 选区边框。
///
/// 冻结帧由下层的静态 Image 承载，十字参考线由独立的 <see cref="CrosshairOverlay"/> 承载，
/// 避免光标每移动 1 像素都重绘整张 4K 桌面位图或重建几何体。
/// </summary>
public sealed class SelectionSurface : FrameworkElement
{
    private readonly MonitorInfo _monitor;

    private static readonly Brush DimBrush = Freeze(new SolidColorBrush(Tokens.Dim));
    private static readonly Brush HighlightFillBrush = Freeze(new SolidColorBrush(Tokens.AccentSoft));
    private static readonly Brush AccentBrush = Freeze(new SolidColorBrush(Tokens.Accent));

    private PxRect _frameBounds;
    private PxRect _selection;
    private PxRect _highlight;
    private double _cachedHairline = -1;
    private Pen? _cachedHighlightPen;
    private Pen? _cachedSelectionPen;

    public SelectionSurface(MonitorInfo monitor)
    {
        _monitor = monitor;
        IsHitTestVisible = false;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    /// <summary>本屏在虚拟桌面中的物理像素范围。</summary>
    public void SetFrame(ImageSource? _, PxRect frameBounds) => SetFrameBounds(frameBounds);

    /// <summary>本屏在虚拟桌面中的物理像素范围。</summary>
    public void SetFrameBounds(PxRect frameBounds)
    {
        _frameBounds = frameBounds;
    }

    /// <summary>当前选区（绝对物理像素）。</summary>
    public void SetSelection(PxRect selection)
    {
        if (_selection == selection) return;
        _selection = selection;
        InvalidateVisual();
    }

    /// <summary>悬停时高亮的窗口/控件矩形（绝对物理像素）。</summary>
    public void SetHighlight(PxRect highlight)
    {
        if (_highlight == highlight) return;
        _highlight = highlight;
        InvalidateVisual();
    }

    /// <summary>物理像素 → 本元素 DIP 的换算比例；以实际布局尺寸为准，避免依赖 WPF 的 DPI 判断。</summary>
    public double PxScale
    {
        get
        {
            if (_frameBounds.IsEmpty || RenderSize.Width <= 0) return 1.0;
            return RenderSize.Width / _frameBounds.Width;
        }
    }

    public Rect ToDip(PxRect px) => new(
        (px.X - _frameBounds.X) * PxScale,
        (px.Y - _frameBounds.Y) * PxScale,
        px.Width * PxScale,
        px.Height * PxScale);

    /// <summary>本屏内的裁剪矩形（用于把跨屏选区裁到本屏范围内）。</summary>
    public PxRect ClipToMonitor(PxRect px) => px.Intersect(_frameBounds);

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0) return;

        var full = new Rect(0, 0, size.Width, size.Height);
        double scale = PxScale;
        double hairline = 1.0 / Math.Max(scale, 0.0001); // 1 物理像素在线性尺寸下的厚度
        EnsurePens(hairline);

        // 1. 遮罩：选区内保持原始亮度
        var selectionLocal = ClipToMonitor(_selection);
        if (selectionLocal.IsEmpty)
        {
            dc.DrawRectangle(DimBrush, null, full);
        }
        else
        {
            var hole = ToDip(selectionLocal);
            var geometry = new CombinedGeometry(
                GeometryCombineMode.Exclude,
                new RectangleGeometry(full),
                new RectangleGeometry(hole));
            dc.DrawGeometry(DimBrush, null, geometry);
        }

        // 2. 未选择时的识别高亮
        if (selectionLocal.IsEmpty && !_highlight.IsEmpty)
        {
            var highlightLocal = ClipToMonitor(_highlight);
            if (!highlightLocal.IsEmpty)
            {
                var rect = ToDip(highlightLocal);
                dc.DrawRectangle(HighlightFillBrush, null, rect);
                dc.DrawRectangle(null, _cachedHighlightPen, Inset(rect, hairline));
            }
        }

        // 3. 选区边框：画在选区最外一圈像素上，永远不被屏幕边缘裁掉
        if (!selectionLocal.IsEmpty)
        {
            var rect = ToDip(selectionLocal);
            dc.DrawRectangle(null, _cachedSelectionPen, Inset(rect, hairline));
        }
    }

    private void EnsurePens(double hairline)
    {
        if (Math.Abs(_cachedHairline - hairline) < 1e-6 && _cachedHighlightPen is not null && _cachedSelectionPen is not null)
        {
            return;
        }

        _cachedHairline = hairline;
        _cachedHighlightPen = FreezePen(AccentBrush, hairline * 2);
        _cachedSelectionPen = FreezePen(AccentBrush, hairline);
    }

    /// <summary>把矩形向内缩半个线宽，让描边正好落在最外一圈物理像素上。</summary>
    private static Rect Inset(Rect rect, double amount)
    {
        double half = amount / 2;
        return new Rect(rect.X + half, rect.Y + half, Math.Max(0, rect.Width - amount), Math.Max(0, rect.Height - amount));
    }

    private static Pen FreezePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// 独立十字参考线层：仅绘制两条 1 物理像素直线，避免移动鼠标时触发遮罩与 4K 底图重绘。
/// </summary>
internal sealed class CrosshairOverlay : FrameworkElement
{
    private static readonly Brush CrosshairBrush = Freeze(new SolidColorBrush(Tokens.WithAlpha(Colors.White, 46)));

    private PxRect _frameBounds;
    private PxPoint? _cursor;
    private bool _suppressed;
    private double _cachedHairline = -1;
    private Pen? _cachedPen;

    public CrosshairOverlay()
    {
        IsHitTestVisible = false;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    public void Update(PxRect frameBounds, PxPoint? cursor, bool hasSelection)
    {
        if (_frameBounds == frameBounds && _cursor == cursor && _suppressed == hasSelection)
        {
            return;
        }

        bool wasVisible = !_suppressed && _cursor is { } oldCur && _frameBounds.Contains(oldCur);
        bool nowVisible = !hasSelection && cursor is { } newCur && frameBounds.Contains(newCur);

        _frameBounds = frameBounds;
        _cursor = cursor;
        _suppressed = hasSelection;

        if (wasVisible || nowVisible)
        {
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_suppressed || _cursor is not { } cursor || !_frameBounds.Contains(cursor)) return;

        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0 || _frameBounds.IsEmpty) return;

        double scale = size.Width / _frameBounds.Width;
        double hairline = 1.0 / Math.Max(scale, 0.0001);
        if (Math.Abs(_cachedHairline - hairline) >= 1e-6 || _cachedPen is null)
        {
            _cachedHairline = hairline;
            var pen = new Pen(CrosshairBrush, hairline);
            pen.Freeze();
            _cachedPen = pen;
        }

        double cx = Math.Round((cursor.X - _frameBounds.X) * scale) + 0.5 * hairline;
        double cy = Math.Round((cursor.Y - _frameBounds.Y) * scale) + 0.5 * hairline;
        dc.DrawLine(_cachedPen, new Point(cx, 0), new Point(cx, size.Height));
        dc.DrawLine(_cachedPen, new Point(0, cy), new Point(size.Width, cy));
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
