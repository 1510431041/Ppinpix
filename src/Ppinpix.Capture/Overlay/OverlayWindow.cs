using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Ppinpix.Capture.Annotation;
using Ppinpix.Capture.Annotation.Shapes;
using Ppinpix.Capture.Interop;
using Ppinpix.Capture.Session;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Display;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Input;
using Ppinpix.Core.Interop;
using Ppinpix.Theme;

namespace Ppinpix.Capture.Overlay;

/// <summary>覆盖层需要向外汇报的输入事件。</summary>
internal interface IOverlayInput
{
    void OnPointerMoved(PxPoint cursor);

    void OnPointerLeft();

    void OnButtonDown(PxPoint cursor, bool isRight, bool isDoubleClick);

    void OnButtonUp(PxPoint cursor);

    void OnWheel(PxPoint cursor, int delta);

    void OnKeyDown(Key key, ModifierKeys modifiers);

    void OnCaptureLost();
}

/// <summary>
/// 每块显示器一个的截图覆盖层。
/// 窗口整体不透明，内容就是冻结帧本身，所以视觉上与桌面完全一致，但获得完整的硬件加速
/// （不需要 AllowsTransparency 那条软件合成路径）。
/// </summary>
internal sealed class OverlayWindow : Window
{
    private static readonly Brush HandleFill = Brushes.White;

    private readonly MonitorInfo _monitor;
    private readonly SelectionState _state;
    private readonly AnnotationEditor _editor;

    private readonly System.Windows.Controls.Image _frameImage;
    private readonly SelectionSurface _surface;
    private readonly CrosshairOverlay _crosshair;
    private readonly AnnotationLayer _annotationLayer;
    private readonly Canvas _layer;
    private readonly Border _badge;
    private readonly TextBlock _badgeText;
    private readonly Ellipse[] _handles;

    private PxRect _frameBounds;
    private PxPoint? _lastCursor;

    public OverlayWindow(
        MonitorInfo monitor,
        SelectionState state,
        IOverlayInput input,
        AnnotationEditor editor,
        ScreenFrame sourceFrame)
    {
        _monitor = monitor;
        _state = state;
        _editor = editor;
        Input = input;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = false;
        Background = Brushes.Black;
        Title = $"Ppinpix 截图覆盖层 {monitor.DeviceName}";

        double scale = monitor.Scale > 0 ? monitor.Scale : 1.0;
        Left = monitor.Bounds.X / scale;
        Top = monitor.Bounds.Y / scale;
        Width = monitor.Bounds.Width / scale;
        Height = monitor.Bounds.Height / scale;

        _frameImage = new System.Windows.Controls.Image
        {
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
        };
        RenderOptions.SetBitmapScalingMode(_frameImage, BitmapScalingMode.NearestNeighbor);

        _surface = new SelectionSurface(monitor);
        _crosshair = new CrosshairOverlay();

        _badgeText = new TextBlock { Style = ThemeLoader.Find<Style>("Pp.MonoText"), Text = "" };
        _badge = new Border
        {
            Background = ThemeLoader.Brush("Pp.Surface"),
            BorderBrush = ThemeLoader.Brush("Pp.Stroke"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Tokens.CornerBadge),
            Padding = new Thickness(Tokens.Spacing2, 2, Tokens.Spacing2, 2),
            Child = _badgeText,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };

        _layer = new Canvas { IsHitTestVisible = false };
        _layer.Children.Add(_badge);

        // 8 个调整手柄：抗锯齿的圆点，画在 Canvas 上而不是绘制面里。
        var accent = ThemeLoader.Brush("Pp.Accent");
        _handles = new Ellipse[SelectionState.AllHandles.Length];
        for (int i = 0; i < _handles.Length; i++)
        {
            _handles[i] = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = HandleFill,
                Stroke = accent,
                StrokeThickness = 1.5,
                Visibility = Visibility.Collapsed,
            };
            _layer.Children.Add(_handles[i]);
        }

        // 没有背景的 Grid 不参与命中测试，必须给个透明背景才能接到鼠标事件。
        _annotationLayer = new AnnotationLayer(_editor, sourceFrame, monitor.Bounds, monitor.Scale)
        {
            IsHitTestVisible = true,
        };

        var root = new Grid { Background = Brushes.Transparent };
        root.Children.Add(_frameImage);
        root.Children.Add(_surface);
        root.Children.Add(_crosshair);
        root.Children.Add(_annotationLayer);
        root.Children.Add(_layer);
        Content = root;

        // 鼠标事件直接挂在 Window 上，与 CaptureMouse() / ReleaseMouseCapture() 的元素捕获作用域一致，
        // 避免 CaptureMouse() 后 WPF 把事件只投递给 Window 而绕过子级 root。
        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
        MouseDown += OnMouseDown;
        MouseUp += OnMouseUp;
        MouseWheel += OnMouseWheel;
        LostMouseCapture += OnLostMouseCapture;
        PreviewKeyDown += OnPreviewKeyDown;

        Loaded += OnLoaded;
    }

    /// <summary>本窗口覆盖的显示器。</summary>
    public MonitorInfo Monitor => _monitor;

    /// <summary>归属的输入接收者（CaptureSession）。</summary>
    public IOverlayInput Input { get; }

    /// <summary>本屏的矢量/马赛克标注层。</summary>
    public AnnotationLayer AnnotationLayer => _annotationLayer;

    /// <summary>设置本屏的冻结帧，并在首次加载后同步一次布局。</summary>
    public void SetFrame(ImageSource view, PxRect frameBounds)
    {
        _frameBounds = frameBounds;
        _frameImage.Source = view;
        _surface.SetFrameBounds(frameBounds);
        SyncLayout();
    }

    public void SetCursorPosition(PxPoint cursor) => _lastCursor = cursor;

    private void OnLoaded(object sender, RoutedEventArgs e) => SyncLayout();

    /// <summary>布局尺寸在 DPI 变化等情况下可能变，重新同步一次。</summary>
    private void SyncLayout()
    {
        _surface.InvalidateVisual();
        Sync();
    }

    protected override void OnDpiChanged(System.Windows.DpiScale oldDpi, System.Windows.DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        SyncLayout();
    }

    /// <summary>把当前选区状态画到界面上。由 CaptureSession 在状态变化后调用。</summary>
    public void Sync()
    {
        _surface.SetSelection(_state.Rect);
        _surface.SetHighlight(_state.Highlight);
        _crosshair.Update(_frameBounds, _lastCursor, _state.HasSelection);
        _annotationLayer.SetClipRect(_state.Rect);

        UpdateHandles();
        UpdateBadge();
        UpdateCursorShape();
    }

    private void UpdateHandles()
    {
        double scale = _surface.PxScale;
        bool visible = _state.HasSelection;

        for (int i = 0; i < _handles.Length; i++)
        {
            var ellipse = _handles[i];
            if (!visible)
            {
                ellipse.Visibility = Visibility.Collapsed;
                continue;
            }

            var center = SelectionState.GetHandleCenter(_state.Rect, SelectionState.AllHandles[i]);
            if (!_frameBounds.Contains(center))
            {
                // 跨屏选区：手柄只画在光标所在的那块屏上。
                ellipse.Visibility = Visibility.Collapsed;
                continue;
            }

            double x = (center.X - _frameBounds.X) * scale;
            double y = (center.Y - _frameBounds.Y) * scale;

            ellipse.Visibility = Visibility.Visible;
            Canvas.SetLeft(ellipse, Math.Round(x - ellipse.Width / 2));
            Canvas.SetTop(ellipse, Math.Round(y - ellipse.Height / 2));
        }
    }

    private void UpdateBadge()
    {
        if (!_state.HasSelection && _state.Highlight.IsEmpty)
        {
            _badge.Visibility = Visibility.Collapsed;
            return;
        }

        var rect = _state.HasSelection ? _state.Rect : _state.Highlight;
        var local = _surface.ClipToMonitor(rect);
        if (local.IsEmpty)
        {
            _badge.Visibility = Visibility.Collapsed;
            return;
        }

        _badgeText.Text = $"{rect.Width} × {rect.Height}";
        _badge.Visibility = Visibility.Visible;

        _badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double badgeWidth = _badge.DesiredSize.Width;
        double badgeHeight = _badge.DesiredSize.Height;

        double scale = _surface.PxScale;
        double left = (local.X - _frameBounds.X) * scale;
        double top = (local.Y - _frameBounds.Y) * scale - badgeHeight - Tokens.Spacing1;

        // 上方放不下就挪到选区内部上沿。
        if (top < 0) top = (local.Y - _frameBounds.Y) * scale + Tokens.Spacing1;

        double maxLeft = Math.Max(0, RenderSize.Width - badgeWidth);
        Canvas.SetLeft(_badge, Math.Clamp(left, 0, maxLeft));
        Canvas.SetTop(_badge, Math.Clamp(top, 0, Math.Max(0, RenderSize.Height - badgeHeight)));
    }

    private void UpdateCursorShape()
    {
        if (_state.Mode == DragMode.Moving)
        {
            Cursor = Cursors.SizeAll;
            return;
        }

        if (_state.Mode == DragMode.Resizing)
        {
            Cursor = CursorForHandle(_state.ActiveHandle);
            return;
        }

        if (_state.Mode == DragMode.Creating)
        {
            Cursor = Cursors.Cross;
            return;
        }

        var cursor = _lastCursor;
        if (cursor is not null && _state.HasSelection)
        {
            if (_editor.SelectedShape is { } sel)
            {
                var sh = AnnotationEditor.HitTestHandle(sel, cursor.Value.X, cursor.Value.Y, HitRadiusPx());
                if (sh != ShapeHandleKind.None)
                {
                    Cursor = CursorForShapeHandle(sh);
                    return;
                }
            }

            if (_editor.ActiveTool != AnnotationTool.None && _state.Rect.Contains(cursor.Value))
            {
                Cursor = _editor.ActiveTool switch
                {
                    AnnotationTool.Text => Cursors.IBeam,
                    AnnotationTool.Select => _editor.HitTestShape(cursor.Value.X, cursor.Value.Y, HitRadiusPx()) is not null
                        ? Cursors.SizeAll
                        : Cursors.Arrow,
                    AnnotationTool.Eraser => Cursors.Hand,
                    _ => Cursors.Cross,
                };
                return;
            }

            var handle = _state.HitTestHandle(cursor.Value, HitRadiusPx());
            if (handle != SelectionHandle.None)
            {
                Cursor = CursorForHandle(handle);
                return;
            }

            if (_state.Rect.Contains(cursor.Value))
            {
                Cursor = Cursors.SizeAll;
                return;
            }
        }

        Cursor = Cursors.Cross;
    }

    private static Cursor CursorForShapeHandle(ShapeHandleKind handle) => handle switch
    {
        ShapeHandleKind.TopLeft or ShapeHandleKind.BottomRight => Cursors.SizeNWSE,
        ShapeHandleKind.TopRight or ShapeHandleKind.BottomLeft => Cursors.SizeNESW,
        ShapeHandleKind.Top or ShapeHandleKind.Bottom => Cursors.SizeNS,
        ShapeHandleKind.Left or ShapeHandleKind.Right => Cursors.SizeWE,
        ShapeHandleKind.Start or ShapeHandleKind.End => Cursors.SizeAll,
        _ => Cursors.Arrow,
    };

    public int HitRadiusPx() =>
        Math.Max(3, (int)Math.Round(SelectionState.HandleHitRadiusDip * DpiScaleForSurface()));

    private double DpiScaleForSurface()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return _surface.PxScale > 0 ? _surface.PxScale : dpi.DpiScaleX;
    }

    private static Cursor CursorForHandle(SelectionHandle handle) => handle switch
    {
        SelectionHandle.TopLeft or SelectionHandle.BottomRight => Cursors.SizeNWSE,
        SelectionHandle.TopRight or SelectionHandle.BottomLeft => Cursors.SizeNESW,
        SelectionHandle.Top or SelectionHandle.Bottom => Cursors.SizeNS,
        SelectionHandle.Left or SelectionHandle.Right => Cursors.SizeWE,
        _ => Cursors.Cross,
    };

    /// <summary>用 Win32 读全局光标位置，跨屏拖动时也准确。</summary>
    private static PxPoint? GetCursorPosition() =>
        GlobalPointer.TryGetPosition(out var point) ? point : null;

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var cursor = GetCursorPosition();
        if (cursor is null || _lastCursor == cursor) return;

        _lastCursor = cursor;
        Input.OnPointerMoved(cursor.Value);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (IsMouseCaptured || e.LeftButton == MouseButtonState.Pressed) return;
        _lastCursor = null;
        _crosshair.Update(_frameBounds, null, _state.HasSelection);
        Input.OnPointerLeft();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Right)) return;

        if (_annotationLayer.IsEditingText && e.OriginalSource is DependencyObject dep && IsInsideTextBox(dep))
        {
            return;
        }

        var cursor = GetCursorPosition();
        if (cursor is null) return;

        _lastCursor = cursor;

        bool isRight = e.ChangedButton == MouseButton.Right;
        bool isDouble = e.ClickCount >= 2;
        Input.OnButtonDown(cursor.Value, isRight, isDouble);

        // 抓取鼠标：跨屏拖动时仍然由本窗口继续接收事件。
        if (!isRight && !isDouble && IsVisible && !_annotationLayer.IsEditingText) CaptureMouse();
        e.Handled = true;
    }

    private static bool IsInsideTextBox(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is System.Windows.Controls.TextBox) return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Right)) return;

        var cursor = GetCursorPosition() ?? _lastCursor;
        if (cursor is not null)
        {
            _lastCursor = cursor;
            if (e.ChangedButton == MouseButton.Left)
            {
                Input.OnButtonUp(cursor.Value);
            }
        }

        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e) => Input.OnCaptureLost();

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var cursor = GetCursorPosition() ?? _lastCursor;
        if (cursor is null) return;

        Input.OnWheel(cursor.Value, e.Delta);
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_annotationLayer.IsEditingText)
        {
            return;
        }

        Input.OnKeyDown(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers);
        e.Handled = true;
    }

    /// <summary>把键盘焦点拿过来（截图会话开始时对光标所在屏调用）。</summary>
    public void TakeKeyboardFocus()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle != 0)
        {
            WindowApi.SetForegroundWindow(handle);
        }

        Activate();
        Focus();
        Keyboard.Focus(this);
    }

    /// <summary>把冻结帧换成「整屏暗化」用于过渡，避免关闭时闪一下桌面。</summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _frameImage.Source = null;
        _surface.SetFrame(null, PxRect.Empty);
    }
}
