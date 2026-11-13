using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Ppinpix.Capture.Interop;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Geometry;
using Ppinpix.Theme;

namespace Ppinpix.Capture.Overlay;

/// <summary>
/// 像素放大镜：光标周围 15×15 物理像素放大若干倍，带像素网格、中心像素标记与取色。
/// 窗口本身鼠标穿透（WS_EX_TRANSPARENT），永远不会挡住选区操作。
/// </summary>
internal sealed class MagnifierWindow : Window
{
    /// <summary>取样半径（像素），窗口内共显示 (2 * Radius + 1)² 个像素。</summary>
    public const int Radius = 7;

    private const int SampleCount = Radius * 2 + 1;
    private const double InfoRowHeight = 18;
    private const double HintRowHeight = 16;
    private const double PanelWidth = 160;
    private const double PanelPadding = 4;

    private readonly double _imageSizeDip;
    private readonly WriteableBitmap _bitmap;
    private readonly Path _grid;
    private readonly SolidColorBrush _colorSwatchBrush;
    private readonly Rectangle _colorSwatch;
    private readonly TextBlock _hexText;
    private readonly TextBlock _coordText;
    private readonly byte[] _pixels = new byte[SampleCount * SampleCount * 4];

    private double _dpiScale = 1.0;
    private PxPoint _lastLocation = new(int.MinValue, int.MinValue);

    public MagnifierWindow(int zoom)
    {
        int zoomFactor = Math.Clamp(zoom, 2, 16);
        _imageSizeDip = SampleCount * zoomFactor;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Width = PanelWidth;
        Height = (1 + PanelPadding + _imageSizeDip + 4 + InfoRowHeight + 2 + HintRowHeight + PanelPadding + 1);
        Title = "Ppinpix 放大镜";

        _bitmap = new WriteableBitmap(SampleCount, SampleCount, 96, 96, PixelFormats.Bgra32, null);

        var image = new Image
        {
            Source = _bitmap,
            Width = _imageSizeDip,
            Height = _imageSizeDip,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);

        _grid = new Path
        {
            Stroke = new SolidColorBrush(Tokens.GridLine),
            StrokeThickness = 1,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };

        // 中心像素标记：外白内黑的双层方框，任何底色下都看得见。
        double cell = _imageSizeDip / SampleCount;
        double centerOrigin = Math.Round(Radius * cell);
        var centerCanvas = new Canvas
        {
            Width = _imageSizeDip,
            Height = _imageSizeDip,
            IsHitTestVisible = false,
        };
        var centerOuter = new Rectangle
        {
            Width = cell,
            Height = cell,
            Stroke = Brushes.White,
            StrokeThickness = 1,
        };
        var centerInner = new Rectangle
        {
            Width = Math.Max(1, cell - 2),
            Height = Math.Max(1, cell - 2),
            Stroke = Brushes.Black,
            StrokeThickness = 1,
        };
        Canvas.SetLeft(centerOuter, centerOrigin);
        Canvas.SetTop(centerOuter, centerOrigin);
        Canvas.SetLeft(centerInner, centerOrigin + 1);
        Canvas.SetTop(centerInner, centerOrigin + 1);
        centerCanvas.Children.Add(centerOuter);
        centerCanvas.Children.Add(centerInner);

        var imageHost = new Grid { Width = _imageSizeDip, Height = _imageSizeDip };
        imageHost.Children.Add(image);
        imageHost.Children.Add(_grid);
        imageHost.Children.Add(centerCanvas);

        _colorSwatchBrush = new SolidColorBrush(Colors.Transparent);
        _colorSwatch = new Rectangle
        {
            Width = 12,
            Height = 12,
            Fill = _colorSwatchBrush,
            Stroke = ThemeLoader.Brush("Pp.Stroke"),
            StrokeThickness = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _hexText = new TextBlock { Style = ThemeLoader.Find<Style>("Pp.MonoText") };
        _coordText = new TextBlock { Style = ThemeLoader.Find<Style>("Pp.MonoText") };

        double contentWidth = PanelWidth - (PanelPadding + 1) * 2;
        var infoRow = new Grid { Height = InfoRowHeight, Width = contentWidth };
        infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        infoRow.Margin = new Thickness(0, 4, 0, 0);

        Grid.SetColumn(_colorSwatch, 0);
        Grid.SetColumn(_hexText, 1);
        Grid.SetColumn(_coordText, 2);
        _hexText.Margin = new Thickness(Tokens.Spacing1, 0, 0, 0);
        _coordText.HorizontalAlignment = HorizontalAlignment.Right;
        infoRow.Children.Add(_colorSwatch);
        infoRow.Children.Add(_hexText);
        infoRow.Children.Add(_coordText);

        var hint = new TextBlock
        {
            Style = ThemeLoader.Find<Style>("Pp.CaptionText"),
            Height = HintRowHeight,
            Width = contentWidth,
            Text = "C 复制颜色   Shift 切换 HEX/RGB",
            Margin = new Thickness(0, 2, 0, 0),
        };

        var stack = new StackPanel();
        stack.Children.Add(imageHost);
        stack.Children.Add(infoRow);
        stack.Children.Add(hint);

        var panel = new Border
        {
            Style = ThemeLoader.Find<Style>("Pp.ToolbarPanel"),
            Padding = new Thickness(PanelPadding),
            Child = stack,
        };

        Content = panel;

        Loaded += (_, _) =>
        {
            WindowChrome.MakeClickThrough(this);
            RebuildGrid();
        };

        SizeChanged += (_, _) => RebuildGrid();
    }

    /// <summary>取色显示格式：false = HEX，true = rgb()。</summary>
    public bool ShowRgbFormat { get; set; }

    /// <summary>当前取到的颜色，供 C 键复制。</summary>
    public (byte R, byte G, byte B) CurrentColor { get; private set; }

    public void SetVisible(bool visible)
    {
        if (visible)
        {
            if (!IsVisible) Show();
            WindowChrome.BringToTopWithoutActivation(this);
        }
        else if (IsVisible)
        {
            _lastLocation = new PxPoint(int.MinValue, int.MinValue);
            Hide();
        }
    }

    /// <summary>根据光标位置刷新放大内容与取色信息。</summary>
    public void Update(ScreenFrame frame, PxPoint cursor, PxRect monitorBounds)
    {
        var span = _pixels.AsSpan();
        int index = 0;

        for (int dy = -Radius; dy <= Radius; dy++)
        {
            for (int dx = -Radius; dx <= Radius; dx++)
            {
                frame.GetPixel(cursor.X + dx, cursor.Y + dy, out byte b, out byte g, out byte r, out _);
                span[index++] = b;
                span[index++] = g;
                span[index++] = r;
                span[index++] = 255;
            }
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, SampleCount, SampleCount), _pixels, SampleCount * 4, 0);

        frame.GetPixel(cursor.X, cursor.Y, out byte cb, out byte cg, out byte cr, out _);
        CurrentColor = (cr, cg, cb);

        var swatchColor = Color.FromRgb(cr, cg, cb);
        if (_colorSwatchBrush.Color != swatchColor)
        {
            _colorSwatchBrush.Color = swatchColor;
        }

        string hex = ShowRgbFormat ? $"rgb({cr},{cg},{cb})" : $"#{cr:X2}{cg:X2}{cb:X2}";
        if (!string.Equals(_hexText.Text, hex, StringComparison.Ordinal))
        {
            _hexText.Text = hex;
        }

        string coord = $"{cursor.X},{cursor.Y}";
        if (!string.Equals(_coordText.Text, coord, StringComparison.Ordinal))
        {
            _coordText.Text = coord;
        }

        Place(cursor, monitorBounds);
    }

    /// <summary>把窗口摆到光标右下 20 DIP 处，贴边时翻到另一侧。</summary>
    private void Place(PxPoint cursor, PxRect monitorBounds)
    {
        double scale = _dpiScale <= 0 ? 1.0 : _dpiScale;
        int widthPx = (int)Math.Ceiling(ActualWidth * scale);
        int heightPx = (int)Math.Ceiling(ActualHeight * scale);
        if (widthPx <= 0 || heightPx <= 0) return;

        int offset = (int)Math.Round(20 * scale);
        int x = cursor.X + offset;
        int y = cursor.Y + offset;

        if (x + widthPx > monitorBounds.Right) x = cursor.X - offset - widthPx;
        if (y + heightPx > monitorBounds.Bottom) y = cursor.Y - offset - heightPx;

        x = Math.Clamp(x, monitorBounds.Left, Math.Max(monitorBounds.Left, monitorBounds.Right - widthPx));
        y = Math.Clamp(y, monitorBounds.Top, Math.Max(monitorBounds.Top, monitorBounds.Bottom - heightPx));

        var location = new PxPoint(x, y);
        if (location == _lastLocation) return;
        _lastLocation = location;
        WindowChrome.MovePhysical(this, location, preserveZOrder: true);
    }

    /// <summary>按当前显示器缩放重建像素网格线，保证网格是 1 物理像素。</summary>
    private void RebuildGrid()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        _dpiScale = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;

        double cell = _imageSizeDip / SampleCount;
        var group = new GeometryGroup();
        for (int i = 1; i < SampleCount; i++)
        {
            double offset = Math.Round(i * cell);
            group.Children.Add(new LineGeometry(new Point(offset, 0), new Point(offset, _imageSizeDip)));
            group.Children.Add(new LineGeometry(new Point(0, offset), new Point(_imageSizeDip, offset)));
        }

        _grid.Data = group;
        _grid.StrokeThickness = 1.0 / _dpiScale;
    }
}
