using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ppinpix.Capture.Annotation.Shapes;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Geometry;
using Ppinpix.Theme;
using SkiaSharp;

namespace Ppinpix.Capture.Annotation;

/// <summary>
/// 承载所有矢量与遮挡标注的 WPF 渲染层，并提供文字标注的就地悬浮 TextBox。
/// 马赛克与高斯模糊复用 Skia 块生成结果以保证预览与最终导出的 1:1 一致性。
/// </summary>
public sealed class AnnotationLayer : Canvas
{
    private readonly AnnotationEditor _editor;
    private readonly double _scale;
    private readonly Dictionary<Guid, (nint PixelsPtr, BitmapSource Bmp)> _patchCache = [];

    private ScreenFrame _sourceFrame;
    private PxRect _canvasBounds;
    private PxRect _clipRectPx = PxRect.Empty;

    private TextBox? _inlineBox;
    private TextShape? _editingShape;
    private bool _editingIsNew;
    private string _editingOriginalText = string.Empty;
    private ShapeStyle _editingOriginalStyle = new();
    private bool _committingText;

    public AnnotationLayer(AnnotationEditor editor, ScreenFrame sourceFrame, PxRect canvasBounds, double scale)
    {
        _editor = editor;
        _sourceFrame = sourceFrame;
        _canvasBounds = canvasBounds;
        _scale = scale <= 0 ? 1.0 : scale;

        IsHitTestVisible = true;
        Background = null;

        _editor.StateChanged += OnEditorStateChanged;
        Unloaded += (_, _) =>
        {
            _editor.StateChanged -= OnEditorStateChanged;
            _patchCache.Clear();
        };
    }

    public bool IsEditingText => _inlineBox is not null;

    public void UpdateSource(ScreenFrame sourceFrame, PxRect canvasBounds)
    {
        _sourceFrame = sourceFrame;
        _canvasBounds = canvasBounds;
        _patchCache.Clear();
        InvalidateVisual();
    }

    public void SetClipRect(PxRect clipRectPx)
    {
        if (_clipRectPx == clipRectPx) return;
        _clipRectPx = clipRectPx;
        InvalidateVisual();
    }

    public bool CommitActiveTextEdit()
    {
        if (_inlineBox is null || _editingShape is null || _committingText) return false;
        _committingText = true;
        try
        {
            var box = _inlineBox;
            var shape = _editingShape;
            bool isNew = _editingIsNew;
            string origText = _editingOriginalText;
            var origStyle = _editingOriginalStyle;
            string currentText = box.Text;

            Children.Remove(box);
            _inlineBox = null;
            _editingShape = null;

            _editor.CommitTextEdit(shape, isNew, origText, origStyle, currentText);
            return true;
        }
        finally
        {
            _committingText = false;
            InvalidateVisual();
        }
    }

    public void CancelActiveTextEdit()
    {
        if (_inlineBox is null || _editingShape is null || _committingText) return;
        _committingText = true;
        try
        {
            var box = _inlineBox;
            var shape = _editingShape;
            bool isNew = _editingIsNew;

            Children.Remove(box);
            _inlineBox = null;
            _editingShape = null;

            shape.IsEditing = false;
            if (!isNew)
            {
                shape.Text = _editingOriginalText;
                shape.Style = _editingOriginalStyle;
            }
            InvalidateVisual();
        }
        finally
        {
            _committingText = false;
        }
    }

    private void OnEditorStateChanged()
    {
        if (_inlineBox is not null && _editingShape is not null)
        {
            var style = _editor.CurrentStyle;
            _editingShape.Style = style;
            ApplyInlineStyle(_inlineBox, style);
        }
        InvalidateVisual();
    }

    public void OpenTextEditor(TextEditRequest req)
    {
        CommitActiveTextEdit();

        _editingShape = req.Shape;
        _editingIsNew = req.IsNew;
        _editingOriginalText = req.Shape.Text;
        _editingOriginalStyle = req.Shape.Style;
        req.Shape.IsEditing = true;

        var box = new TextBox
        {
            Text = req.Shape.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinWidth = 48,
            MinHeight = 28,
            Padding = new Thickness(6, 3, 6, 3),
            BorderBrush = new SolidColorBrush(Tokens.Accent),
            BorderThickness = new Thickness(1),
            CaretBrush = new SolidColorBrush(Tokens.TextPrimary),
            SelectionBrush = new SolidColorBrush(Color.FromArgb(110, Tokens.Accent.R, Tokens.Accent.G, Tokens.Accent.B)),
        };
        ApplyInlineStyle(box, req.Shape.Style);

        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelActiveTextEdit();
                FocusParentWindow();
            }
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                e.Handled = true;
                CommitActiveTextEdit();
                FocusParentWindow();
            }
        };
        box.LostKeyboardFocus += (_, _) =>
        {
            // 若是点击同一套工具栏或属性栏（WS_EX_NOACTIVATE）则通常不会失焦；外部失焦即提交
            if (!_committingText && _inlineBox == box)
            {
                CommitActiveTextEdit();
            }
        };

        _inlineBox = box;
        SetLeft(box, Math.Max(0, (req.Shape.X - _canvasBounds.X) / _scale));
        SetTop(box, Math.Max(0, (req.Shape.Y - _canvasBounds.Y) / _scale));
        Children.Add(box);

        Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            box.CaretIndex = box.Text.Length;
        });

        InvalidateVisual();
    }

    private void ApplyInlineStyle(TextBox box, ShapeStyle style)
    {
        var fg = ToColor(style.Argb);
        box.Foreground = new SolidColorBrush(fg);
        box.Background = style.TextBackground
            ? new SolidColorBrush(GeometryHelper.IsLightColor(style.Argb)
                ? Color.FromArgb(215, 27, 31, 36)
                : Color.FromArgb(228, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(170, 20, 24, 30));
        box.FontFamily = new FontFamily(style.FontFamily);
        box.FontSize = Math.Max(8, style.FontSize / _scale);
        box.FontWeight = style.Bold ? FontWeights.Bold : FontWeights.Normal;
    }

    private void FocusParentWindow() => Window.GetWindow(this)?.Focus();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        bool hasClip = !_clipRectPx.IsEmpty;
        Rect clipDip = hasClip
            ? ToDipRect(_clipRectPx.Left, _clipRectPx.Top, _clipRectPx.Width, _clipRectPx.Height)
            : new Rect(0, 0,
                ActualWidth > 0 ? ActualWidth : _sourceFrame.Width / _scale,
                ActualHeight > 0 ? ActualHeight : _sourceFrame.Height / _scale);

        if (hasClip)
        {
            dc.PushClip(new RectangleGeometry(clipDip));
        }

        using var renderCtx = new AnnotationRenderContext(_sourceFrame);

        IEnumerable<IShape> allVisible = _editor.DraftShape is { } draft
            ? _editor.Shapes.Append(draft)
            : _editor.Shapes;
        var visibleList = allVisible.ToList();

        // 1. 马赛克与模糊底层
        foreach (var shape in visibleList)
        {
            if (shape is MosaicShape or BlurShape)
            {
                DrawShape(dc, shape, renderCtx);
            }
        }

        // 2. 聚光灯暗色遮罩（合并所有有效聚光灯为并集，保留聚光区域原始亮度）
        var spotlights = visibleList.OfType<SpotlightShape>().Where(s => s.IsValid).ToList();
        if (spotlights.Count > 0)
        {
            DrawSpotlightMask(dc, clipDip, spotlights);
        }

        // 3. 其余矢量标注与文字
        foreach (var shape in visibleList)
        {
            if (shape is MosaicShape or BlurShape or SpotlightShape) continue;
            DrawShape(dc, shape, renderCtx);
        }

        if (hasClip)
        {
            dc.Pop();
        }

        // 选中的标注控制点画在裁剪层之外，便于在边缘处精准拖拽
        if (_editor.SelectedShape is { } selected && (! (selected is TextShape ts && ts.IsEditing)))
        {
            DrawSelectionChrome(dc, selected);
        }
    }

    private void DrawShape(DrawingContext dc, IShape shape, AnnotationRenderContext renderCtx)
    {
        if (!shape.IsValid && shape != _editor.DraftShape) return;

        switch (shape)
        {
            case RectangleShape rect when rect.IsValid:
                DrawRectangle(dc, rect);
                break;
            case EllipseShape ell when ell.IsValid:
                DrawEllipse(dc, ell);
                break;
            case LineShape line when line.IsValid:
                DrawLine(dc, line);
                break;
            case ArrowShape arrow when arrow.IsValid:
                DrawArrow(dc, arrow);
                break;
            case FreehandShape free when free.IsValid:
                DrawFreehand(dc, free);
                break;
            case HighlightShape hi when hi.IsValid:
                DrawHighlight(dc, hi);
                break;
            case NumberBadgeShape badge:
                DrawNumberBadge(dc, badge);
                break;
            case TextShape text when text.IsValid && !text.IsEditing:
                DrawText(dc, text);
                break;
            case MosaicShape mosaic when mosaic.IsValid:
                var mPatch = mosaic.GetOrBuildPatch(_sourceFrame, out var mRegion);
                if (mPatch is not null) DrawPatch(dc, mosaic.Id, mPatch, mRegion, mosaic.Rect, mosaic.Style.CornerRadius);
                break;
            case BlurShape blur when blur.IsValid:
                var bPatch = blur.GetOrBuildPatch(renderCtx, out var bRegion);
                if (bPatch is not null) DrawPatch(dc, blur.Id, bPatch, bRegion, blur.Rect, blur.Style.CornerRadius);
                break;
        }
    }

    private void DrawSpotlightMask(DrawingContext dc, Rect fullDip, IReadOnlyList<SpotlightShape> spotlights)
    {
        Geometry? union = null;
        foreach (var spot in spotlights)
        {
            var dip = ToDipRect(spot.Rect.X, spot.Rect.Y, spot.Rect.Width, spot.Rect.Height);
            double rDip = spot.ClampedCornerRadius / _scale;
            var geom = rDip > 0.3
                ? new RectangleGeometry(dip, rDip, rDip)
                : new RectangleGeometry(dip);
            union = union is null ? geom : Geometry.Combine(union, geom, GeometryCombineMode.Union, null);
        }

        if (union is null) return;

        var masked = Geometry.Combine(new RectangleGeometry(fullDip), union, GeometryCombineMode.Exclude, null);
        masked.Freeze();
        var dimBrush = new SolidColorBrush(Color.FromArgb(SpotlightShape.MaskAlpha, 0, 0, 0));
        dimBrush.Freeze();
        dc.DrawGeometry(dimBrush, null, masked);

        var rimBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
        rimBrush.Freeze();
        var rimPen = new Pen(rimBrush, Math.Max(1.0, 1.25 / _scale));
        rimPen.Freeze();
        union.Freeze();
        dc.DrawGeometry(null, rimPen, union);
    }

    private void DrawPatch(DrawingContext dc, Guid id, SKBitmap patch, PxRect region, RectF shapeRect, double cornerRadiusPx)
    {
        nint ptr = patch.GetPixels();
        BitmapSource bmp;
        if (_patchCache.TryGetValue(id, out var cached) && cached.PixelsPtr == ptr &&
            cached.Bmp.PixelWidth == patch.Width && cached.Bmp.PixelHeight == patch.Height)
        {
            bmp = cached.Bmp;
        }
        else
        {
            bmp = BitmapSource.Create(
                patch.Width,
                patch.Height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                ptr,
                patch.ByteCount,
                patch.RowBytes);
            bmp.Freeze();
            _patchCache[id] = (ptr, bmp);
        }

        double rDip = GeometryHelper.ClampCornerRadius(shapeRect, cornerRadiusPx) / _scale;
        if (rDip > 0.3)
        {
            var clipDip = ToDipRect(shapeRect.X, shapeRect.Y, shapeRect.Width, shapeRect.Height);
            dc.PushClip(new RectangleGeometry(clipDip, rDip, rDip));
            dc.DrawImage(bmp, ToDipRect(region.Left, region.Top, region.Width, region.Height));
            dc.Pop();
        }
        else
        {
            dc.DrawImage(bmp, ToDipRect(region.Left, region.Top, region.Width, region.Height));
        }
    }

    private void DrawRectangle(DrawingContext dc, RectangleShape r)
    {
        var dip = ToDipRect(r.Rect.X, r.Rect.Y, r.Rect.Width, r.Rect.Height);
        double rDip = r.ClampedCornerRadius / _scale;
        var color = ToColor(r.Style.Argb);
        if (r.Style.Filled)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            if (rDip > 0.3)
                dc.DrawRoundedRectangle(brush, null, dip, rDip, rDip);
            else
                dc.DrawRectangle(brush, null, dip);
        }
        else
        {
            var pen = MakePen(r.Style, miterJoin: rDip <= 0.3);
            if (rDip > 0.3)
                dc.DrawRoundedRectangle(null, pen, dip, rDip, rDip);
            else
                dc.DrawRectangle(null, pen, dip);
        }
    }

    private void DrawEllipse(DrawingContext dc, EllipseShape e)
    {
        var dip = ToDipRect(e.Rect.X, e.Rect.Y, e.Rect.Width, e.Rect.Height);
        var center = new Point(dip.X + dip.Width * 0.5, dip.Y + dip.Height * 0.5);
        double rx = dip.Width * 0.5;
        double ry = dip.Height * 0.5;

        if (e.Style.Filled)
        {
            var brush = new SolidColorBrush(ToColor(e.Style.Argb));
            brush.Freeze();
            dc.DrawEllipse(brush, null, center, rx, ry);
        }
        else
        {
            dc.DrawEllipse(null, MakePen(e.Style), center, rx, ry);
        }
    }

    private void DrawLine(DrawingContext dc, LineShape l)
    {
        var pen = MakePen(l.Style with { Filled = false });
        dc.DrawLine(pen, ToDipPoint(l.X1, l.Y1), ToDipPoint(l.X2, l.Y2));
    }

    private void DrawArrow(DrawingContext dc, ArrowShape a)
    {
        var g = a.ComputeHeadGeometry();
        var pen = MakePen(a.Style with { Filled = false, Dashed = false });
        if (a.IsCurved)
        {
            var shaftGeom = new StreamGeometry();
            using (var ctx = shaftGeom.Open())
            {
                ctx.BeginFigure(ToDipPoint(a.X1, a.Y1), isFilled: false, isClosed: false);
                ctx.QuadraticBezierTo(
                    ToDipPoint(a.ControlX, a.ControlY),
                    ToDipPoint(g.ShaftEnd.X, g.ShaftEnd.Y),
                    isStroked: true,
                    isSmoothJoin: true);
            }
            shaftGeom.Freeze();
            dc.DrawGeometry(null, pen, shaftGeom);
        }
        else
        {
            dc.DrawLine(pen, ToDipPoint(a.X1, a.Y1), ToDipPoint(g.ShaftEnd.X, g.ShaftEnd.Y));
        }

        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(ToDipPoint(g.Tip.X, g.Tip.Y), isFilled: true, isClosed: true);
            ctx.LineTo(ToDipPoint(g.LeftWing.X, g.LeftWing.Y), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(ToDipPoint(g.Notch.X, g.Notch.Y), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(ToDipPoint(g.RightWing.X, g.RightWing.Y), isStroked: false, isSmoothJoin: false);
        }
        geom.Freeze();

        var brush = new SolidColorBrush(ToColor(a.Style.Argb));
        brush.Freeze();
        dc.DrawGeometry(brush, null, geom);
    }

    private void DrawFreehand(DrawingContext dc, FreehandShape f)
    {
        if (f.Points.Count < 2) return;
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(ToDipPoint(f.Points[0].X, f.Points[0].Y), isFilled: false, isClosed: false);
            if (f.Points.Count == 2)
            {
                ctx.LineTo(ToDipPoint(f.Points[1].X, f.Points[1].Y), isStroked: true, isSmoothJoin: true);
            }
            else
            {
                for (int i = 1; i < f.Points.Count - 1; i++)
                {
                    var ctrl = ToDipPoint(f.Points[i].X, f.Points[i].Y);
                    var mid = ToDipPoint(
                        (f.Points[i].X + f.Points[i + 1].X) * 0.5,
                        (f.Points[i].Y + f.Points[i + 1].Y) * 0.5);
                    ctx.QuadraticBezierTo(ctrl, mid, isStroked: true, isSmoothJoin: true);
                }
                var last = f.Points[^1];
                ctx.LineTo(ToDipPoint(last.X, last.Y), isStroked: true, isSmoothJoin: true);
            }
        }
        geom.Freeze();
        dc.DrawGeometry(null, MakePen(f.Style with { Filled = false, Dashed = false }), geom);
    }

    private void DrawHighlight(DrawingContext dc, HighlightShape h)
    {
        if (h.Points.Count < 2) return;
        var baseColor = ToColor(h.Style.Argb);
        var semiColor = Color.FromArgb(105, baseColor.R, baseColor.G, baseColor.B);
        var brush = new SolidColorBrush(semiColor);
        brush.Freeze();

        var pen = new Pen(brush, Math.Max(4, h.EffectiveStrokeWidth / _scale))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();

        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(ToDipPoint(h.Points[0].X, h.Points[0].Y), isFilled: false, isClosed: false);
            for (int i = 1; i < h.Points.Count; i++)
            {
                ctx.LineTo(ToDipPoint(h.Points[i].X, h.Points[i].Y), isStroked: true, isSmoothJoin: true);
            }
        }
        geom.Freeze();
        dc.DrawGeometry(null, pen, geom);
    }

    private void DrawNumberBadge(DrawingContext dc, NumberBadgeShape b)
    {
        var center = ToDipPoint(b.CenterX, b.CenterY);
        double rDip = b.Radius / _scale;

        var fill = new SolidColorBrush(ToColor(b.Style.Argb));
        fill.Freeze();

        var ringColor = GeometryHelper.IsLightColor(b.Style.Argb)
            ? Color.FromArgb(96, 27, 31, 36)
            : Color.FromArgb(112, 255, 255, 255);
        var ringBrush = new SolidColorBrush(ringColor);
        ringBrush.Freeze();
        var ringPen = new Pen(ringBrush, Math.Max(1.0, 1.25 / _scale))
        {
            LineJoin = PenLineJoin.Round,
        };
        ringPen.Freeze();

        if (b.TryGetTailTriangle(out var bl, out var tip, out var br))
        {
            var circleGeom = new EllipseGeometry(center, rDip, rDip);
            var tailGeom = new StreamGeometry();
            using (var ctx = tailGeom.Open())
            {
                ctx.BeginFigure(ToDipPoint(bl.X, bl.Y), isFilled: true, isClosed: true);
                ctx.LineTo(ToDipPoint(tip.X, tip.Y), isStroked: true, isSmoothJoin: true);
                ctx.LineTo(ToDipPoint(br.X, br.Y), isStroked: true, isSmoothJoin: true);
            }
            var unionGeom = Geometry.Combine(circleGeom, tailGeom, GeometryCombineMode.Union, null);
            unionGeom.Freeze();
            dc.DrawGeometry(fill, ringPen, unionGeom);
        }
        else
        {
            dc.DrawEllipse(fill, ringPen, center, rDip, rDip);
        }

        string label = b.Number.ToString(CultureInfo.InvariantCulture);
        double fontSizeDip = rDip * (label.Length >= 2 ? 0.96 : 1.15);
        var textBrush = new SolidColorBrush(GeometryHelper.IsLightColor(b.Style.Argb)
            ? Color.FromRgb(0x1B, 0x1F, 0x24)
            : Colors.White);
        textBrush.Freeze();

        var ft = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            fontSizeDip,
            textBrush,
            _scale);
        dc.DrawText(ft, new Point(center.X - ft.Width * 0.5, center.Y - ft.Height * 0.5));
    }

    private void DrawText(DrawingContext dc, TextShape t)
    {
        var bounds = t.Bounds;
        var rectDip = ToDipRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);

        if (t.Style.TextBackground)
        {
            var bg = new SolidColorBrush(GeometryHelper.IsLightColor(t.Style.Argb)
                ? Color.FromArgb(204, 27, 31, 36)
                : Color.FromArgb(220, 255, 255, 255));
            bg.Freeze();
            double corner = 4.0 / _scale;
            dc.DrawRoundedRectangle(bg, null, rectDip, corner, corner);
        }

        var brush = new SolidColorBrush(ToColor(t.Style.Argb));
        brush.Freeze();

        var typeface = new Typeface(
            new FontFamily(t.Style.FontFamily),
            FontStyles.Normal,
            t.Style.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);
        double fontSizeDip = t.Style.FontSize / _scale;
        double lineHeightDip = fontSizeDip * 1.3;
        double padXDip = 8.0 / _scale;
        double padYDip = 4.0 / _scale;

        double y = rectDip.Y + padYDip;
        foreach (string line in t.Text.Split('\n'))
        {
            if (line.Length > 0)
            {
                var ft = new FormattedText(
                    line,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSizeDip,
                    brush,
                    _scale);
                dc.DrawText(ft, new Point(rectDip.X + padXDip, y));
            }
            y += lineHeightDip;
        }
    }

    private void DrawSelectionChrome(DrawingContext dc, IShape selected)
    {
        var b = selected.Bounds;
        var rectDip = ToDipRect(b.X - 3, b.Y - 3, b.Width + 6, b.Height + 6);

        var dashPen = new Pen(new SolidColorBrush(Tokens.Accent), 1.2)
        {
            DashStyle = new DashStyle([4, 3], 0),
        };
        dashPen.Freeze();

        if (selected is not LineShape && selected is not ArrowShape)
        {
            dc.DrawRectangle(null, dashPen, rectDip);
        }

        var fill = Brushes.White;
        var stroke = new Pen(new SolidColorBrush(Tokens.Accent), 1.5);
        stroke.Freeze();
        var specialFill = new SolidColorBrush(Tokens.Accent);
        specialFill.Freeze();
        var specialStroke = new Pen(Brushes.White, 1.5);
        specialStroke.Freeze();
        const double r = 4.0;
        foreach (var h in selected.GetHandles())
        {
            bool isSpecial = h.Kind is ShapeHandleKind.CurveControl or ShapeHandleKind.TailEnd or ShapeHandleKind.CornerRadius;
            dc.DrawEllipse(
                isSpecial ? specialFill : fill,
                isSpecial ? specialStroke : stroke,
                ToDipPoint(h.X, h.Y),
                isSpecial ? 4.3 : r,
                isSpecial ? 4.3 : r);
        }
    }

    private Pen MakePen(ShapeStyle style, bool miterJoin = false)
    {
        var brush = new SolidColorBrush(ToColor(style.Argb));
        brush.Freeze();
        double strokeDip = Math.Max(0.75, style.LineWidth / _scale);
        var pen = new Pen(brush, strokeDip)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = miterJoin ? PenLineJoin.Miter : PenLineJoin.Round,
        };
        if (style.Dashed)
        {
            pen.DashStyle = new DashStyle([3.0, 2.2], 0);
        }
        pen.Freeze();
        return pen;
    }

    private Point ToDipPoint(double px, double py) =>
        new((px - _canvasBounds.X) / _scale, (py - _canvasBounds.Y) / _scale);

    private Rect ToDipRect(double px, double py, double pw, double ph) =>
        new((px - _canvasBounds.X) / _scale, (py - _canvasBounds.Y) / _scale, pw / _scale, ph / _scale);

    private static Color ToColor(uint argb) =>
        Color.FromArgb(
            (byte)((argb >> 24) & 0xFF),
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF));
}
