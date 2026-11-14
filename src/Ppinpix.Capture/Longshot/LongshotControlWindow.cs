using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ppinpix.Capture.Interop;
using Ppinpix.Capture.Output;
using Ppinpix.Capture.Session;
using Ppinpix.Core;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Display;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Input;
using Ppinpix.Core.Interop;
using Ppinpix.Theme;

namespace Ppinpix.Capture.Longshot;

/// <summary>
/// 滚动长截图控制浮窗 + 选区外侧穿透边框。
/// 支持自动鼠标滚轮滚动与手动滚轮滚动两种模式，实时显示已拼接高度与缩略图。
/// </summary>
internal sealed class LongshotControlWindow : Window
{
    private const int BorderThicknessPx = 3;
    private const int GapDip = 10;

    private readonly PxRect _region;
    private readonly DisplayService _displays;
    private readonly LongshotStitcher _stitcher;
    private readonly Action<CaptureAction, ScreenFrame?> _onFinished;
    private readonly GdiScreenCapture _screenCapture = new();

    private readonly Window _frameHighlight;
    private readonly Image _previewImage;
    private readonly TextBlock _statusText;
    private readonly TextBlock _hintText;
    private readonly Button _autoScrollButton;
    private readonly TextBlock _autoScrollLabel;
    private readonly DispatcherTimer _tickTimer;

    private bool _autoScrolling = true;
    private bool _waitingScrollCapture;
    private int _consecutiveIdleTicks;
    private bool _closed;

    public LongshotControlWindow(
        PxRect region,
        ScreenFrame initialRegionFrame,
        DisplayService displays,
        Action<CaptureAction, ScreenFrame?> onFinished)
    {
        _region = region;
        _displays = displays;
        _stitcher = new LongshotStitcher(initialRegionFrame);
        _onFinished = onFinished;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "Ppinpix 滚动长截图";

        var card = new Border
        {
            Style = ThemeLoader.Find<Style>("Pp.ToolbarPanel"),
            CornerRadius = new CornerRadius(Tokens.CornerPanel),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(Tokens.ShadowDepth),
            Width = 236,
        };
        UiFactory.ApplyShadow(card, blur: 18, depth: 5, opacity: 0.45);

        var stack = new StackPanel();

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var title = new TextBlock
        {
            Text = "滚动长截图",
            FontFamily = ThemeLoader.Find<FontFamily>("Pp.UiFont"),
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeLoader.Brush("Pp.TextPrimary"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.Children.Add(title);
        stack.Children.Add(header);

        _statusText = new TextBlock
        {
            Text = $"高度 {initialRegionFrame.Height} px · 第 1 帧",
            FontFamily = ThemeLoader.Find<FontFamily>("Pp.MonoFont"),
            FontSize = 11.5,
            Foreground = ThemeLoader.Brush("Pp.Accent"),
            Margin = new Thickness(0, 0, 0, 6),
        };
        stack.Children.Add(_statusText);

        var previewHost = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x13, 0x16, 0x1B)),
            BorderBrush = ThemeLoader.Brush("Pp.Stroke"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(4),
            MaxHeight = 240,
            MinHeight = 84,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _previewImage = new Image
        {
            Stretch = Stretch.Uniform,
            Source = CaptureOutput.ToBitmapSource(initialRegionFrame),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        RenderOptions.SetBitmapScalingMode(_previewImage, BitmapScalingMode.Linear);
        previewHost.Child = _previewImage;
        stack.Children.Add(previewHost);

        _hintText = new TextBlock
        {
            Text = "正在自动向下滚动（空格暂停 / 可随时手动滚轮）",
            FontFamily = ThemeLoader.Find<FontFamily>("Pp.UiFont"),
            FontSize = 11,
            Foreground = ThemeLoader.Brush("Pp.TextSecondary"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        stack.Children.Add(_hintText);

        _autoScrollLabel = new TextBlock
        {
            Text = "⏸ 暂停自动滚动",
            FontFamily = ThemeLoader.Find<FontFamily>("Pp.UiFont"),
            FontSize = 12,
            Foreground = ThemeLoader.Brush("Pp.TextPrimary"),
        };
        _autoScrollButton = new Button
        {
            Style = ThemeLoader.Find<Style>("Pp.ToolButton"),
            Width = double.NaN,
            Height = 30,
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(10, 0, 10, 0),
            Background = ThemeLoader.Brush("Pp.SurfaceRaised"),
            Content = _autoScrollLabel,
        };
        _autoScrollButton.Click += (_, _) => ToggleAutoScroll();
        stack.Children.Add(_autoScrollButton);

        var actionsRow = new UniformGridCompatRow(
            BuildActionButton("✓ 完成", "完成并复制 (Enter)", () => Complete(CaptureAction.Copy), accent: true),
            BuildActionButton("📌 贴图", "完成并贴到屏幕", () => Complete(CaptureAction.Pin), visible: Features.Pin),
            BuildActionButton("💾 保存", "完成并保存图片", () => Complete(CaptureAction.Save)),
            BuildActionButton("✕ 取消", "取消长截图 (Esc)", Cancel));
        stack.Children.Add(actionsRow);

        card.Child = stack;
        Content = card;

        _frameHighlight = BuildSelectionOutlineWindow(region);

        _tickTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(280),
        };
        _tickTimer.Tick += OnTick;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Cancel();
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Complete(CaptureAction.Copy);
            }
            else if (e.Key == Key.Space)
            {
                e.Handled = true;
                ToggleAutoScroll();
            }
        };

        Loaded += (_, _) =>
        {
            WindowChrome.MakeNoActivate(this);
            PositionControlWindow();
            _tickTimer.Start();
        };
        Closed += (_, _) => StopAndCleanup();
    }

    public void Start()
    {
        _frameHighlight.Show();
        WindowChrome.MakeClickThrough(_frameHighlight);
        var outlineBounds = _region.Inflate(BorderThicknessPx + 1);
        WindowChrome.PlacePhysical(_frameHighlight, outlineBounds);

        Show();
        PositionControlWindow();
        WindowChrome.BringToTopWithoutActivation(this);
    }

    private void ToggleAutoScroll()
    {
        _autoScrolling = !_autoScrolling;
        _consecutiveIdleTicks = 0;
        if (_autoScrolling)
        {
            _autoScrollLabel.Text = "⏸ 暂停自动滚动";
            _hintText.Text = "正在自动向下滚动（空格暂停 / 可随时手动滚轮）";
        }
        else
        {
            _autoScrollLabel.Text = "▶ 继续自动滚动";
            _hintText.Text = "手动模式：请将鼠标放在目标区域内滚动滚轮";
        }
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_closed || _waitingScrollCapture) return;

        try
        {
            if (_autoScrolling)
            {
                _waitingScrollCapture = true;
                SendTargetWheel();
                await Task.Delay(150);
                _waitingScrollCapture = false;
                if (_closed) return;
            }

            var current = _screenCapture.Capture(_region);
            var step = _stitcher.AppendFrame(current);

            if (step.Status == StitchStatus.Stitched)
            {
                _consecutiveIdleTicks = 0;
                _statusText.Text = $"高度 {step.TotalHeightPx} px · 第 {_stitcher.FrameCount} 帧";
                UpdateLiveThumbnail();
            }
            else if (step.Status == StitchStatus.MaxHeightReached)
            {
                _autoScrolling = false;
                _autoScrollLabel.Text = "▶ 已达最大高度";
                _autoScrollButton.IsEnabled = false;
                _hintText.Text = $"已达到单张最大高度 {LongshotStitcher.DefaultMaxHeightPx} px，请点击完成。";
            }
            else if (step.Status == StitchStatus.NoMovement && _autoScrolling)
            {
                _consecutiveIdleTicks++;
                if (_consecutiveIdleTicks >= 5)
                {
                    _autoScrolling = false;
                    _autoScrollLabel.Text = "▶ 继续自动滚动";
                    _hintText.Text = "已到达底部或页面停止滚动，可点击完成或手动继续滚动。";
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("滚动长截图采样失败。", ex);
        }
        finally
        {
            _waitingScrollCapture = false;
        }
    }

    private void SendTargetWheel()
    {
        if (!GlobalPointer.TryGetPosition(out var pt) || !_region.Contains(pt))
        {
            WindowApi.SetCursorPos(_region.Center.X, _region.Center.Y);
        }
        WindowApi.mouse_event(WindowApi.MOUSEEVENTF_WHEEL, 0, 0, -240, 0);
    }

    private void UpdateLiveThumbnail()
    {
        var preview = _stitcher.BuildPreviewFrame();
        _previewImage.Source = CaptureOutput.ToBitmapSource(preview);
    }

    private void Complete(CaptureAction action)
    {
        if (_closed) return;
        _closed = true;
        _tickTimer.Stop();

        ScreenFrame finalFrame;
        try
        {
            finalFrame = _stitcher.BuildFrame();
        }
        finally
        {
            Close();
        }

        _onFinished(action, finalFrame);
    }

    private void Cancel()
    {
        if (_closed) return;
        _closed = true;
        _tickTimer.Stop();
        Close();
        _onFinished(CaptureAction.Cancel, null);
    }

    private void StopAndCleanup()
    {
        _closed = true;
        _tickTimer.Stop();
        if (_frameHighlight.IsVisible)
        {
            _frameHighlight.Close();
        }
        else
        {
            try { _frameHighlight.Close(); } catch { }
        }
    }

    private void PositionControlWindow()
    {
        UpdateLayout();
        var mon = _displays.FromPoint(_region.Center);
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0) scale = mon.Scale > 0 ? mon.Scale : 1.0;

        int wPx = (int)Math.Ceiling(ActualWidth * scale);
        int hPx = (int)Math.Ceiling(ActualHeight * scale);
        if (wPx <= 0 || hPx <= 0) return;

        int gap = (int)Math.Round(GapDip * scale);
        var work = mon.WorkArea;

        // 优先摆在选区右侧外边，放不下就左侧，再放不下摆在左下/左上角（始终避开选区内部）
        int x = _region.Right + gap;
        if (x + wPx > work.Right)
        {
            x = _region.Left - gap - wPx;
        }
        int y = _region.Bottom - hPx;
        if (x < work.Left)
        {
            x = Math.Clamp(_region.Right - wPx, work.Left + gap, Math.Max(work.Left + gap, work.Right - wPx - gap));
            y = _region.Bottom + gap;
            if (y + hPx > work.Bottom)
            {
                y = Math.Max(work.Top + gap, _region.Top - gap - hPx);
            }
        }

        x = Math.Clamp(x, work.Left + gap, Math.Max(work.Left + gap, work.Right - wPx - gap));
        y = Math.Clamp(y, work.Top + gap, Math.Max(work.Top + gap, work.Bottom - hPx - gap));

        WindowChrome.MovePhysical(this, new PxPoint(x, y));
        WindowChrome.BringToTopWithoutActivation(this);
    }

    private static Window BuildSelectionOutlineWindow(PxRect region)
    {
        var border = new Border
        {
            BorderBrush = ThemeLoader.Brush("Pp.Accent"),
            BorderThickness = new Thickness(2),
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        return new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ShowActivated = false,
            IsHitTestVisible = false,
            Content = border,
            Title = "Ppinpix 长截图选区框",
        };
    }

    private static Button BuildActionButton(string label, string tooltip, Action onClick, bool accent = false, bool visible = true)
    {
        var btn = UiFactory.TextButton(label, tooltip, onClick, accent);
        btn.Height = 28;
        btn.Padding = new Thickness(6, 0, 6, 0);
        btn.Margin = new Thickness(2, 0, 2, 0);
        btn.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        return btn;
    }

    private sealed class UniformGridCompatRow : StackPanel
    {
        public UniformGridCompatRow(params Button[] buttons)
        {
            Orientation = Orientation.Horizontal;
            HorizontalAlignment = HorizontalAlignment.Center;
            foreach (var b in buttons)
            {
                if (b.Visibility == Visibility.Visible)
                {
                    Children.Add(b);
                }
            }
        }
    }
}
