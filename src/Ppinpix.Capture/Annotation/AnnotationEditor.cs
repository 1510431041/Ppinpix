using System.Runtime.InteropServices;
using Ppinpix.Capture.Annotation.Commands;
using Ppinpix.Capture.Annotation.Shapes;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Settings;
using Ppinpix.Theme;
using SkiaSharp;

namespace Ppinpix.Capture.Annotation;

/// <summary>打开或提交文字标注时通知 UI 层的请求。</summary>
public sealed record TextEditRequest(TextShape Shape, bool IsNew);

/// <summary>无 UI 依赖的纯标注状态机与矢量/马赛克渲染管线，支持截图覆盖层和贴图重编辑复用。</summary>
public sealed class AnnotationEditor : IDisposable
{
    private readonly List<IShape> _shapes = [];
    private readonly CommandStack _commands = new();

    private IShape? _draftShape;
    private Guid? _selectedShapeId;
    private DragAction _dragAction = DragAction.None;
    private ShapeHandleKind _activeHandle = ShapeHandleKind.None;
    private IShape? _resizeOriginSnapshot;
    private double _dragLastX;
    private double _dragLastY;
    private double _moveTotalDx;
    private double _moveTotalDy;
    private readonly List<IAnnotationCommand> _eraserBatch = [];
    private readonly HashSet<Guid> _erasedInGesture = [];

    private enum DragAction
    {
        None,
        CreateShape,
        MoveShape,
        ResizeHandle,
        EraseSweep,
    }

    public AnnotationEditor(AnnotationSettings settings)
    {
        CurrentStyle = ShapeStyle.FromSettings(settings);
        _commands.Changed += () => StateChanged?.Invoke();
    }

    public IReadOnlyList<IShape> Shapes => _shapes;

    public IShape? DraftShape => _draftShape;

    public IShape? SelectedShape =>
        _selectedShapeId is Guid id ? _shapes.FirstOrDefault(s => s.Id == id) : null;

    public AnnotationTool ActiveTool { get; private set; } = AnnotationTool.None;

    public AnnotationTool LastMaskTool { get; private set; } = AnnotationTool.Mosaic;

    public ShapeStyle CurrentStyle { get; private set; }

    public bool CanUndo => _commands.CanUndo;

    public bool CanRedo => _commands.CanRedo;

    public bool HasShapes => _shapes.Count > 0;

    public int NextBadgeNumber => _shapes.OfType<NumberBadgeShape>().Count() + 1;

    public event Action? StateChanged;

    public event Action<TextEditRequest>? TextEditRequested;

    public void SetTool(AnnotationTool tool)
    {
        if (tool is AnnotationTool.Mosaic or AnnotationTool.Blur)
        {
            LastMaskTool = tool;
        }

        if (ActiveTool == tool) return;
        CancelDraft();
        ActiveTool = tool;
        if (tool != AnnotationTool.Select)
        {
            _selectedShapeId = null;
        }
        StateChanged?.Invoke();
    }

    public void ToggleTool(AnnotationTool tool)
    {
        SetTool(ActiveTool == tool ? AnnotationTool.None : tool);
    }

    public void UpdateStyle(Func<ShapeStyle, ShapeStyle> updater)
    {
        ShapeStyle next = updater(CurrentStyle);
        if (next == CurrentStyle && SelectedShape is null) return;

        CurrentStyle = next;
        if (SelectedShape is { } selected)
        {
            ShapeStyle old = selected.Style;
            ShapeStyle applied = updater(old);
            if (applied != old)
            {
                _commands.Execute(new ModifyStyleCommand(selected.Id, old, applied), _shapes);
                RenumberBadges();
            }
        }
        StateChanged?.Invoke();
    }

    public void SetColorIndex(int index)
    {
        if (index < 0 || index >= Tokens.AnnotationPalette.Length) return;
        var c = Tokens.AnnotationPalette[index];
        uint argb = ((uint)0xFF << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
        UpdateStyle(s => s with { Argb = argb });
    }

    public void AdjustPrimaryMetric(int direction)
    {
        var tool = SelectedShape?.Tool ?? ActiveTool;
        if (tool == AnnotationTool.Text)
        {
            double[] sizes = [14, 18, 24, 32, 42];
            UpdateStyle(s => s with { FontSize = StepDiscrete(s.FontSize, sizes, direction) });
        }
        else if (tool == AnnotationTool.NumberBadge)
        {
            int[] sizes = [20, 24, 32];
            UpdateStyle(s => s with { BadgeSize = StepDiscreteInt(s.BadgeSize, sizes, direction) });
        }
        else if (tool == AnnotationTool.Mosaic)
        {
            int[] sizes = [8, 12, 16, 24];
            UpdateStyle(s => s with { MosaicBlockPx = StepDiscreteInt(s.MosaicBlockPx, sizes, direction) });
        }
        else if (tool == AnnotationTool.Blur)
        {
            int[] sizes = [6, 12, 20, 32];
            UpdateStyle(s => s with { BlurRadius = StepDiscreteInt(s.BlurRadius, sizes, direction) });
        }
        else
        {
            double[] widths = [2, 3, 5, 8, 12];
            UpdateStyle(s => s with { LineWidth = StepDiscrete(s.LineWidth, widths, direction) });
        }
    }

    /// <summary>在画布物理像素坐标 (x, y) 处处理鼠标按下；返回 true 表示标注器接管本次交互。</summary>
    public bool OnPointerDown(double x, double y, double handleHitRadiusPx, int clickCount = 1)
    {
        if (ActiveTool == AnnotationTool.None) return false;

        if (ActiveTool == AnnotationTool.Eraser)
        {
            _dragAction = DragAction.EraseSweep;
            _eraserBatch.Clear();
            _erasedInGesture.Clear();
            TryEraseAt(x, y, handleHitRadiusPx);
            return true;
        }

        if (ActiveTool == AnnotationTool.Select)
        {
            if (SelectedShape is { } current)
            {
                var handle = HitTestHandle(current, x, y, handleHitRadiusPx);
                if (handle != ShapeHandleKind.None)
                {
                    if (clickCount >= 2 && handle == ShapeHandleKind.CurveControl && current is ArrowShape curved && curved.IsCurved)
                    {
                        ResetArrowCurve(curved);
                        return false;
                    }
                    _activeHandle = handle;
                    _resizeOriginSnapshot = current.Clone(newId: false);
                    _dragAction = DragAction.ResizeHandle;
                    return true;
                }
            }

            var hit = HitTestShape(x, y, handleHitRadiusPx);
            if (hit is not null)
            {
                _selectedShapeId = hit.Id;
                if (hit is TextShape textShape && clickCount >= 2)
                {
                    _dragAction = DragAction.None;
                    StateChanged?.Invoke();
                    TextEditRequested?.Invoke(new TextEditRequest(textShape, IsNew: false));
                    return true;
                }
                if (hit is ArrowShape arrowShape && arrowShape.IsCurved && clickCount >= 2)
                {
                    ResetArrowCurve(arrowShape);
                    return false;
                }

                _dragLastX = x;
                _dragLastY = y;
                _moveTotalDx = 0;
                _moveTotalDy = 0;
                _dragAction = DragAction.MoveShape;
                StateChanged?.Invoke();
                return true;
            }

            if (_selectedShapeId is not null)
            {
                _selectedShapeId = null;
                StateChanged?.Invoke();
            }
            return true;
        }

        if (ActiveTool == AnnotationTool.Text)
        {
            var existing = HitTestShape(x, y, handleHitRadiusPx);
            if (existing is TextShape existingText)
            {
                _selectedShapeId = existingText.Id;
                StateChanged?.Invoke();
                TextEditRequested?.Invoke(new TextEditRequest(existingText, IsNew: false));
                return true;
            }

            var newText = new TextShape(x, y, string.Empty, CurrentStyle);
            TextEditRequested?.Invoke(new TextEditRequest(newText, IsNew: true));
            return true;
        }

        // 若当前选中某个图形且点击落在其控制点上，即使在绘图模式下也允许直接调控制点
        if (SelectedShape is { } selectedForHandle)
        {
            var h = HitTestHandle(selectedForHandle, x, y, handleHitRadiusPx);
            if (h != ShapeHandleKind.None)
            {
                if (clickCount >= 2 && h == ShapeHandleKind.CurveControl && selectedForHandle is ArrowShape curved && curved.IsCurved)
                {
                    ResetArrowCurve(curved);
                    return false;
                }
                _activeHandle = h;
                _resizeOriginSnapshot = selectedForHandle.Clone(newId: false);
                _dragAction = DragAction.ResizeHandle;
                return true;
            }
        }

        _draftShape = CreateShape(ActiveTool, x, y);
        if (_draftShape is null) return false;

        _selectedShapeId = null;
        _dragAction = DragAction.CreateShape;
        StateChanged?.Invoke();
        return true;
    }

    public bool OnPointerMove(double x, double y, bool constrain, double hitRadiusPx)
    {
        switch (_dragAction)
        {
            case DragAction.CreateShape when _draftShape is not null:
                _draftShape.UpdateDrag(x, y, constrain);
                StateChanged?.Invoke();
                return true;

            case DragAction.MoveShape when SelectedShape is { } moved:
                double dx = x - _dragLastX;
                double dy = y - _dragLastY;
                _dragLastX = x;
                _dragLastY = y;
                _moveTotalDx += dx;
                _moveTotalDy += dy;
                moved.Translate(dx, dy);
                StateChanged?.Invoke();
                return true;

            case DragAction.ResizeHandle when SelectedShape is { } resized:
                resized.MoveHandle(_activeHandle, x, y, constrain);
                StateChanged?.Invoke();
                return true;

            case DragAction.EraseSweep:
                TryEraseAt(x, y, hitRadiusPx);
                return true;

            default:
                return false;
        }
    }

    public bool OnPointerUp(double x, double y, bool constrain)
    {
        switch (_dragAction)
        {
            case DragAction.CreateShape when _draftShape is not null:
                _draftShape.UpdateDrag(x, y, constrain);
                var finished = _draftShape;
                _draftShape = null;
                _dragAction = DragAction.None;

                if (finished.IsValid)
                {
                    _commands.Execute(new AddShapeCommand(finished), _shapes);
                    RenumberBadges();
                    _selectedShapeId = finished.Id;
                }
                else
                {
                    (finished as IDisposable)?.Dispose();
                }

                StateChanged?.Invoke();
                return true;

            case DragAction.MoveShape when SelectedShape is { } moved:
                _dragAction = DragAction.None;
                if (Math.Abs(_moveTotalDx) > 0.5 || Math.Abs(_moveTotalDy) > 0.5)
                {
                    // 先回退即时拖动偏移，再由命令统一 Execute 保证命令栈状态干净一致
                    moved.Translate(-_moveTotalDx, -_moveTotalDy);
                    _commands.Execute(new MoveShapeCommand(moved.Id, _moveTotalDx, _moveTotalDy), _shapes);
                }
                StateChanged?.Invoke();
                return true;

            case DragAction.ResizeHandle when SelectedShape is { } resized && _resizeOriginSnapshot is not null:
                _dragAction = DragAction.None;
                var after = resized.Clone(newId: false);
                var before = _resizeOriginSnapshot;
                _resizeOriginSnapshot = null;
                _activeHandle = ShapeHandleKind.None;

                // 先还原至 before 再执行命令
                ReplaceShapeInstance(before.Clone(newId: false));
                _commands.Execute(new ResizeShapeCommand(before, after), _shapes);
                StateChanged?.Invoke();
                return true;

            case DragAction.EraseSweep:
                _dragAction = DragAction.None;
                if (_eraserBatch.Count > 0)
                {
                    // TryEraseAt 已即时移除了，为了标准 Undo/Redo 栈一致，先 Undo 再通过 CompositeCommand Execute
                    for (int i = _eraserBatch.Count - 1; i >= 0; i--)
                    {
                        _eraserBatch[i].Undo(_shapes);
                    }
                    _commands.Execute(new CompositeCommand(_eraserBatch), _shapes);
                    RenumberBadges();
                }
                _eraserBatch.Clear();
                _erasedInGesture.Clear();
                StateChanged?.Invoke();
                return true;

            default:
                return false;
        }
    }

    public void CommitTextEdit(TextShape shape, bool isNew, string originalText, ShapeStyle originalStyle, string newText)
    {
        string normalized = (newText ?? string.Empty).Replace("\r\n", "\n");
        shape.IsEditing = false;

        if (isNew)
        {
            shape.Text = normalized;
            shape.Style = CurrentStyle;
            if (shape.IsValid)
            {
                _commands.Execute(new AddShapeCommand(shape), _shapes);
                _selectedShapeId = shape.Id;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(normalized))
            {
                _commands.Execute(new DeleteShapeCommand(shape), _shapes);
                if (_selectedShapeId == shape.Id) _selectedShapeId = null;
            }
            else if (normalized != originalText || shape.Style != originalStyle)
            {
                ShapeStyle targetStyle = shape.Style;
                shape.Text = originalText;
                shape.Style = originalStyle;
                _commands.Execute(
                    new EditTextCommand(shape.Id, originalText, normalized, originalStyle, targetStyle),
                    _shapes);
                _selectedShapeId = shape.Id;
            }
        }
        StateChanged?.Invoke();
    }

    public bool Undo()
    {
        CancelDraft();
        if (!_commands.Undo(_shapes)) return false;
        RenumberBadges();
        if (_selectedShapeId is Guid id && _shapes.All(s => s.Id != id))
        {
            _selectedShapeId = null;
        }
        StateChanged?.Invoke();
        return true;
    }

    public bool Redo()
    {
        CancelDraft();
        if (!_commands.Redo(_shapes)) return false;
        RenumberBadges();
        StateChanged?.Invoke();
        return true;
    }

    public bool DeleteSelected()
    {
        if (SelectedShape is not { } selected) return false;
        _commands.Execute(new DeleteShapeCommand(selected), _shapes);
        _selectedShapeId = null;
        RenumberBadges();
        StateChanged?.Invoke();
        return true;
    }

    public bool DuplicateSelected(double offsetX = 14, double offsetY = 14)
    {
        if (SelectedShape is not { } selected) return false;
        var copy = selected.Clone(newId: true);
        copy.Translate(offsetX, offsetY);
        _commands.Execute(new AddShapeCommand(copy), _shapes);
        RenumberBadges();
        _selectedShapeId = copy.Id;
        StateChanged?.Invoke();
        return true;
    }

    public void AddShapeDirectly(IShape shape)
    {
        _commands.Execute(new AddShapeCommand(shape), _shapes);
        RenumberBadges();
        _selectedShapeId = shape.Id;
        StateChanged?.Invoke();
    }

    public IShape? HitTestShape(double x, double y, double tolerancePx)
    {
        for (int i = _shapes.Count - 1; i >= 0; i--)
        {
            if (_shapes[i].HitTest(x, y, tolerancePx))
            {
                return _shapes[i];
            }
        }
        return null;
    }

    public static ShapeHandleKind HitTestHandle(IShape shape, double x, double y, double tolerancePx)
    {
        foreach (var h in shape.GetHandles())
        {
            if (Math.Abs(x - h.X) <= tolerancePx && Math.Abs(y - h.Y) <= tolerancePx)
            {
                return h.Kind;
            }
        }
        return ShapeHandleKind.None;
    }

    /// <summary>
    /// 按 1:1 物理像素渲染最终导出帧（先拷贝原图裁剪区，再依次合成所有标注；UI 选择框与控制点不会输出）。
    /// </summary>
    public ScreenFrame RenderToFrame(ScreenFrame source, PxRect cropRect)
    {
        var clamped = cropRect.Intersect(source.Bounds);
        if (clamped.IsEmpty)
        {
            return new ScreenFrame(cropRect, Math.Max(0, cropRect.Width * 4), new byte[Math.Max(0, cropRect.Width * cropRect.Height * 4)]);
        }

        if (_shapes.Count == 0)
        {
            return source.Crop(clamped);
        }

        byte[] croppedBytes = source.CopyRegionBytes(clamped);
        var info = new SKImageInfo(clamped.Width, clamped.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        using var baseBmp = new SKBitmap(info);
        Marshal.Copy(croppedBytes, 0, baseBmp.GetPixels(), croppedBytes.Length);

        var canvas = surface.Canvas;
        canvas.DrawBitmap(baseBmp, 0, 0);

        using var renderCtx = new AnnotationRenderContext(source);

        // 先绘制马赛克/模糊（底层打码），再统一绘制聚光灯遮罩（暗化非聚光区域），最后绘制其余矢量标注与文字
        foreach (var shape in _shapes)
        {
            if (shape is MosaicShape or BlurShape)
            {
                shape.Render(canvas, renderCtx, clamped.X, clamped.Y);
            }
        }

        var spotlights = _shapes.OfType<SpotlightShape>().Where(s => s.IsValid).ToList();
        if (spotlights.Count > 0)
        {
            SpotlightShape.RenderSpotlightMask(canvas, clamped.Width, clamped.Height, spotlights, clamped.X, clamped.Y);
        }

        foreach (var shape in _shapes)
        {
            if (shape is MosaicShape or BlurShape or SpotlightShape) continue;
            shape.Render(canvas, renderCtx, clamped.X, clamped.Y);
        }
        canvas.Flush();

        using var img = surface.Snapshot();
        using var outBmp = SKBitmap.FromImage(img);
        byte[] result = new byte[clamped.Width * clamped.Height * 4];
        IntPtr srcPtr = outBmp.GetPixels();
        int rowBytes = clamped.Width * 4;
        for (int y = 0; y < clamped.Height; y++)
        {
            Marshal.Copy(srcPtr + y * outBmp.RowBytes, result, y * rowBytes, rowBytes);
        }

        return new ScreenFrame(clamped, rowBytes, result);
    }

    private IShape? CreateShape(AnnotationTool tool, double x, double y) => tool switch
    {
        AnnotationTool.Rectangle => new RectangleShape(x, y, CurrentStyle),
        AnnotationTool.Ellipse => new EllipseShape(x, y, CurrentStyle),
        AnnotationTool.Arrow => new ArrowShape(x, y, x, y, CurrentStyle),
        AnnotationTool.Line => new LineShape(x, y, x, y, CurrentStyle),
        AnnotationTool.Freehand => new FreehandShape(x, y, CurrentStyle),
        AnnotationTool.Highlight => new HighlightShape(x, y, CurrentStyle),
        AnnotationTool.NumberBadge => new NumberBadgeShape(x, y, NextBadgeNumber, CurrentStyle),
        AnnotationTool.Mosaic => new MosaicShape(x, y, CurrentStyle),
        AnnotationTool.Blur => new BlurShape(x, y, CurrentStyle),
        AnnotationTool.Spotlight => new SpotlightShape(x, y, CurrentStyle),
        _ => null,
    };

    public void Clear()
    {
        CancelDraft();
        foreach (var shape in _shapes)
        {
            (shape as IDisposable)?.Dispose();
        }
        _shapes.Clear();
        _selectedShapeId = null;
        ActiveTool = AnnotationTool.None;
        _commands.Clear();
        StateChanged?.Invoke();
    }

    private void ResetArrowCurve(ArrowShape arrow)
    {
        var before = arrow.Clone(newId: false);
        arrow.ResetCurve();
        var after = arrow.Clone(newId: false);
        ReplaceShapeInstance(before.Clone(newId: false));
        _commands.Execute(new ResizeShapeCommand(before, after), _shapes);
        _dragAction = DragAction.None;
        StateChanged?.Invoke();
    }

    private void TryEraseAt(double x, double y, double tolerancePx)
    {
        for (int i = _shapes.Count - 1; i >= 0; i--)
        {
            var shape = _shapes[i];
            if (_erasedInGesture.Contains(shape.Id)) continue;
            if (shape.HitTest(x, y, tolerancePx))
            {
                _erasedInGesture.Add(shape.Id);
                var del = new DeleteShapeCommand(shape);
                del.Execute(_shapes);
                _eraserBatch.Add(del);
                if (_selectedShapeId == shape.Id) _selectedShapeId = null;
                RenumberBadges();
                StateChanged?.Invoke();
                break;
            }
        }
    }

    private void ReplaceShapeInstance(IShape replacement)
    {
        for (int i = 0; i < _shapes.Count; i++)
        {
            if (_shapes[i].Id == replacement.Id)
            {
                _shapes[i] = replacement;
                return;
            }
        }
    }

    private void RenumberBadges()
    {
        int seq = 1;
        foreach (var shape in _shapes)
        {
            if (shape is NumberBadgeShape badge)
            {
                badge.Number = seq++;
            }
        }
    }

    private void CancelDraft()
    {
        if (_draftShape is IDisposable disp) disp.Dispose();
        _draftShape = null;
        _dragAction = DragAction.None;
        _resizeOriginSnapshot = null;
        _activeHandle = ShapeHandleKind.None;
    }

    private static double StepDiscrete(double current, double[] steps, int direction)
    {
        int nearest = 0;
        double best = double.MaxValue;
        for (int i = 0; i < steps.Length; i++)
        {
            double diff = Math.Abs(steps[i] - current);
            if (diff < best)
            {
                best = diff;
                nearest = i;
            }
        }
        int next = Math.Clamp(nearest + (direction >= 0 ? 1 : -1), 0, steps.Length - 1);
        return steps[next];
    }

    private static int StepDiscreteInt(int current, int[] steps, int direction)
    {
        int nearest = 0;
        int best = int.MaxValue;
        for (int i = 0; i < steps.Length; i++)
        {
            int diff = Math.Abs(steps[i] - current);
            if (diff < best)
            {
                best = diff;
                nearest = i;
            }
        }
        int next = Math.Clamp(nearest + (direction >= 0 ? 1 : -1), 0, steps.Length - 1);
        return steps[next];
    }

    public void Dispose()
    {
        CancelDraft();
        foreach (var shape in _shapes)
        {
            (shape as IDisposable)?.Dispose();
        }
        _shapes.Clear();
    }
}
