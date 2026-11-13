using Ppinpix.Capture.Annotation.Shapes;

namespace Ppinpix.Capture.Annotation.Commands;

/// <summary>修改图形样式属性（颜色 / 线宽 / 字号 / 填充 / 虚线等）。</summary>
public sealed class ModifyStyleCommand : IAnnotationCommand
{
    private readonly Guid _shapeId;
    private readonly ShapeStyle _oldStyle;
    private readonly ShapeStyle _newStyle;

    public ModifyStyleCommand(Guid shapeId, ShapeStyle oldStyle, ShapeStyle newStyle)
    {
        _shapeId = shapeId;
        _oldStyle = oldStyle;
        _newStyle = newStyle;
    }

    public void Execute(IList<IShape> shapes)
    {
        var shape = shapes.FirstOrDefault(s => s.Id == _shapeId);
        if (shape is not null) shape.Style = _newStyle;
    }

    public void Undo(IList<IShape> shapes)
    {
        var shape = shapes.FirstOrDefault(s => s.Id == _shapeId);
        if (shape is not null) shape.Style = _oldStyle;
    }
}
