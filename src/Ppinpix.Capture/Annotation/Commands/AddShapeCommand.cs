using Ppinpix.Capture.Annotation.Shapes;

namespace Ppinpix.Capture.Annotation.Commands;

/// <summary>添加单个图形命令。</summary>
public sealed class AddShapeCommand : IAnnotationCommand
{
    private readonly IShape _shape;
    private readonly int _insertIndex;

    public AddShapeCommand(IShape shape, int insertIndex = -1)
    {
        _shape = shape;
        _insertIndex = insertIndex;
    }

    public IShape Shape => _shape;

    public void Execute(IList<IShape> shapes)
    {
        if (shapes.Any(s => s.Id == _shape.Id)) return;
        if (_insertIndex >= 0 && _insertIndex <= shapes.Count)
        {
            shapes.Insert(_insertIndex, _shape);
        }
        else
        {
            shapes.Add(_shape);
        }
    }

    public void Undo(IList<IShape> shapes)
    {
        for (int i = shapes.Count - 1; i >= 0; i--)
        {
            if (shapes[i].Id == _shape.Id)
            {
                shapes.RemoveAt(i);
                break;
            }
        }
    }
}
