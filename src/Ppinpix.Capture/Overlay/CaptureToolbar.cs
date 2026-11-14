using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Ppinpix.Capture.Annotation;
using Ppinpix.Capture.Interop;
using Ppinpix.Capture.Session;
using Ppinpix.Core;
using Ppinpix.Core.Display;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Settings;
using Ppinpix.Theme;

namespace Ppinpix.Capture.Overlay;

/// <summary>
/// 截图工具栏。按钮随 <see cref="Features"/> 开关增减，未实现的模块不会出现死按钮。
/// 窗口不可激活（WS_EX_NOACTIVATE），点按钮不会把键盘焦点从覆盖层抢走，Esc/Enter 始终有效。
/// </summary>
internal sealed class CaptureToolbar : Window
{
    /// <summary>工具栏与选区之间的间距（DIP）。</summary>
    private const double GapDip = 8;

    private readonly ICaptureToolbarActions _actions;
    private readonly Grid _root;
    private readonly Dictionary<AnnotationTool, Button> _toolButtons = [];
    private readonly Button? _undoButton;
    private readonly Button? _redoButton;

    private double _dpiScale = 1.0;

    public PxRect PlacedBounds { get; private set; }

    public CaptureToolbar(
        ICaptureToolbarActions actions,
        bool annotationOnly = false,
        ToolbarVisibilitySettings? visibility = null)
    {
        _actions = actions;
        var vis = visibility ?? new ToolbarVisibilitySettings();

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "Ppinpix 截图工具栏";

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (Features.Annotate || annotationOnly)
        {
            if (vis.ShowSelect) AddTool(buttons, AnnotationTool.Select, "选择 / 移动标注", "V", BuildPointerVector());
            if (vis.ShowRectangle) AddTool(buttons, AnnotationTool.Rectangle, "矩形", "R", BuildRectVector());
            if (vis.ShowEllipse) AddTool(buttons, AnnotationTool.Ellipse, "椭圆", "O", BuildEllipseVector());
            if (vis.ShowArrow) AddTool(buttons, AnnotationTool.Arrow, "箭头（拖动中点弯曲，双击还原）", "A", MakeGlyph(Icons.Arrow));
            if (vis.ShowLine) AddTool(buttons, AnnotationTool.Line, "直线", "L", BuildLineVector());
            if (vis.ShowFreehand) AddTool(buttons, AnnotationTool.Freehand, "画笔", "P", MakeGlyph(Icons.Edit));
            if (vis.ShowNumberBadge) AddTool(buttons, AnnotationTool.NumberBadge, "序号标记（按住拖出指针箭头）", "N", BuildBadgeVector());
            if (vis.ShowText) AddTool(buttons, AnnotationTool.Text, "文字标注", "T", BuildTextVector());
            if (vis.ShowMosaic) AddTool(buttons, AnnotationTool.Mosaic, "马赛克 / 模糊", "M", BuildMosaicVector());
            if (vis.ShowSpotlight) AddTool(buttons, AnnotationTool.Spotlight, "聚光灯", "K", BuildSpotlightVector());
            if (vis.ShowHighlight) AddTool(buttons, AnnotationTool.Highlight, "荧光笔", "H", MakeGlyph(Icons.Highlight));
            if (vis.ShowEraser) AddTool(buttons, AnnotationTool.Eraser, "橡皮擦", "E", MakeGlyph(Icons.Eraser));

            if (vis.ShowUndoRedo)
            {
                AddSeparatorIfNeeded(buttons);
                _undoButton = UiFactory.IconButton(Icons.Undo, "撤销", () => _actions.Undo(), shortcut: "Ctrl+Z");
                _redoButton = UiFactory.IconButton(Icons.Redo, "重做", () => _actions.Redo(), shortcut: "Ctrl+Y");
                buttons.Children.Add(_undoButton);
                buttons.Children.Add(_redoButton);
            }

            AddSeparatorIfNeeded(buttons);
        }

        if (!annotationOnly)
        {
            if (vis.ShowCopy) buttons.Children.Add(UiFactory.IconButton(Icons.Copy, "复制到剪贴板", () => _actions.Copy(), shortcut: "Ctrl+C"));
            if (vis.ShowSave) buttons.Children.Add(UiFactory.IconButton(Icons.Save, "保存图片", () => _actions.Save(), shortcut: "Ctrl+S"));
            if (vis.ShowLongshot)
            {
                var longshotBtn = new Button
                {
                    Style = ThemeLoader.Find<Style>("Pp.ToolButton"),
                    Content = BuildLongshotVector(),
                    ToolTip = UiFactory.BuildTooltip("滚动长截图", "Ctrl+L"),
                };
                longshotBtn.Click += (_, _) => _actions.Longshot();
                buttons.Children.Add(longshotBtn);
            }

            if (Features.Pin && vis.ShowPin)
            {
                buttons.Children.Add(UiFactory.IconButton(Icons.Pin, "贴到屏幕", () => _actions.Pin(), shortcut: "Ctrl+T"));
            }

            if (Features.Ocr && vis.ShowOcr)
            {
                buttons.Children.Add(UiFactory.IconButton(Icons.Document, "提取文字", () => _actions.Ocr(), shortcut: "Ctrl+O"));
            }

            if (Features.Translate && vis.ShowTranslate)
            {
                buttons.Children.Add(UiFactory.IconButton(Icons.Globe, "翻译截图", () => _actions.Translate(), shortcut: "Ctrl+Shift+T"));
            }

            AddSeparatorIfNeeded(buttons);
        }

        buttons.Children.Add(UiFactory.IconButton(Icons.Close, "取消", () => _actions.Cancel(), shortcut: "Esc"));
        buttons.Children.Add(UiFactory.IconButton(Icons.Accept, annotationOnly ? "应用标注" : "完成（复制并关闭）", () => _actions.Accept(), accent: true, shortcut: "Enter"));

        _root = new Grid { Margin = new Thickness(Tokens.ShadowDepth) };
        _root.Children.Add(new Border
        {
            Style = ThemeLoader.Find<Style>("Pp.ToolbarPanel"),
            Padding = new Thickness(4),
            Child = buttons,
        });

        Content = _root;

        Loaded += (_, _) => WindowChrome.MakeNoActivate(this);
        SizeChanged += (_, _) =>
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            _dpiScale = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
        };

        SetToolState(AnnotationTool.None, canUndo: false, canRedo: false);
    }

    public void SetToolState(AnnotationTool activeTool, bool canUndo, bool canRedo)
    {
        foreach (var (tool, btn) in _toolButtons)
        {
            bool isActive = tool == activeTool ||
                (tool == AnnotationTool.Mosaic && activeTool == AnnotationTool.Blur);
            btn.Background = isActive ? ThemeLoader.Brush("Pp.AccentSoft") : Brushes.Transparent;
            btn.BorderBrush = isActive ? ThemeLoader.Brush("Pp.Accent") : Brushes.Transparent;
            btn.BorderThickness = new Thickness(1);
        }

        if (_undoButton is not null)
        {
            _undoButton.IsEnabled = canUndo;
            _undoButton.Opacity = canUndo ? 1.0 : 0.35;
        }
        if (_redoButton is not null)
        {
            _redoButton.IsEnabled = canRedo;
            _redoButton.Opacity = canRedo ? 1.0 : 0.35;
        }
    }

    /// <summary>按选区位置摆放工具栏。默认在选区右下角外侧下方，空间不够时改到上方或内部。</summary>
    public void Position(PxRect selection, MonitorInfo monitor)
    {
        UpdateLayout();
        double scale = _dpiScale <= 0 ? 1.0 : _dpiScale;
        int widthPx = (int)Math.Ceiling(ActualWidth * scale);
        int heightPx = (int)Math.Ceiling(ActualHeight * scale);
        if (widthPx <= 0 || heightPx <= 0) return;

        int gap = (int)Math.Round(GapDip * scale);
        var work = monitor.WorkArea;

        // 默认：右对齐选区右边缘，放在选区下方。
        int x = selection.Right - widthPx;
        int y = selection.Bottom + gap;

        if (y + heightPx > work.Bottom)
        {
            // 下方放不下 → 放到选区上方。
            y = selection.Top - gap - heightPx;

            if (y < work.Top)
            {
                // 上方也放不下 → 放进选区内部右下角。
                y = Math.Min(selection.Bottom - heightPx - gap, work.Bottom - heightPx);
                y = Math.Max(y, work.Top + gap);
            }
        }

        x = Math.Clamp(x, work.Left + gap, Math.Max(work.Left + gap, work.Right - widthPx - gap));
        y = Math.Clamp(y, work.Top + gap, Math.Max(work.Top + gap, work.Bottom - heightPx - gap));

        PlacedBounds = new PxRect(x, y, widthPx, heightPx);
        WindowChrome.MovePhysical(this, new PxPoint(x, y));
        WindowChrome.BringToTopWithoutActivation(this);
    }

    public void ShowToolbar()
    {
        Show();
        WindowChrome.BringToTopWithoutActivation(this);
    }

    public void HideToolbar() => Hide();

    private void AddTool(StackPanel panel, AnnotationTool tool, string tooltip, string shortcut, UIElement icon)
    {
        var btn = new Button
        {
            Style = ThemeLoader.Find<Style>("Pp.ToolButton"),
            Content = icon,
            ToolTip = UiFactory.BuildTooltip(tooltip, shortcut),
        };
        btn.Click += (_, _) => _actions.SelectTool(tool);
        _toolButtons[tool] = btn;
        panel.Children.Add(btn);
    }

    private static UIElement MakeGlyph(string glyph) => new TextBlock
    {
        Text = glyph,
        FontFamily = ThemeLoader.Find<FontFamily>("Pp.IconFont"),
        FontSize = 15,
        Foreground = ThemeLoader.Brush("Pp.TextPrimary"),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static UIElement BuildPointerVector() => new Path
    {
        Width = 14,
        Height = 14,
        Stretch = Stretch.Uniform,
        Stroke = ThemeLoader.Brush("Pp.TextPrimary"),
        StrokeThickness = 1.4,
        Data = Geometry.Parse("M2,1 L2,12 L5.5,9 L8,13.5 L9.8,12.5 L7.2,8 L11.5,7.5 Z"),
    };

    private static UIElement BuildRectVector() => new Rectangle
    {
        Width = 13,
        Height = 10,
        Stroke = ThemeLoader.Brush("Pp.TextPrimary"),
        StrokeThickness = 1.6,
        RadiusX = 1.2,
        RadiusY = 1.2,
    };

    private static UIElement BuildEllipseVector() => new Ellipse
    {
        Width = 13,
        Height = 11,
        Stroke = ThemeLoader.Brush("Pp.TextPrimary"),
        StrokeThickness = 1.6,
    };

    private static UIElement BuildLineVector() => new Line
    {
        X1 = 1,
        Y1 = 12,
        X2 = 13,
        Y2 = 2,
        Width = 14,
        Height = 14,
        Stroke = ThemeLoader.Brush("Pp.TextPrimary"),
        StrokeThickness = 1.7,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };

    private static UIElement BuildBadgeVector()
    {
        var grid = new Grid { Width = 15, Height = 15 };
        grid.Children.Add(new Ellipse
        {
            Stroke = ThemeLoader.Brush("Pp.TextPrimary"),
            StrokeThickness = 1.5,
        });
        grid.Children.Add(new TextBlock
        {
            Text = "1",
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = ThemeLoader.Brush("Pp.TextPrimary"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return grid;
    }

    private static UIElement BuildTextVector() => new TextBlock
    {
        Text = "T",
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        FontFamily = new FontFamily("Georgia, Segoe UI"),
        Foreground = ThemeLoader.Brush("Pp.TextPrimary"),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static UIElement BuildMosaicVector()
    {
        var grid = new Grid { Width = 12, Height = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());

        var strong = ThemeLoader.Brush("Pp.TextPrimary");
        var soft = ThemeLoader.Brush("Pp.TextSecondary");

        AddCell(0, 0, strong);
        AddCell(1, 0, soft);
        AddCell(0, 1, soft);
        AddCell(1, 1, strong);
        return grid;

        void AddCell(int c, int r, Brush fill)
        {
            var rect = new Rectangle { Fill = fill, Opacity = ReferenceEquals(fill, strong) ? 0.95 : 0.35 };
            Grid.SetColumn(rect, c);
            Grid.SetRow(rect, r);
            grid.Children.Add(rect);
        }
    }

    private static void AddSeparatorIfNeeded(StackPanel panel)
    {
        if (panel.Children.Count == 0 || panel.Children[^1] is Border) return;
        panel.Children.Add(UiFactory.Separator());
    }

    private static UIElement BuildSpotlightVector()
    {
        var grid = new Grid { Width = 14, Height = 14 };
        grid.Children.Add(new Rectangle
        {
            Width = 14,
            Height = 14,
            RadiusX = 2.5,
            RadiusY = 2.5,
            Stroke = ThemeLoader.Brush("Pp.TextSecondary"),
            StrokeThickness = 1.2,
            Fill = Brushes.Transparent,
        });
        grid.Children.Add(new Ellipse
        {
            Width = 6.5,
            Height = 6.5,
            Fill = ThemeLoader.Brush("Pp.TextPrimary"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return grid;
    }

    private static UIElement BuildLongshotVector() => new Path
    {
        Width = 14,
        Height = 15,
        Stretch = Stretch.Uniform,
        Stroke = ThemeLoader.Brush("Pp.TextPrimary"),
        StrokeThickness = 1.45,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        Data = Geometry.Parse("M2,1.5 L12,1.5 M2,13.5 L12,13.5 M7,3.5 L7,11.5 M4.5,9 L7,11.5 L9.5,9"),
    };
}

/// <summary>工具栏按钮回调。</summary>
internal interface ICaptureToolbarActions
{
    void SelectTool(AnnotationTool tool);

    void Undo();

    void Redo();

    void Copy();

    void Save();

    void Longshot();

    void Pin();

    void Ocr();

    void Translate();

    void Cancel();

    void Accept();
}
