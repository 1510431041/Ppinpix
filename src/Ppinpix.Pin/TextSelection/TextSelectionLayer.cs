using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Ppinpix.Ocr.Models;
using Ppinpix.Theme;

namespace Ppinpix.Pin.TextSelection;

/// <summary>纯坐标映射与字符命中辅助，负责把原图 OCR 字符框准确映射到任意旋转/翻转/缩放后的贴图显示区域。</summary>
public static class TextCoordinateMapper
{
    public static Rect MapQuadToDisplayRect(
        Quad q,
        int sourceW,
        int sourceH,
        int rotationQuarterTurns,
        bool flipH,
        bool flipV,
        double displayW,
        double displayH)
    {
        if (sourceW <= 0 || sourceH <= 0 || displayW <= 0 || displayH <= 0)
        {
            return Rect.Empty;
        }

        var (nx1, ny1) = TransformNormalized(q.MinX / sourceW, q.MinY / sourceH, rotationQuarterTurns, flipH, flipV);
        var (nx2, ny2) = TransformNormalized(q.MaxX / sourceW, q.MaxY / sourceH, rotationQuarterTurns, flipH, flipV);

        double left = Math.Min(nx1, nx2) * displayW;
        double top = Math.Min(ny1, ny2) * displayH;
        double right = Math.Max(nx1, nx2) * displayW;
        double bottom = Math.Max(ny1, ny2) * displayH;

        return new Rect(left, top, Math.Max(1.0, right - left), Math.Max(1.0, bottom - top));
    }

    private static (double Nx, double Ny) TransformNormalized(double x, double y, int rot90, bool flipH, bool flipV)
    {
        int r = ((rot90 % 4) + 4) % 4;
        (double rx, double ry) = r switch
        {
            1 => (1.0 - y, x),
            2 => (1.0 - x, 1.0 - y),
            3 => (y, 1.0 - x),
            _ => (x, y),
        };

        if (flipH) rx = 1.0 - rx;
        if (flipV) ry = 1.0 - ry;
        return (rx, ry);
    }
}

/// <summary>
/// 贴图静默 OCR 文字选区叠加层。
/// 悬停文字区域出现 I 型光标，支持阅读顺序划词、高亮选中、Ctrl+C 复制文字、Ctrl+A 全选、按住 Space 优先拖拽窗口。
/// </summary>
public sealed class TextSelectionLayer : FrameworkElement
{
    private readonly record struct IndexedChar(CharBox Char, int LineIndex, bool IsLineEnd);

    private readonly List<IndexedChar> _flatChars = [];
    private OcrResult _ocr = OcrResult.Empty;

    private int _sourceWidth;
    private int _sourceHeight;
    private int _rotationQuarterTurns;
    private bool _flipH;
    private bool _flipV;

    private int _selAnchor = -1;
    private int _selCaret = -1;
    private bool _selecting;

    public bool HasOcrText => _flatChars.Count > 0;

    public bool HasSelection => _selAnchor >= 0 && _selCaret >= 0;

    public OcrResult CurrentOcr => _ocr;

    public event Action? SelectionChanged;

    public TextSelectionLayer()
    {
        Focusable = false;
        IsHitTestVisible = true;
    }

    public void SetOcrResult(OcrResult result, int sourceWidth, int sourceHeight)
    {
        _ocr = result ?? OcrResult.Empty;
        _sourceWidth = sourceWidth;
        _sourceHeight = sourceHeight;
        ClearSelection();
        RebuildFlatIndex();
        InvalidateVisual();
    }

    public void UpdateTransform(int sourceWidth, int sourceHeight, int rotationQuarterTurns, bool flipH, bool flipV)
    {
        _sourceWidth = sourceWidth;
        _sourceHeight = sourceHeight;
        _rotationQuarterTurns = ((rotationQuarterTurns % 4) + 4) % 4;
        _flipH = flipH;
        _flipV = flipV;
        InvalidateVisual();
    }

    public void SelectAll()
    {
        if (_flatChars.Count == 0) return;
        _selAnchor = 0;
        _selCaret = _flatChars.Count - 1;
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public void ClearSelection()
    {
        if (!HasSelection && !_selecting) return;
        _selAnchor = -1;
        _selCaret = -1;
        _selecting = false;
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public string GetSelectedText()
    {
        if (!HasSelection || _flatChars.Count == 0) return string.Empty;
        int start = Math.Clamp(Math.Min(_selAnchor, _selCaret), 0, _flatChars.Count - 1);
        int end = Math.Clamp(Math.Max(_selAnchor, _selCaret), 0, _flatChars.Count - 1);

        var chars = new List<char>(end - start + 16);
        int prevLine = _flatChars[start].LineIndex;

        for (int i = start; i <= end; i++)
        {
            var entry = _flatChars[i];
            if (entry.LineIndex != prevLine)
            {
                chars.Add('\n');
                prevLine = entry.LineIndex;
            }
            chars.AddRange(entry.Char.Text);
        }

        return new string(chars.ToArray());
    }

    public bool TryBeginDragSelection(Point pt, int clickCount)
    {
        if (Keyboard.IsKeyDown(Key.Space) || _flatChars.Count == 0)
        {
            ClearSelection();
            return false;
        }

        int hit = HitTestCharIndex(pt);
        if (hit < 0)
        {
            ClearSelection();
            return false;
        }

        if (clickCount >= 2)
        {
            // 双击选中整行文字
            int lineIdx = _flatChars[hit].LineIndex;
            int first = hit;
            while (first > 0 && _flatChars[first - 1].LineIndex == lineIdx) first--;
            int last = hit;
            while (last < _flatChars.Count - 1 && _flatChars[last + 1].LineIndex == lineIdx) last++;
            _selAnchor = first;
            _selCaret = last;
            _selecting = false;
            SelectionChanged?.Invoke();
            InvalidateVisual();
            return true;
        }

        _selecting = true;
        _selAnchor = hit;
        _selCaret = hit;
        CaptureMouse();
        SelectionChanged?.Invoke();
        InvalidateVisual();
        return true;
    }

    public bool TryUpdateDragSelection(Point pt)
    {
        if (!_selecting) return false;
        int nearest = FindNearestCharIndex(pt);
        if (nearest >= 0 && nearest != _selCaret)
        {
            _selCaret = nearest;
            SelectionChanged?.Invoke();
            InvalidateVisual();
        }
        return true;
    }

    public bool EndDragSelection()
    {
        if (!_selecting) return false;
        _selecting = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        return true;
    }

    public bool IsPointOverText(Point pt) =>
        !Keyboard.IsKeyDown(Key.Space) && HitTestCharIndex(pt) >= 0;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (!HasSelection || _flatChars.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;

        int start = Math.Clamp(Math.Min(_selAnchor, _selCaret), 0, _flatChars.Count - 1);
        int end = Math.Clamp(Math.Max(_selAnchor, _selCaret), 0, _flatChars.Count - 1);

        var highlightColor = Color.FromArgb(92, Tokens.Accent.R, Tokens.Accent.G, Tokens.Accent.B);
        var brush = new SolidColorBrush(highlightColor);
        brush.Freeze();

        // 按连续同行合并矩形，绘制干净顺滑
        int i = start;
        while (i <= end)
        {
            int lineIdx = _flatChars[i].LineIndex;
            Rect union = GetCharDisplayRect(i);
            int j = i + 1;
            while (j <= end && _flatChars[j].LineIndex == lineIdx)
            {
                union.Union(GetCharDisplayRect(j));
                j++;
            }
            dc.DrawRoundedRectangle(brush, null, union, 2, 2);
            i = j;
        }
    }

    private void RebuildFlatIndex()
    {
        _flatChars.Clear();
        if (_ocr.Lines.Count == 0) return;

        for (int li = 0; li < _ocr.Lines.Count; li++)
        {
            var line = _ocr.Lines[li];
            if (line.Chars.Count > 0)
            {
                for (int ci = 0; ci < line.Chars.Count; ci++)
                {
                    _flatChars.Add(new IndexedChar(line.Chars[ci], li, ci == line.Chars.Count - 1));
                }
            }
            else if (!string.IsNullOrEmpty(line.Text))
            {
                double w = line.Box.Width / line.Text.Length;
                for (int ci = 0; ci < line.Text.Length; ci++)
                {
                    var cb = new CharBox(
                        line.Text[ci].ToString(),
                        Quad.FromRect(line.Box.MinX + ci * w, line.Box.MinY, w, line.Box.Height),
                        line.Confidence);
                    _flatChars.Add(new IndexedChar(cb, li, ci == line.Text.Length - 1));
                }
            }
        }
    }

    private Rect GetCharDisplayRect(int index) =>
        TextCoordinateMapper.MapQuadToDisplayRect(
            _flatChars[index].Char.Box,
            _sourceWidth,
            _sourceHeight,
            _rotationQuarterTurns,
            _flipH,
            _flipV,
            ActualWidth,
            ActualHeight);

    private int HitTestCharIndex(Point pt)
    {
        for (int i = 0; i < _flatChars.Count; i++)
        {
            var r = GetCharDisplayRect(i);
            r.Inflate(2, 2);
            if (r.Contains(pt)) return i;
        }
        return -1;
    }

    private int FindNearestCharIndex(Point pt)
    {
        int direct = HitTestCharIndex(pt);
        if (direct >= 0) return direct;

        int best = -1;
        double bestDist = double.MaxValue;
        for (int i = 0; i < _flatChars.Count; i++)
        {
            var r = GetCharDisplayRect(i);
            double cx = r.X + r.Width * 0.5;
            double cy = r.Y + r.Height * 0.5;
            // Y 偏差给予更高权重以符合多行阅读习惯
            double dy = (pt.Y - cy) * 2.5;
            double dx = pt.X - cx;
            double dist = dx * dx + dy * dy;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }
        return best;
    }
}
