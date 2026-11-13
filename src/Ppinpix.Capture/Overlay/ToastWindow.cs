using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Ppinpix.Capture.Interop;
using Ppinpix.Core.Display;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Input;
using Ppinpix.Theme;

namespace Ppinpix.Capture.Overlay;

public enum ToastKind
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>
/// 右下角提示条。出现 160ms、停留 1.6s、消失 160ms，鼠标穿透，不抢焦点。
/// 同一时间只保留一条，新消息直接替换旧的。
/// </summary>
public sealed class ToastWindow : Window
{
    private const double MarginDip = 16;
    private const int StayMilliseconds = 1600;

    private static ToastWindow? _current;

    private readonly Border _bar;
    private readonly TextBlock _icon;
    private readonly TextBlock _text;
    private readonly DispatcherTimer _timer;

    private ToastWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 360;
        Title = "Ppinpix 提示";

        _bar = new Border
        {
            Width = 3,
            Background = ThemeLoader.Brush("Pp.Accent"),
            CornerRadius = new CornerRadius(2),
        };

        _icon = new TextBlock
        {
            FontFamily = ThemeLoader.Find<FontFamily>("Pp.IconFont"),
            FontSize = Tokens.IconSize,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeLoader.Brush("Pp.TextPrimary"),
        };

        _text = new TextBlock
        {
            FontFamily = ThemeLoader.Find<FontFamily>("Pp.UiFont"),
            FontSize = Tokens.FontBody,
            Foreground = ThemeLoader.Brush("Pp.TextPrimary"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 300,
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(Tokens.Spacing3, Tokens.Spacing2, Tokens.Spacing3, Tokens.Spacing2),
        };
        content.Children.Add(_icon);
        _icon.Margin = new Thickness(0, 0, Tokens.Spacing2, 0);
        content.Children.Add(_text);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_bar, 0);
        Grid.SetColumn(content, 1);
        grid.Children.Add(_bar);
        grid.Children.Add(content);

        var panel = new Border
        {
            Style = ThemeLoader.Find<Style>("Pp.ToolbarPanel"),
            Child = grid,
        };
        UiFactory.ApplyShadow(panel, Tokens.ShadowBlur, Tokens.ShadowDepth, Tokens.ShadowOpacity);
        Content = panel;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(StayMilliseconds) };
        _timer.Tick += (_, _) => Close();

        Loaded += (_, _) => WindowChrome.MakeClickThrough(this);
    }

    public static void Show(string text, ToastKind kind, DisplayService displays)
    {
        _current?.Close();

        var toast = new ToastWindow();
        _current = toast;

        toast._text.Text = text;
        toast._icon.Text = kind switch
        {
            ToastKind.Success => Icons.Success,
            ToastKind.Warning => Icons.Warning,
            ToastKind.Error => Icons.Error,
            _ => Icons.Info,
        };
        toast._bar.Background = kind switch
        {
            ToastKind.Success => ThemeLoader.Brush("Pp.Success"),
            ToastKind.Warning => ThemeLoader.Brush("Pp.Warning"),
            ToastKind.Error => ThemeLoader.Brush("Pp.Danger"),
            _ => ThemeLoader.Brush("Pp.Accent"),
        };

        toast.Show();
        toast.Place(displays);
        toast._timer.Start();
    }

    /// <summary>放到光标所在显示器工作区的右下角。</summary>
    private void Place(DisplayService displays)
    {
        UpdateLayout();

        var monitor = displays.FromPoint(GlobalPointer.GetPosition());
        var work = monitor.WorkArea;
        double scale = monitor.Scale <= 0 ? 1.0 : monitor.Scale;

        int widthPx = (int)Math.Ceiling(ActualWidth * scale);
        int heightPx = (int)Math.Ceiling(ActualHeight * scale);
        int margin = (int)Math.Round(MarginDip * scale);

        int x = work.Right - margin - widthPx;
        int y = work.Bottom - margin - heightPx;

        WindowChrome.MovePhysical(this, new PxPoint(x, y));
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        if (ReferenceEquals(_current, this)) _current = null;
        base.OnClosed(e);
    }
}
