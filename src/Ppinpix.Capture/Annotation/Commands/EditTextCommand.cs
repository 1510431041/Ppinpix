using Ppinpix.Capture.Annotation.Shapes;

namespace Ppinpix.Capture.Annotation.Commands;

/// <summary>编辑文字标注内容（包括样式同步更新）。</summary>
public sealed class EditTextCommand : IAnnotationCommand
{
    private readonly Guid _shapeId;
    private readonly string _oldText;
    private readonly string _newText;
    private readonly ShapeStyle _oldStyle;
    private readonly ShapeStyle _newStyle;

    public EditTextCommand(Guid shapeId, string oldText, string newText, ShapeStyle oldStyle, ShapeStyle newStyle)
    {
        _shapeId = shapeId;
        _oldText = oldText;
        _newText = newText;
        _oldStyle = oldStyle;
        _newStyle = newStyle;
    }

    public void Execute(IList<IShape> shapes)
    {
        if (shapes.FirstOrDefault(s => s.Id == _shapeId) is TextShape text)
        {
            text.Style = _newStyle;
            text.Text = _newText;
        }
    }

    public void Undo(IList<IShape> shapes)
    {
        if (shapes.FirstOrDefault(s => s.Id == _shapeId) is TextShape text)
        {
            text.Style = _oldStyle;
            text.Text = _oldText;
        }
    }
}
