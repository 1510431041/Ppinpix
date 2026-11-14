using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Ppinpix.Capture.Interop;
using Ppinpix.Core.Display;
using Ppinpix.Core.Geometry;
using Ppinpix.Theme;

namespace Ppinpix.Capture.Annotation;

/// <summary>
/// 标注工具副参数浮窗（紧贴主工具栏展示色板、线宽、字号、实心/虚线、马赛克/模糊粒度等选项）。
/// </summary>
internal sealed class ToolPropertiesBar : Window
{
    private const double GapDip = 6;

    private readonly AnnotationEditor _editor;
    private readonly StackPanel _panel;
    private double _dpiScale = 1.0;

    public ToolPropertiesBar(AnnotationEditor editor)
    {
        _editor = editor;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;

        _panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var card = new Border
        {
            Style = ThemeLoader.Find<Style>("Pp.ToolbarPanel"),
            CornerRadius = new CornerRadius(Tokens.CornerPanel),
            Padding = new Thickness(8, 5, 8, 5),
            Child = _panel,
        };
        UiFactory.ApplyShadow(card, blur: 14, depth: 3, opacity: 0.42);
        Content = card;

        Loaded += (_, _) => WindowChrome.MakeNoActivate(this);
        SizeChanged += (_, _) =>
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            _dpiScale = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
        };

        _editor.StateChanged += Rebuild;
        Rebuild();
    }

    public bool ShouldBeVisible
    {
        get
        {
            var tool = EffectiveTool();
            return tool is not (AnnotationTool.None or AnnotationTool.Select or AnnotationTool.Eraser);
        }
    }

    public void PositionNear(PxRect mainRect, PxRect selection, MonitorInfo monitor)
    {
        if (!ShouldBeVisible)
        {
            Hide();
            return;
        }

        if (!IsVisible) Show();
        UpdateLayout();

        double scale = _dpiScale <= 0 ? 1.0 : _dpiScale;
        int widthPx = (int)Math.Ceiling(ActualWidth * scale);
        int heightPx = (int)Math.Ceiling(ActualHeight * scale);
        if (widthPx <= 0 || heightPx <= 0) return;

        int gap = (int)Math.Round(GapDip * scale);
        var work = monitor.WorkArea;

        int x = mainRect.IsEmpty
            ? selection.Right - widthPx
            : mainRect.Left;

        // 优先摆在主工具栏紧邻一侧：主工具栏在选区下方时放其正下方，在上方时放其正上方
        int y;
        bool mainBelowSelection = !mainRect.IsEmpty && mainRect.Top >= selection.Bottom - 4;
        if (mainBelowSelection)
        {
            y = mainRect.Bottom + gap;
            if (y + heightPx > work.Bottom) y = mainRect.Top - gap - heightPx;
        }
        else if (!mainRect.IsEmpty)
        {
            y = mainRect.Top - gap - heightPx;
            if (y < work.Top) y = mainRect.Bottom + gap;
        }
        else
        {
            y = selection.Bottom + gap * 2 + 44;
        }

        x = Math.Clamp(x, work.Left + gap, Math.Max(work.Left + gap, work.Right - widthPx - gap));
        y = Math.Clamp(y, work.Top + gap, Math.Max(work.Top + gap, work.Bottom - heightPx - gap));

        WindowChrome.MovePhysical(this, new PxPoint(x, y));
        WindowChrome.BringToTopWithoutActivation(this);
    }

    private AnnotationTool EffectiveTool() =>
        _editor.SelectedShape?.Tool ?? _editor.ActiveTool;

    private ShapeStyle EffectiveStyle() =>
        _editor.SelectedShape?.Style ?? _editor.CurrentStyle;

    private void Rebuild()
    {
        _panel.Children.Clear();

        var tool = EffectiveTool();
        if (tool is AnnotationTool.None or AnnotationTool.Select or AnnotationTool.Eraser)
        {
            Hide();
            return;
        }

        var style = EffectiveStyle();

        // 马赛克 / 模糊 / 聚光灯没有颜色选择
        if (tool is not (AnnotationTool.Mosaic or AnnotationTool.Blur or AnnotationTool.Spotlight))
        {
            BuildColorSwatches(style.Argb);
            _panel.Children.Add(UiFactory.Separator());
        }

        switch (tool)
        {
            case AnnotationTool.Rectangle:
                BuildLineWidthPills(style.LineWidth);
                _panel.Children.Add(UiFactory.Separator());
                _panel.Children.Add(TogglePill("填充", style.Filled, v => _editor.UpdateStyle(s => s with { Filled = v })));
                _panel.Children.Add(TogglePill("虚线", style.Dashed, v => _editor.UpdateStyle(s => s with { Dashed = v })));
                _panel.Children.Add(UiFactory.Separator());
                BuildCornerRadiusPills(style.CornerRadius);
                break;

            case AnnotationTool.Ellipse:
                BuildLineWidthPills(style.LineWidth);
                _panel.Children.Add(UiFactory.Separator());
                _panel.Children.Add(TogglePill("填充", style.Filled, v => _editor.UpdateStyle(s => s with { Filled = v })));
                _panel.Children.Add(TogglePill("虚线", style.Dashed, v => _editor.UpdateStyle(s => s with { Dashed = v })));
                break;

            case AnnotationTool.Line:
                BuildLineWidthPills(style.LineWidth);
                _panel.Children.Add(UiFactory.Separator());
                _panel.Children.Add(TogglePill("虚线", style.Dashed, v => _editor.UpdateStyle(s => s with { Dashed = v })));
                break;

            case AnnotationTool.Arrow:
            case AnnotationTool.Freehand:
            case AnnotationTool.Highlight:
                BuildLineWidthPills(style.LineWidth);
                break;

            case AnnotationTool.NumberBadge:
                BuildOptionPills(
                    [("小", 20), ("中", 24), ("大", 32)],
                    v => v == style.BadgeSize,
                    v => _editor.UpdateStyle(s => s with { BadgeSize = v }));
                break;

            case AnnotationTool.Text:
                BuildOptionPills(
                    [("14", 14d), ("18", 18d), ("24", 24d), ("32", 32d)],
                    v => Math.Abs(v - style.FontSize) < 0.5,
                    v => _editor.UpdateStyle(s => s with { FontSize = v }));
                _panel.Children.Add(UiFactory.Separator());
                _panel.Children.Add(TogglePill("B", style.Bold, v => _editor.UpdateStyle(s => s with { Bold = v }), bold: true));
                _panel.Children.Add(TogglePill("底框", style.TextBackground, v => _editor.UpdateStyle(s => s with { TextBackground = v })));
                break;

            case AnnotationTool.Mosaic:
            case AnnotationTool.Blur:
                _panel.Children.Add(ModePill("马赛克", tool == AnnotationTool.Mosaic, () => _editor.SetTool(AnnotationTool.Mosaic)));
                _panel.Children.Add(ModePill("模糊", tool == AnnotationTool.Blur, () => _editor.SetTool(AnnotationTool.Blur)));
                _panel.Children.Add(UiFactory.Separator());
                if (tool == AnnotationTool.Mosaic)
                {
                    BuildOptionPills(
                        [("细", 8), ("中", 12), ("粗", 16)],
                        v => v == style.MosaicBlockPx,
                        v => _editor.UpdateStyle(s => s with { MosaicBlockPx = v }));
                }
                else
                {
                    BuildOptionPills(
                        [("弱", 6), ("中", 12), ("强", 20)],
                        v => v == style.BlurRadius,
                        v => _editor.UpdateStyle(s => s with { BlurRadius = v }));
                }
                _panel.Children.Add(UiFactory.Separator());
                BuildCornerRadiusPills(style.CornerRadius);
                break;

            case AnnotationTool.Spotlight:
                BuildCornerRadiusPills(style.CornerRadius);
                break;
        }
    }

    private void BuildCornerRadiusPills(double currentRadius) =>
        BuildOptionPills(
            [("直角", 0d), ("圆角", 10d), ("大圆角", 22d)],
            v => Math.Abs(v - currentRadius) < 1.5,
            v => _editor.UpdateStyle(s => s with { CornerRadius = v }));

    private void BuildColorSwatches(uint currentArgb)
    {
        for (int i = 0; i < Tokens.AnnotationPalette.Length; i++)
        {
            var c = Tokens.AnnotationPalette[i];
            uint argb = ((uint)0xFF << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
            bool active = (currentArgb & 0x00FFFFFFu) == (argb & 0x00FFFFFFu);

            var dot = new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = new SolidColorBrush(c),
                Stroke = new SolidColorBrush(c == Colors.White ? Color.FromRgb(0x66, 0x6E, 0x7A) : Color.FromArgb(40, 255, 255, 255)),
                StrokeThickness = 1,
            };

            var btn = new Button
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(1.5, 0, 1.5, 0),
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderBrush = active ? Brushes.White : Brushes.Transparent,
                BorderThickness = new Thickness(active ? 1.5 : 0),
                Content = dot,
                Cursor = System.Windows.Input.Cursors.Hand,
                Style = ThemeLoader.Find<Style>("Pp.ToolButton"),
                ToolTip = UiFactory.BuildTooltip($"颜色 {i + 1}", $"{i + 1}"),
            };
            int captured = i;
            btn.Click += (_, _) => _editor.SetColorIndex(captured);
            _panel.Children.Add(btn);
        }
    }

    private void BuildLineWidthPills(double current)
    {
        double[] widths = [2, 3, 5];
        foreach (double w in widths)
        {
            bool active = Math.Abs(current - w) < 0.6;
            double dotSize = 4 + w * 1.4;
            var dot = new Ellipse
            {
                Width = dotSize,
                Height = dotSize,
                Fill = new SolidColorBrush(active ? Colors.White : Tokens.TextSecondary),
            };
            var btn = new Button
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(1, 0, 1, 0),
                Padding = new Thickness(0),
                Content = dot,
                Style = ThemeLoader.Find<Style>("Pp.ToolButton"),
                Background = active ? ThemeLoader.Brush("Pp.AccentSoft") : Brushes.Transparent,
                ToolTip = UiFactory.BuildTooltip($"线宽 {w:0}px", "[ / ]"),
            };
            double captured = w;
            btn.Click += (_, _) => _editor.UpdateStyle(s => s with { LineWidth = captured });
            _panel.Children.Add(btn);
        }
    }

    private void BuildOptionPills<T>(
        (string Label, T Value)[] options,
        Func<T, bool> isSelected,
        Action<T> onSelect)
    {
        foreach (var (label, val) in options)
        {
            bool active = isSelected(val);
            var btn = ModePill(label, active, () => onSelect(val));
            _panel.Children.Add(btn);
        }
    }

    private static Button TogglePill(string label, bool active, Action<bool> onToggle, bool bold = false) =>
        ModePill(label, active, () => onToggle(!active), bold);

    private static Button ModePill(string label, bool active, Action onClick, bool bold = false)
    {
        var btn = new Button
        {
            Height = 26,
            MinWidth = 28,
            Padding = new Thickness(7, 0, 7, 0),
            Margin = new Thickness(1, 0, 1, 0),
            Content = new TextBlock
            {
                Text = label,
                FontSize = 11.5,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = new SolidColorBrush(active ? Colors.White : Tokens.TextPrimary),
            },
            Style = ThemeLoader.Find<Style>("Pp.ToolButton"),
            Background = active ? ThemeLoader.Brush("Pp.Accent") : Brushes.Transparent,
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }
}
