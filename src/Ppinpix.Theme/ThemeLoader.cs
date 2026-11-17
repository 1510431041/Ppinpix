using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;

namespace Ppinpix.Theme;

/// <summary>
/// 把 Theme.xaml 合并进应用程序资源，并提供按键取资源的小工具。
/// App 启动时调用一次 <see cref="EnsureLoaded"/>，之后任何界面都能用 StaticResource/DynamicResource 取到。
/// </summary>
public static class ThemeLoader
{
    private static readonly Uri ThemeUri = new("pack://application:,,,/Ppinpix.Theme;component/Theme.xaml", UriKind.Absolute);

    /// <summary>通用控件样式（按钮 / 开关 / 输入框 / 下拉框 / 滑块 / 滚动条等）。窗口需合并后使用。</summary>
    public static readonly Uri ControlsUri = new("pack://application:,,,/Ppinpix.Theme;component/Controls.xaml", UriKind.Absolute);

    private static bool _loaded;

    public static void EnsureLoaded(Application application)
    {
        if (_loaded) return;

        var dictionary = new ResourceDictionary { Source = ThemeUri };
        application.Resources.MergedDictionaries.Insert(0, dictionary);
        _loaded = true;
    }

    /// <summary>检测 Windows 系统当前是否处于深色应用模式。</summary>
    public static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>把浅/深色设置页动态色刷写入指定窗口或应用的资源字典（key: Pp.Settings.*）。</summary>
    public static void ApplySettingsTheme(ResourceDictionary target, bool isDark)
    {
        var windowBg = isDark ? Tokens.Dark.WindowBg : Tokens.Light.WindowBg;
        var cardBg = isDark ? Tokens.Dark.CardBg : Tokens.Light.CardBg;
        var cardStroke = isDark ? Tokens.Dark.CardStroke : Tokens.Light.CardStroke;
        var navBg = isDark ? Tokens.Dark.NavSelectedBg : Tokens.Light.NavSelectedBg;
        var textPrimary = isDark ? Tokens.Dark.TextPrimary : Tokens.Light.TextPrimary;
        var textSecondary = isDark ? Tokens.Dark.TextSecondary : Tokens.Light.TextSecondary;
        var controlBg = isDark ? Tokens.SurfaceRaised : Color.FromRgb(0xF8, 0xF9, 0xFB);
        var hoverBg = isDark ? Tokens.SurfaceHover : Color.FromArgb(14, 0, 0, 0);

        var windowBrush = FreezeBrush(windowBg);
        var cardBrush = FreezeBrush(cardBg);
        var strokeBrush = FreezeBrush(cardStroke);
        var navBrush = FreezeBrush(navBg);
        var textBrush = FreezeBrush(textPrimary);
        var secondaryBrush = FreezeBrush(textSecondary);
        var controlBrush = FreezeBrush(controlBg);
        var hoverBrush = FreezeBrush(hoverBg);

        target["Pp.Settings.WindowBg"] = windowBrush;
        target["Pp.Settings.CardBg"] = cardBrush;
        target["Pp.Settings.CardStroke"] = strokeBrush;
        target["Pp.Settings.NavSelectedBg"] = navBrush;
        target["Pp.Settings.TextPrimary"] = textBrush;
        target["Pp.Settings.TextSecondary"] = secondaryBrush;
        target["Pp.Settings.ControlBg"] = controlBrush;
        target["Pp.Settings.HoverBg"] = hoverBrush;

        // 控件描边 / 按下态 / 禁用文字：由浅深色基准派生，供 Controls.xaml 使用
        target["Pp.Settings.InputBorder"] = FreezeBrush(isDark ? Color.FromRgb(0x3B, 0x42, 0x4D) : Color.FromRgb(0xD3, 0xD8, 0xDF));
        target["Pp.Settings.PressedBg"] = FreezeBrush(isDark ? Tokens.SurfacePressed : Color.FromArgb(28, 0, 0, 0));
        target["Pp.Settings.TextDisabled"] = FreezeBrush(isDark ? Tokens.TextDisabled : Color.FromRgb(0x9A, 0xA3, 0xAF));

        // 兼容简短命名键（Settings 子面板与自定义控件共用）
        target["Pp.Settings.Card"] = cardBrush;
        target["Pp.Settings.Border"] = strokeBrush;
        target["Pp.Settings.Text"] = textBrush;
        target["Pp.Settings.Muted"] = secondaryBrush;
        target["Pp.Settings.Input"] = controlBrush;
    }

    private static SolidColorBrush FreezeBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>取资源；找不到时抛出，避免静默出现“没颜色”的界面。</summary>
    public static T Find<T>(string key) where T : class
    {
        object? value = Application.Current?.TryFindResource(key);
        return value as T
            ?? throw new InvalidOperationException($"主题资源未找到：{key}（是否忘了调用 ThemeLoader.EnsureLoaded？）");
    }

    public static Brush Brush(string key) => Find<Brush>(key);
}

/// <summary>
/// 用代码创建符合视觉规范的常用控件。
/// 覆盖层、工具栏这类需要按物理像素精确摆放的界面全部用代码构建，比 XAML 更好控制。
/// </summary>
public static class UiFactory
{
    /// <summary>工具栏上的图标按钮。</summary>
    public static Button IconButton(string glyph, string tooltip, Action onClick, bool accent = false, string? shortcut = null)
    {
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = ThemeLoader.Find<FontFamily>("Pp.IconFont"),
            FontSize = Tokens.IconSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        TextOptions.SetTextFormattingMode(icon, TextFormattingMode.Ideal);

        var button = new Button
        {
            Style = ThemeLoader.Find<Style>(accent ? "Pp.ToolButtonAccent" : "Pp.ToolButton"),
            Content = icon,
            ToolTip = BuildTooltip(tooltip, shortcut),
        };

        if (accent) icon.Foreground = ThemeLoader.Brush("Pp.OnAccent");
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>工具栏上的文字按钮（用于“复制译文”这类动作）。</summary>
    public static Button TextButton(string text, string tooltip, Action onClick, bool accent = false)
    {
        var label = new TextBlock
        {
            Text = text,
            FontFamily = ThemeLoader.Find<FontFamily>("Pp.UiFont"),
            FontSize = Tokens.FontBody,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var button = new Button
        {
            Style = ThemeLoader.Find<Style>(accent ? "Pp.ToolButtonAccent" : "Pp.ToolButton"),
            Width = double.NaN,
            Padding = new Thickness(Tokens.Spacing3, 0, Tokens.Spacing3, 0),
            Content = label,
            ToolTip = BuildTooltip(tooltip, null),
        };

        if (accent) label.Foreground = ThemeLoader.Brush("Pp.OnAccent");
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>组间 1×20 分隔线。</summary>
    public static Border Separator() => new() { Style = ThemeLoader.Find<Style>("Pp.ToolbarSeparator") };

    /// <summary>尺寸 / 坐标徽标（等宽字体）。</summary>
    public static Border Badge(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            Style = ThemeLoader.Find<Style>("Pp.MonoText"),
        };

        return new Border
        {
            Background = ThemeLoader.Brush("Pp.Surface"),
            BorderBrush = ThemeLoader.Brush("Pp.Stroke"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Tokens.CornerBadge),
            Padding = new Thickness(Tokens.Spacing2, 2, Tokens.Spacing2, 2),
            Child = label,
            SnapsToDevicePixels = true,
        };
    }

    /// <summary>“名称  快捷键”样式的提示，使用与浮层一致的深色外观。</summary>
    public static ToolTip BuildTooltip(string text, string? shortcut)
    {
        var block = new TextBlock { FontSize = Tokens.FontCaption };
        block.Inlines.Add(new Run(text));
        if (!string.IsNullOrEmpty(shortcut))
        {
            block.Inlines.Add(new Run("  " + shortcut) { Foreground = ThemeLoader.Brush("Pp.TextSecondary") });
        }

        return new ToolTip
        {
            Style = ThemeLoader.Find<Style>("Pp.ToolTip"),
            Content = block,
        };
    }

    /// <summary>按视觉规范给浮层加阴影。</summary>
    public static void ApplyShadow(Border target, double blur, double depth, double opacity)
    {
        target.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = blur,
            ShadowDepth = depth,
            Direction = 270,
            Opacity = opacity,
            Color = Colors.Black,
            RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance,
        };
    }
}
