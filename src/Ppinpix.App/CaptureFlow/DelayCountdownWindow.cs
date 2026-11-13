using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Ppinpix.Core.Display;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Interop;
using Ppinpix.Theme;

namespace Ppinpix.App.CaptureFlow;

/// <summary>延迟截图倒计时浮动角标（按 Esc 或右键可随时中止）。</summary>
public sealed class DelayCountdownWindow : Window
{
    private readonly TextBlock _text;
    private readonly DispatcherTimer _timer;
    private readonly Action _onComplete;
    private readonly Action _onCancel;
    private int _remainingMs;
    private bool _finished;

    public DelayCountdownWindow(int delayMs, DisplayService displays, Action onComplete, Action onCancel)
    {
        _remainingMs = Math.Max(200, delayMs);
        _onComplete = onComplete;
        _onCancel = onCancel;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;

        _text = new TextBlock
        {
            Style = ThemeLoader.Find<Style>("Pp.MonoText"),
            FontSize = 13,
            Foreground = ThemeLoader.Brush("Pp.TextPrimary"),
        };

        var card = new Border
        {
            Style = ThemeLoader.Find<Style>("Pp.ToolbarPanel"),
            CornerRadius = new CornerRadius(Tokens.CornerPanel),
            Padding = new Thickness(14, 9, 14, 9),
            Child = _text,
            Margin = new Thickness(Tokens.ShadowDepth),
        };
        UiFactory.ApplyShadow(card, 14, 4, 0.45);
        Content = card;

        Loaded += (_, _) =>
        {
            var primary = displays.Monitors.FirstOrDefault(m => m.IsPrimary) ?? displays.Monitors[0];
            var dpi = VisualTreeHelper.GetDpi(this);
            double scale = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
            int wPx = (int)Math.Ceiling(ActualWidth * scale);
            int x = primary.WorkArea.Right - wPx - (int)Math.Round(24 * scale);
            int y = primary.WorkArea.Top + (int)Math.Round(24 * scale);
            nint hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != 0)
            {
                WindowApi.SetWindowPos(
                    hwnd, WindowApi.HWND_TOPMOST, x, y, 0, 0,
                    WindowApi.SWP_NOSIZE | WindowApi.SWP_NOACTIVATE);
            }
            Activate();
            Focus();
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Cancel();
                e.Handled = true;
            }
        };
        MouseRightButtonUp += (_, _) => Cancel();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) =>
        {
            _remainingMs -= 100;
            if (_remainingMs <= 0)
            {
                FinishCapture();
            }
            else
            {
                UpdateLabel();
            }
        };

        UpdateLabel();
        _timer.Start();
    }

    private void UpdateLabel()
    {
        double sec = Math.Ceiling(_remainingMs / 100.0) / 10.0;
        _text.Text = $"{sec:0.0} 秒后抓屏…（Esc 取消）";
    }

    private void FinishCapture()
    {
        if (_finished) return;
        _finished = true;
        _timer.Stop();
        Close();
        Dispatcher.BeginInvoke(DispatcherPriority.Render, _onComplete);
    }

    public void Cancel()
    {
        if (_finished) return;
        _finished = true;
        _timer.Stop();
        Close();
        _onCancel();
    }
}
