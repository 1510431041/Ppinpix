using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ppinpix.Capture.Annotation;
using Ppinpix.Capture.Annotation.Shapes;
using Ppinpix.Capture.Interop;
using Ppinpix.Capture.Longshot;
using Ppinpix.Capture.Output;
using Ppinpix.Capture.Overlay;
using Ppinpix.Capture.Windowing;
using Ppinpix.Core;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Display;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Hotkeys;
using Ppinpix.Core.Input;
using Ppinpix.Core.Interop;
using Ppinpix.Core.Settings;
using Ppinpix.Core.Windowing;

namespace Ppinpix.Capture.Session;

/// <summary>截图完成后执行的动作。</summary>
public enum CaptureAction
{
    None = 0,
    Copy,
    Save,
    Pin,
    Ocr,
    Translate,
    Cancel,
}

public enum CaptureMode
{
    /// <summary>框选（默认）。</summary>
    Region = 0,

    /// <summary>直接全屏（虚拟桌面）。</summary>
    FullVirtualScreen = 1,
}

public sealed record CaptureRequest(CaptureMode Mode = CaptureMode.Region, CaptureAction? DirectAction = null);

/// <summary>一次截图会话的结果。</summary>
public sealed record CaptureResult(CaptureAction Action, ScreenFrame? Frame, PxRect Region);

/// <summary>
/// 一次截图会话：抓屏 → 多屏覆盖层 → 选区交互 → 输出。
///
/// 关键实现约定：
/// · 抓屏在覆盖层显示之前完成，覆盖层把冻结帧当作背景，所以抓到的永远是没有自己界面的干净画面；
/// · 所有坐标都是虚拟桌面物理像素，DIP 只在绘制时换算；
/// · 覆盖层可获取键盘焦点（保证 Esc / 方向键可用），工具栏不可激活（点按钮不抢焦点）。
/// </summary>
public sealed class CaptureSession : IOverlayInput, ICaptureToolbarActions, IDisposable
{
    private const int MinimumSelectionPx = 3;
    private const int KeyboardNudgeLargePx = 10;

    private readonly AppSettings _settings;
    private readonly DisplayService _displays;
    private readonly CaptureRequest _request;
    private readonly Action<CaptureResult> _onFinished;

    private readonly SelectionState _state = new();
    private readonly AnnotationEditor _annotations;
    private readonly List<OverlayWindow> _overlays = [];
    private readonly HotkeyManager _escapeHotkey = new();

    private ScreenFrame _frame = null!;
    private SnapEngine _snap = new([]);
    private MagnifierWindow? _magnifier;
    private CaptureToolbar? _toolbar;
    private ToolPropertiesBar? _propertiesBar;
    private LongshotControlWindow? _longshotWindow;
    private bool _longshotActive;

    private bool _annotationDragging;

    private PxPoint _cursor;
    private PxPoint _dragStartCursor;
    private PxPoint _createAnchor;
    private PxRect _dragOriginRect;
    private PxRect _pendingClickHighlight;
    private PxRect _virtualBounds;
    private nint _previousForeground;
    private bool _finished;
    private bool _disposed;

    private CaptureSession(AppSettings settings, DisplayService displays, CaptureRequest request, Action<CaptureResult> onFinished)
    {
        _settings = settings;
        _displays = displays;
        _request = request;
        _onFinished = onFinished;
        _annotations = new AnnotationEditor(settings.Annotation);
        _annotations.StateChanged += OnAnnotationStateChanged;
        _annotations.TextEditRequested += OnAnnotationTextEditRequested;
    }

    /// <summary>启动一次截图会话。必须在 UI 线程调用。</summary>
    public static CaptureSession Start(
        AppSettings settings,
        DisplayService displays,
        CaptureRequest request,
        Action<CaptureResult> onFinished)
    {
        var session = new CaptureSession(settings, displays, request, onFinished);
        try
        {
            session.Begin();
        }
        catch
        {
            // 抓屏或建窗中途失败：把已经显示出来的覆盖层收掉，不能留一块盖住桌面的窗口。
            session._finished = true;
            session.Dispose();
            throw;
        }

        return session;
    }

    internal SelectionState State => _state;

    internal static CaptureSession CreateForTest(
        AppSettings settings,
        DisplayService displays,
        ScreenFrame frame,
        SnapEngine snap,
        Action<CaptureResult> onFinished)
    {
        var session = new CaptureSession(settings, displays, new CaptureRequest(CaptureMode.Region), onFinished);
        session._virtualBounds = frame.Bounds;
        session._frame = frame;
        session._snap = snap;
        return session;
    }

    /// <summary>会话是否已经结束（完成或取消）。</summary>
    public bool IsFinished => _finished;

    private void Begin()
    {
        _previousForeground = WindowApi.GetForegroundWindow();
        _virtualBounds = _displays.VirtualBounds;

        // 1. 抓屏。必须在覆盖层出现之前。
        _frame = new GdiScreenCapture().Capture(_virtualBounds);
        if (_settings.Capture.IncludeCursor)
        {
            CursorCompositor.Composite(_frame);
        }

        // 2. 枚举吸附目标。同样必须在覆盖层出现之前。
        if (_settings.Capture.SnapEnabled)
        {
            var targets = new WindowEnumerator().Collect(
                _virtualBounds,
                excludeHandles: [nint.Zero],
                includeChildren: _settings.Capture.SnapToControls,
                includeUiaControls: _settings.Capture.SnapToControls,
                uiaCollector: _settings.Capture.SnapToControls
                    ? (vb, ex) => UiaControlCollector.CollectNearCursor(vb, ex)
                    : null);
            _snap = new SnapEngine(targets);

            Log.Info($"截图会话：抓到 {_frame.Width}×{_frame.Height} 像素，{targets.Count} 个吸附目标，{_displays.Monitors.Count} 块显示器。");
        }

        // 3. 覆盖层。每块显示器一个，各按本屏缩放。
        foreach (var monitor in _displays.Monitors)
        {
            var overlay = new OverlayWindow(monitor, _state, this, _annotations, _frame);
            overlay.SetFrame(FrameBitmap.GetMonitorView(_frame, monitor), monitor.Bounds);
            _overlays.Add(overlay);
        }

        if (_settings.Capture.ShowMagnifier)
        {
            _magnifier = new MagnifierWindow(_settings.Capture.MagnifierZoom)
            {
                ShowRgbFormat = _settings.Capture.DefaultColorFormatRgb,
            };
        }

        _toolbar = new CaptureToolbar(this, annotationOnly: false, _settings.Capture.Toolbar);
        if (Features.Annotate)
        {
            _propertiesBar = new ToolPropertiesBar(_annotations);
        }

        // 4. 先隐藏显示，再用 SetWindowPos 精确定位，避免在错误的显示器上闪一下。
        foreach (var overlay in _overlays)
        {
            overlay.Show();
            WindowChrome.MakeOverlay(overlay);
            WindowChrome.PlacePhysical(overlay, overlay.Monitor.Bounds);
        }

        GlobalPointer.TryGetPosition(out _cursor);
        var active = ActiveOverlay();
        active?.SetCursorPosition(_cursor);
        active?.TakeKeyboardFocus();

        _magnifier?.SetVisible(true);

        UpdateHoverHighlight();
        SyncAll();
        UpdateMagnifier();

        if (_request.Mode == CaptureMode.FullVirtualScreen)
        {
            _state.Rect = _virtualBounds;
            ShowToolbar();
        }

        // 5. Esc 兜底：即使焦点意外丢失也能取消。
        _escapeHotkey.Pressed += OnEscapeHotkey;
        var result = _escapeHotkey.Register("SessionCancel", new HotkeyGesture(HotkeyModifiers.None, 0x1B));
        if (!result.IsRegistered)
        {
            Log.Warn("注册会话级 Esc 失败，取消操作将只依赖键盘焦点。");
        }
    }

    private OverlayWindow? ActiveOverlay() =>
        _overlays.FirstOrDefault(o => o.Monitor.Bounds.Contains(_cursor)) ?? _overlays.FirstOrDefault();

    private void OnEscapeHotkey(string name)
    {
        if (_finished) return;

        if (_state.HasSelection)
        {
            ClearSelection();
        }
        else
        {
            Finish(CaptureAction.Cancel);
        }
    }

    // ------------------------------------------------------------------ 输入

    public void OnPointerMoved(PxPoint cursor)
    {
        if (_finished) return;
        _cursor = cursor;

        if (_annotationDragging)
        {
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            _annotations.OnPointerMove(cursor.X, cursor.Y, shift, HitRadiusPx());
            SyncAll();
            return;
        }

        switch (_state.Mode)
        {
            case DragMode.Creating:
                UpdateCreate(cursor);
                break;
            case DragMode.Moving:
                UpdateMove(cursor);
                break;
            case DragMode.Resizing:
                UpdateResize(cursor);
                break;
            default:
                UpdateHoverHighlight();
                break;
        }

        SyncAll();
        UpdateMagnifier();
    }

    public void OnPointerLeft()
    {
        // 光标只是移动到另一块屏幕的覆盖层上了，下一次 PointerMoved 会刷新全部覆盖层。
    }

    public void OnButtonDown(PxPoint cursor, bool isRight, bool isDoubleClick)
    {
        if (_finished) return;
        _cursor = cursor;

        bool wasEditingText = _overlays.Any(o => o.AnnotationLayer.IsEditingText);
        if (wasEditingText)
        {
            CommitAllTextEdits();
            if (!isRight) return;
        }

        if (isRight)
        {
            if (_annotations.ActiveTool != AnnotationTool.None)
            {
                _annotations.SetTool(AnnotationTool.None);
                SyncAll();
                return;
            }

            // 右键：有选区先清空，没有选区则退出。
            if (_state.HasSelection)
            {
                ClearSelection();
            }
            else
            {
                Finish(CaptureAction.Cancel);
            }
            return;
        }

        if (_state.HasSelection && _annotations.ActiveTool != AnnotationTool.None)
        {
            bool handleOwnedByAnnotation = _annotations.SelectedShape is { } sel &&
                AnnotationEditor.HitTestHandle(sel, cursor.X, cursor.Y, HitRadiusPx()) != ShapeHandleKind.None;

            if (handleOwnedByAnnotation || _state.HitTestHandle(cursor, HitRadiusPx()) == SelectionHandle.None)
            {
                if (handleOwnedByAnnotation || _state.Rect.Contains(cursor))
                {
                    _annotationDragging = _annotations.OnPointerDown(
                        cursor.X, cursor.Y, HitRadiusPx(), isDoubleClick ? 2 : 1);
                    SyncAll();
                    UpdateMagnifier();
                    return;
                }
            }
        }

        if (isDoubleClick)
        {
            if (_state.HasSelection && _state.Rect.Contains(cursor))
            {
                Accept();
                return;
            }

            // 空白处双击：直接采用当前识别到的窗口/控件，没有则整屏。
            var target = _snap.Probe(cursor, _state.Level);
            _state.Rect = target?.Bounds.ClampTo(_virtualBounds) ?? ActiveOverlay()?.Monitor.Bounds ?? _virtualBounds;
            _state.ClearHighlight();
            ShowToolbar();
            SyncAll();
            return;
        }

        _dragStartCursor = cursor;
        _dragOriginRect = _state.Rect;

        if (_state.HasSelection)
        {
            var handle = _state.HitTestHandle(cursor, HitRadiusPx());
            if (handle != SelectionHandle.None)
            {
                _state.Mode = DragMode.Resizing;
                _state.ActiveHandle = handle;
                return;
            }

            if (_state.Rect.Contains(cursor))
            {
                _state.Mode = DragMode.Moving;
                return;
            }
        }

        // 拉出新选区；如果只是轻点一下且未移动超过阈值，则在松开时确认当前高亮区域。
        bool hadSelection = _state.HasSelection;
        _pendingClickHighlight = hadSelection ? PxRect.Empty : _state.Highlight;
        _createAnchor = cursor;
        _state.Mode = DragMode.Creating;
        _state.ActiveHandle = SelectionHandle.None;
        _state.Level = 0;
        _state.Rect = new PxRect(cursor.X, cursor.Y, 0, 0);
        _state.ClearHighlight();
        HideToolbar();
        if (hadSelection) SyncAll();
    }

    public void OnButtonUp(PxPoint cursor)
    {
        if (_finished) return;
        _cursor = cursor;

        if (_annotationDragging)
        {
            _annotationDragging = false;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            _annotations.OnPointerUp(cursor.X, cursor.Y, shift);
            SyncAll();
            UpdateMagnifier();
            return;
        }

        var mode = _state.Mode;
        _state.Mode = DragMode.None;
        _state.ActiveHandle = SelectionHandle.None;

        if (mode == DragMode.Creating)
        {
            if (_state.Rect.Width < MinimumSelectionPx || _state.Rect.Height < MinimumSelectionPx)
            {
                if (!_pendingClickHighlight.IsEmpty)
                {
                    _state.Rect = _pendingClickHighlight;
                    _state.SnapTarget = null;
                    _pendingClickHighlight = PxRect.Empty;
                    ShowToolbar();
                }
                else
                {
                    // 只是点了一下，且没有高亮目标可确认。
                    _state.Rect = PxRect.Empty;
                    HideToolbar();
                    UpdateHoverHighlight();
                }
            }
            else
            {
                _pendingClickHighlight = PxRect.Empty;
                _state.SnapTarget = null;
                ShowToolbar();
            }
        }
        else if (mode is DragMode.Moving or DragMode.Resizing)
        {
            ShowToolbar();
        }

        SyncAll();
        UpdateMagnifier();
    }

    public void OnCaptureLost()
    {
        if (_finished) return;

        bool changed = false;
        if (_annotationDragging)
        {
            _annotationDragging = false;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            _annotations.OnPointerUp(_cursor.X, _cursor.Y, shift);
            changed = true;
        }

        var mode = _state.Mode;
        if (mode != DragMode.None)
        {
            _state.Mode = DragMode.None;
            _state.ActiveHandle = SelectionHandle.None;
            _pendingClickHighlight = PxRect.Empty;

            if (mode == DragMode.Creating &&
                (_state.Rect.Width < MinimumSelectionPx || _state.Rect.Height < MinimumSelectionPx))
            {
                _state.Rect = PxRect.Empty;
                HideToolbar();
                UpdateHoverHighlight();
            }
            else if (_state.HasSelection)
            {
                ShowToolbar();
            }

            changed = true;
        }

        if (changed)
        {
            SyncAll();
            UpdateMagnifier();
        }
    }

    public void OnWheel(PxPoint cursor, int delta)
    {
        if (_finished || _state.Mode != DragMode.None || _state.HasSelection) return;

        _cursor = cursor;

        int levels = _snap.LevelCount(cursor);
        if (levels <= 1) return;

        int next = _state.Level + (delta > 0 ? 1 : -1);
        _state.Level = Math.Clamp(next, 0, levels - 1);

        UpdateHoverHighlight();
        SyncAll();
    }

    public void OnKeyDown(Key key, ModifierKeys modifiers)
    {
        if (_finished || _longshotActive) return;

        bool ctrl = (modifiers & ModifierKeys.Control) != 0;
        bool shift = (modifiers & ModifierKeys.Shift) != 0;

        if (key == Key.F1 && shift && !ctrl)
        {
            ReCapture();
            return;
        }

        if (Features.Annotate && _state.HasSelection)
        {
            if (ctrl && key == Key.Z)
            {
                if (shift) Redo();
                else Undo();
                return;
            }
            if (ctrl && key == Key.Y)
            {
                Redo();
                return;
            }
            if (ctrl && key == Key.D)
            {
                _annotations.DuplicateSelected();
                SyncAll();
                return;
            }
            if (ctrl && shift && key == Key.T && Features.Translate)
            {
                Translate();
                return;
            }
        }

        switch (key)
        {
            case Key.Escape:
                if (_annotations.ActiveTool != AnnotationTool.None)
                {
                    _annotations.SetTool(AnnotationTool.None);
                    SyncAll();
                }
                else if (_state.HasSelection) ClearSelection();
                else Finish(CaptureAction.Cancel);
                return;

            case Key.Enter:
                if (_state.HasSelection) Accept();
                return;

            case Key.Delete or Key.Back:
                if (_annotations.DeleteSelected())
                {
                    SyncAll();
                }
                else if (_state.HasSelection && key == Key.Delete)
                {
                    ClearSelection();
                }
                return;

            case Key.C when ctrl:
                Copy();
                return;

            case Key.S when ctrl:
                Save();
                return;

            case Key.T when ctrl && !shift && Features.Pin:
                Pin();
                return;

            case Key.O when ctrl && Features.Ocr:
                Ocr();
                return;

            case Key.L when ctrl && _state.HasSelection:
                Longshot();
                return;

            case Key.A when ctrl:
                _state.Rect = _virtualBounds;
                _state.ClearHighlight();
                ShowToolbar();
                SyncAll();
                return;

            case Key.C when !ctrl:
                CopyCurrentColor();
                return;

            case Key.LeftShift or Key.RightShift:
                // Shift 在已有选区时是「方向键改尺寸」的修饰键，只有没选区时才用来切换取色格式。
                if (!_state.HasSelection) ToggleColorFormat();
                return;

            case Key.Left or Key.Right or Key.Up or Key.Down:
                Nudge(key, ctrl ? KeyboardNudgeLargePx : 1, shift);
                return;
        }

        if (Features.Annotate && _state.HasSelection && !ctrl)
        {
            switch (key)
            {
                case Key.V: SelectTool(AnnotationTool.Select); return;
                case Key.R: SelectTool(AnnotationTool.Rectangle); return;
                case Key.O: SelectTool(AnnotationTool.Ellipse); return;
                case Key.A: SelectTool(AnnotationTool.Arrow); return;
                case Key.L: SelectTool(AnnotationTool.Line); return;
                case Key.P: SelectTool(AnnotationTool.Freehand); return;
                case Key.N: SelectTool(AnnotationTool.NumberBadge); return;
                case Key.T: SelectTool(AnnotationTool.Text); return;
                case Key.M:
                    _annotations.SetTool(_annotations.ActiveTool == AnnotationTool.Mosaic
                        ? AnnotationTool.Blur
                        : _annotations.ActiveTool == AnnotationTool.Blur
                            ? AnnotationTool.None
                            : _annotations.LastMaskTool);
                    SyncAll();
                    return;
                case Key.K: SelectTool(AnnotationTool.Spotlight); return;
                case Key.H: SelectTool(AnnotationTool.Highlight); return;
                case Key.E: SelectTool(AnnotationTool.Eraser); return;
                case >= Key.D1 and <= Key.D8:
                    _annotations.SetColorIndex(key - Key.D1);
                    SyncAll();
                    return;
                case >= Key.NumPad1 and <= Key.NumPad8:
                    _annotations.SetColorIndex(key - Key.NumPad1);
                    SyncAll();
                    return;
                case Key.OemOpenBrackets:
                    _annotations.AdjustPrimaryMetric(-1);
                    SyncAll();
                    return;
                case Key.OemCloseBrackets:
                    _annotations.AdjustPrimaryMetric(+1);
                    SyncAll();
                    return;
            }
        }
    }

    // ------------------------------------------------------------------ 选区更新

    private void UpdateCreate(PxPoint cursor)
    {
        var raw = PxRect.FromCorners(_createAnchor, cursor);
        if (raw.Width >= MinimumSelectionPx && raw.Height >= MinimumSelectionPx)
        {
            _pendingClickHighlight = PxRect.Empty;
        }

        var snapped = _snap.SnapMovingCorner(raw, cursor, SnapThresholdPx(), SnappingAllowed());

        _state.Rect = SelectionState.ClampRect(snapped.Rect, _virtualBounds);
        _state.SnapTarget = snapped.Target;
    }

    private void UpdateMove(PxPoint cursor)
    {
        int dx = cursor.X - _dragStartCursor.X;
        int dy = cursor.Y - _dragStartCursor.Y;

        _state.Rect = SelectionState.MoveTo(_dragOriginRect, dx, dy, _virtualBounds);
        _state.SnapTarget = null;

        // 移动后再做一次边缘吸附，让整块选区贴到窗口边界上。
        if (SnappingAllowed())
        {
            var snapped = _snap.SnapRect(_state.Rect, SnapThresholdPx());
            if (snapped.Snapped)
            {
                _state.Rect = SelectionState.ClampRect(snapped.Rect, _virtualBounds);
                _state.SnapTarget = snapped.Target;
            }
        }
    }

    private void UpdateResize(PxPoint cursor)
    {
        var resized = SelectionState.Resize(_dragOriginRect, _state.ActiveHandle, cursor, MinimumSelectionPx);

        if (SnappingAllowed())
        {
            var snapped = _snap.SnapRect(resized, SnapThresholdPx());
            if (snapped.Snapped) resized = snapped.Rect;
        }

        _state.Rect = SelectionState.ClampRect(resized, _virtualBounds);
    }

    private void UpdateHoverHighlight()
    {
        if (_state.HasSelection || _state.Mode != DragMode.None)
        {
            _state.ClearHighlight();
            return;
        }

        var target = _snap.Probe(_cursor, _state.Level);
        if (target is null)
        {
            _state.ClearHighlight();
            return;
        }

        _state.Highlight = target.Bounds.Intersect(_virtualBounds);
        _state.HighlightTarget = target;
    }

    private void Nudge(Key key, int step, bool resize)
    {
        if (!_state.HasSelection) return;

        int dx = key == Key.Left ? -step : key == Key.Right ? step : 0;
        int dy = key == Key.Up ? -step : key == Key.Down ? step : 0;

        if (resize)
        {
            // Shift + 方向键：改变尺寸，左上角固定。
            int width = Math.Max(MinimumSelectionPx, _state.Rect.Width + dx);
            int height = Math.Max(MinimumSelectionPx, _state.Rect.Height + dy);
            _state.Rect = SelectionState.ClampRect(new PxRect(_state.Rect.X, _state.Rect.Y, width, height), _virtualBounds);
        }
        else
        {
            _state.Rect = SelectionState.MoveTo(_state.Rect, dx, dy, _virtualBounds);
        }

        ShowToolbar();
        SyncAll();
    }

    private void ClearSelection()
    {
        _pendingClickHighlight = PxRect.Empty;
        _state.Clear();
        HideToolbar();
        UpdateHoverHighlight();
        SyncAll();
    }

    private void SyncAll()
    {
        foreach (var overlay in _overlays)
        {
            overlay.Sync();
        }
    }

    private void UpdateMagnifier()
    {
        if (_magnifier is null) return;

        bool visible = _settings.Capture.ShowMagnifier
            && _annotations.ActiveTool == AnnotationTool.None
            && !(_settings.Capture.HideMagnifierWhileDragging && _state.Mode is DragMode.Moving or DragMode.Resizing);

        _magnifier.SetVisible(visible);
        if (!visible) return;

        var monitor = _displays.FromPoint(_cursor);
        _magnifier.Update(_frame, _cursor, monitor.Bounds);
    }

    private void ToggleColorFormat()
    {
        if (_magnifier is null) return;
        _magnifier.ShowRgbFormat = !_magnifier.ShowRgbFormat;
        UpdateMagnifier();
    }

    private void CopyCurrentColor()
    {
        if (_magnifier is null) return;

        var (r, g, b) = _magnifier.CurrentColor;
        string text = _magnifier.ShowRgbFormat ? $"rgb({r},{g},{b})" : $"#{r:X2}{g:X2}{b:X2}";

        try
        {
            Clipboard.SetText(text);
            ToastWindow.Show($"已复制颜色 {text}", ToastKind.Success, _displays);
        }
        catch (Exception ex)
        {
            Log.Warn("复制颜色失败。", ex);
        }
    }

    private int HitRadiusPx()
    {
        var overlay = ActiveOverlay();
        return overlay?.HitRadiusPx() ?? 7;
    }

    private int SnapThresholdPx()
    {
        if (!_settings.Capture.SnapEnabled) return 0;
        double scale = _displays.FromPoint(_cursor).Scale;
        return (int)Math.Round(_settings.Capture.SnapThresholdDip * (scale <= 0 ? 1 : scale));
    }

    /// <summary>按住 Alt 临时关闭吸附。</summary>
    private static bool SnappingAllowed()
    {
        try
        {
            return (Keyboard.Modifiers & ModifierKeys.Alt) == 0;
        }
        catch
        {
            return true;
        }
    }

    // ------------------------------------------------------------------ 工具栏动作

    private void OnAnnotationStateChanged()
    {
        _toolbar?.SetToolState(_annotations.ActiveTool, _annotations.CanUndo, _annotations.CanRedo);
        if (_state.HasSelection && _toolbar is not null)
        {
            var monitor = _displays.FromPoint(_state.Rect.Center);
            _propertiesBar?.PositionNear(_toolbar.PlacedBounds, _state.Rect, monitor);
        }
        UpdateMagnifier();
        SyncAll();
    }

    private void OnAnnotationTextEditRequested(TextEditRequest req)
    {
        var pt = new PxPoint((int)Math.Round(req.Shape.X), (int)Math.Round(req.Shape.Y));
        var target = _overlays.FirstOrDefault(o => o.Monitor.Bounds.Contains(pt))
            ?? ActiveOverlay()
            ?? _overlays.FirstOrDefault();
        target?.AnnotationLayer.OpenTextEditor(req);
    }

    private void CommitAllTextEdits()
    {
        foreach (var overlay in _overlays)
        {
            overlay.AnnotationLayer.CommitActiveTextEdit();
        }
    }

    public void SelectTool(AnnotationTool tool)
    {
        CommitAllTextEdits();
        if (tool == AnnotationTool.Mosaic)
        {
            bool activeMask = _annotations.ActiveTool is AnnotationTool.Mosaic or AnnotationTool.Blur;
            _annotations.SetTool(activeMask ? AnnotationTool.None : _annotations.LastMaskTool);
        }
        else
        {
            _annotations.ToggleTool(tool);
        }
        SyncAll();
    }

    public void Undo()
    {
        CommitAllTextEdits();
        _annotations.Undo();
        SyncAll();
    }

    public void Redo()
    {
        CommitAllTextEdits();
        _annotations.Redo();
        SyncAll();
    }

    private void ShowToolbar()
    {
        if (_toolbar is null || !_state.HasSelection) return;

        var monitor = _displays.FromPoint(_state.Rect.Center);
        _toolbar.ShowToolbar();
        _toolbar.Position(_state.Rect, monitor);
        _propertiesBar?.PositionNear(_toolbar.PlacedBounds, _state.Rect, monitor);
    }

    private void HideToolbar()
    {
        _toolbar?.HideToolbar();
        _propertiesBar?.Hide();
    }

    private ScreenFrame RenderComposite(PxRect region)
    {
        CommitAllTextEdits();
        return _annotations.RenderToFrame(_frame, region);
    }

    public void Copy()
    {
        if (!_state.HasSelection) return;

        var region = _state.Rect;
        var output = RenderComposite(region);
        if (CaptureOutput.CopyToClipboard(output))
        {
            ToastWindow.Show($"已复制 {region.Width} × {region.Height} 到剪贴板", ToastKind.Success, _displays);
            if (_settings.Capture.CloseOnCopy)
            {
                FinishWithRendered(CaptureAction.Copy, output, region);
            }
        }
        else
        {
            ToastWindow.Show("复制失败：剪贴板被其他程序占用。", ToastKind.Error, _displays);
        }
    }

    public void Save()
    {
        if (!_state.HasSelection) return;

        try
        {
            var output = RenderComposite(_state.Rect);
            string path = CaptureOutput.Save(output, _settings.Save);
            ToastWindow.Show($"已保存到 {path}", ToastKind.Success, _displays);

            if (_settings.Save.CopyAfterSave) CaptureOutput.CopyToClipboard(output);
        }
        catch (Exception ex)
        {
            Log.Error("保存截图失败。", ex);
            ToastWindow.Show($"保存失败：{ex.Message}", ToastKind.Error, _displays);
        }
    }

    public void Longshot()
    {
        if (_finished || _longshotActive || !_state.HasSelection) return;

        CommitAllTextEdits();
        var region = _state.Rect;
        var initialCrop = _frame.Crop(region);
        if (initialCrop.Bounds.IsEmpty) return;

        _longshotActive = true;
        _escapeHotkey.UnregisterAll();

        HideToolbar();
        _magnifier?.SetVisible(false);
        foreach (var overlay in _overlays)
        {
            overlay.Hide();
        }

        if (_previousForeground != 0 && WindowApi.IsWindow(_previousForeground))
        {
            WindowApi.SetForegroundWindow(_previousForeground);
        }

        _longshotWindow = new LongshotControlWindow(
            region,
            initialCrop,
            _displays,
            OnLongshotCompleted);
        _longshotWindow.Start();
    }

    private void OnLongshotCompleted(CaptureAction action, ScreenFrame? stitchedFrame)
    {
        _longshotWindow = null;
        _longshotActive = false;

        if (_finished) return;

        if (action == CaptureAction.Cancel || stitchedFrame is null)
        {
            FinishWithRendered(CaptureAction.Cancel, null, PxRect.Empty);
            return;
        }

        var bounds = stitchedFrame.Bounds;
        if (action == CaptureAction.Copy)
        {
            if (CaptureOutput.CopyToClipboard(stitchedFrame))
            {
                ToastWindow.Show($"已复制长截图 {bounds.Width} × {bounds.Height} 到剪贴板", ToastKind.Success, _displays);
            }
            else
            {
                ToastWindow.Show("复制失败：剪贴板被其他程序占用。", ToastKind.Error, _displays);
            }
            FinishWithRendered(CaptureAction.Copy, stitchedFrame, bounds);
            return;
        }

        if (action == CaptureAction.Save)
        {
            try
            {
                string path = CaptureOutput.Save(stitchedFrame, _settings.Save);
                ToastWindow.Show($"已保存长截图到 {path}", ToastKind.Success, _displays);
                if (_settings.Save.CopyAfterSave) CaptureOutput.CopyToClipboard(stitchedFrame);
                FinishWithRendered(CaptureAction.Save, stitchedFrame, bounds);
            }
            catch (Exception ex)
            {
                Log.Error("保存长截图失败。", ex);
                ToastWindow.Show($"保存失败：{ex.Message}", ToastKind.Error, _displays);
                FinishWithRendered(CaptureAction.Cancel, null, PxRect.Empty);
            }
            return;
        }

        FinishWithRendered(action, stitchedFrame, bounds);
    }

    private void ReCapture()
    {
        if (_finished || _longshotActive) return;

        CommitAllTextEdits();
        _magnifier?.SetVisible(false);

        var dispatcher = _overlays.FirstOrDefault()?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            if (_finished || _longshotActive) return;

            var newFrame = new GdiScreenCapture().Capture(_virtualBounds);
            if (_settings.Capture.IncludeCursor)
            {
                CursorCompositor.Composite(newFrame);
            }

            _frame = newFrame;
            _annotations.Clear();
            _pendingClickHighlight = PxRect.Empty;
            _state.Clear();
            HideToolbar();

            foreach (var overlay in _overlays)
            {
                overlay.UpdateFrozenFrame(_frame);
            }

            UpdateHoverHighlight();
            SyncAll();
            UpdateMagnifier();
            ToastWindow.Show("已将当前画面定格为新底图，可重新框选或嵌套标注", ToastKind.Info, _displays);
        });
    }

    public void Pin()
    {
        if (!_state.HasSelection) return;
        Finish(CaptureAction.Pin);
    }

    public void Ocr()
    {
        if (!_state.HasSelection) return;
        Finish(CaptureAction.Ocr);
    }

    public void Translate()
    {
        if (!_state.HasSelection) return;
        Finish(CaptureAction.Translate);
    }

    public void Cancel() => Finish(CaptureAction.Cancel);

    public void Accept()
    {
        if (!_state.HasSelection) return;

        if (_request.DirectAction is { } direct && direct is not CaptureAction.None and not CaptureAction.Cancel)
        {
            Finish(direct);
            return;
        }

        var region = _state.Rect;
        var output = RenderComposite(region);
        if (CaptureOutput.CopyToClipboard(output))
        {
            ToastWindow.Show($"已复制 {region.Width} × {region.Height} 到剪贴板", ToastKind.Success, _displays);
        }
        FinishWithRendered(CaptureAction.Copy, output, region);
    }

    // ------------------------------------------------------------------ 收尾

    private void Finish(CaptureAction action)
    {
        if (action == CaptureAction.Cancel || !_state.HasSelection)
        {
            FinishWithRendered(CaptureAction.Cancel, null, PxRect.Empty);
            return;
        }

        var region = _state.Rect;
        var rendered = RenderComposite(region);
        FinishWithRendered(action, rendered, region);
    }

    private void FinishWithRendered(CaptureAction action, ScreenFrame? rendered, PxRect region)
    {
        if (_finished) return;
        _finished = true;

        var result = action == CaptureAction.Cancel || rendered is null
            ? new CaptureResult(CaptureAction.Cancel, null, PxRect.Empty)
            : new CaptureResult(action, rendered, region);

        Dispose();

        try
        {
            _onFinished(result);
        }
        catch (Exception ex)
        {
            Log.Error("处理截图结果时出错。", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _escapeHotkey.Pressed -= OnEscapeHotkey;
        _escapeHotkey.Dispose();

        _annotations.StateChanged -= OnAnnotationStateChanged;
        _annotations.TextEditRequested -= OnAnnotationTextEditRequested;
        _annotations.Dispose();

        _magnifier?.Close();
        _magnifier = null;

        _longshotWindow?.Close();
        _longshotWindow = null;

        _propertiesBar?.Close();
        _propertiesBar = null;

        _toolbar?.Close();
        _toolbar = null;

        foreach (var overlay in _overlays)
        {
            overlay.Close();
        }
        _overlays.Clear();

        // 把焦点还给截图前的前台窗口，避免用户感觉“焦点丢了”。
        if (_previousForeground != 0 && WindowApi.IsWindow(_previousForeground))
        {
            WindowApi.SetForegroundWindow(_previousForeground);
        }

        GC.SuppressFinalize(this);
    }
}
