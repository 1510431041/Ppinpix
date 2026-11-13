using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ppinpix.Capture.Output;
using Ppinpix.Capture.Overlay;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Display;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Settings;
using Ppinpix.Theme;

namespace Ppinpix.Capture.Annotation;

/// <summary>贴图二次编辑标注窗口（复用完整标注管线与导出渲染器）。</summary>
public sealed class PinAnnotationWindow : Window, ICaptureToolbarActions
{
    private readonly ScreenFrame _source;
    private readonly DisplayService _displays;
    private readonly AnnotationEditor _editor;
    private readonly AnnotationLayer _layer;
    private readonly CaptureToolbar _toolbar;
    private readonly ToolPropertiesBar _propertiesBar;
    private readonly PxRect _localBounds;
    private bool _dragging;
    private double _dpiScale = 1.0;

    public PinAnnotationWindow(ScreenFrame source, AnnotationSettings settings, DisplayService displays)
    {
        // 将边界归一化到 (0,0) 以在本地编辑器内精准坐标对齐
        _localBounds = new PxRect(0, 0, source.Width, source.Height);
        _source = new ScreenFrame(_localBounds, source.Stride, source.Pixels.ToArray());
        _displays = displays;
        _editor = new AnnotationEditor(settings);

        Title = "编辑标注 — Ppinpix";
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x16, 0x19, 0x1E));

        var img = new Image
        {
            Source = CaptureOutput.ToBitmapSource(_source),
            Stretch = Stretch.Fill,
        };

        _layer = new AnnotationLayer(_editor, _source, _localBounds, 1.0);
        var host = new Grid { Background = Brushes.Transparent, ClipToBounds = true };
        host.Children.Add(img);
        host.Children.Add(_layer);

        Loaded += (_, _) =>
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            _dpiScale = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
            double wDip = _source.Width / _dpiScale;
            double hDip = _source.Height / _dpiScale;
            // 若图像过大，限制在屏幕工作区 80% 内等比缩放显示（内部坐标仍为原图物理像素）
            var mon = _displays.FromWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            double maxW = mon.WorkArea.Width * 0.8 / _dpiScale;
            double maxH = mon.WorkArea.Height * 0.8 / _dpiScale;
            double fit = Math.Min(1.0, Math.Min(maxW / Math.Max(1, wDip), maxH / Math.Max(1, hDip)));

            img.Width = wDip * fit;
            img.Height = hDip * fit;
            _layer.Width = _source.Width;
            _layer.Height = _source.Height;
            _layer.RenderTransform = new ScaleTransform(fit / _dpiScale, fit / _dpiScale);
            host.Width = wDip * fit;
            host.Height = hDip * fit;

            PositionToolbars();
        };

        var outer = new Border
        {
            Padding = new Thickness(12),
            Child = new Border
            {
                BorderBrush = ThemeLoader.Brush("Pp.Stroke"),
                BorderThickness = new Thickness(1),
                Child = host,
            },
        };
        Content = outer;

        _toolbar = new CaptureToolbar(this, annotationOnly: true);
        _propertiesBar = new ToolPropertiesBar(_editor);
        _editor.SetTool(AnnotationTool.Rectangle);
        _editor.StateChanged += () =>
        {
            _toolbar.SetToolState(_editor.ActiveTool, _editor.CanUndo, _editor.CanRedo);
            PositionToolbars();
        };
        _editor.TextEditRequested += req => _layer.OpenTextEditor(req);

        host.MouseLeftButtonDown += OnCanvasMouseDown;
        host.MouseMove += OnCanvasMouseMove;
        host.MouseLeftButtonUp += OnCanvasMouseUp;
        PreviewKeyDown += OnKeyDownHandler;
        LocationChanged += (_, _) => PositionToolbars();
        Closed += (_, _) =>
        {
            _toolbar.Close();
            _propertiesBar.Close();
            _editor.Dispose();
        };
    }

    public ScreenFrame? ResultFrame { get; private set; }

    private void PositionToolbars()
    {
        if (!IsVisible) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        double scale = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
        var winPx = new PxRect(
            (int)Math.Round(Left * scale),
            (int)Math.Round(Top * scale),
            (int)Math.Ceiling(ActualWidth * scale),
            (int)Math.Ceiling(ActualHeight * scale));
        var mon = _displays.FromPoint(winPx.Center);
        _toolbar.ShowToolbar();
        _toolbar.SetToolState(_editor.ActiveTool, _editor.CanUndo, _editor.CanRedo);
        _toolbar.Position(winPx, mon);
        _propertiesBar.PositionNear(_toolbar.PlacedBounds, winPx, mon);
    }

    private (double X, double Y) ToCanvasPx(MouseEventArgs e)
    {
        var p = e.GetPosition(_layer);
        return (Math.Clamp(p.X, 0, _source.Width), Math.Clamp(p.Y, 0, _source.Height));
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_layer.IsEditingText)
        {
            if (e.OriginalSource is DependencyObject dep && IsTextBox(dep)) return;
            _layer.CommitActiveTextEdit();
            return;
        }
        var (x, y) = ToCanvasPx(e);
        _dragging = _editor.OnPointerDown(x, y, 8.0, e.ClickCount);
        if (_dragging) ((UIElement)sender).CaptureMouse();
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var (x, y) = ToCanvasPx(e);
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        _editor.OnPointerMove(x, y, shift, 8.0);
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
        var (x, y) = ToCanvasPx(e);
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        _editor.OnPointerUp(x, y, shift);
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (_layer.IsEditingText) return;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        switch (e.Key)
        {
            case Key.Escape: Cancel(); e.Handled = true; break;
            case Key.Enter: Accept(); e.Handled = true; break;
            case Key.Z when ctrl: Undo(); e.Handled = true; break;
            case Key.Y when ctrl: Redo(); e.Handled = true; break;
            case Key.Delete: _editor.DeleteSelected(); e.Handled = true; break;
        }
    }

    private static bool IsTextBox(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is TextBox) return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    public void SelectTool(AnnotationTool tool)
    {
        _layer.CommitActiveTextEdit();
        if (tool == AnnotationTool.Mosaic)
        {
            bool activeMask = _editor.ActiveTool is AnnotationTool.Mosaic or AnnotationTool.Blur;
            _editor.SetTool(activeMask ? AnnotationTool.Select : _editor.LastMaskTool);
        }
        else
        {
            _editor.SetTool(tool);
        }
    }

    public void Undo() { _layer.CommitActiveTextEdit(); _editor.Undo(); }
    public void Redo() { _layer.CommitActiveTextEdit(); _editor.Redo(); }
    public void Copy() { }
    public void Save() { }
    public void Pin() { }
    public void Ocr() { }
    public void Translate() { }
    public void Cancel() { DialogResult = false; Close(); }
    public void Accept()
    {
        _layer.CommitActiveTextEdit();
        ResultFrame = _editor.RenderToFrame(_source, _localBounds);
        DialogResult = true;
        Close();
    }
}
