using Ppinpix.Capture.Annotation.Shapes;

namespace Ppinpix.Capture.Annotation.Commands;

/// <summary>删除单个图形并记住其原始层级索引。</summary>
public sealed class DeleteShapeCommand : IAnnotationCommand
{
    private readonly IShape _shape;
    private int _removedIndex = -1;

    public DeleteShapeCommand(IShape shape)
    {
        _shape = shape;
    }

    public void Execute(IList<IShape> shapes)
    {
        for (int i = 0; i < shapes.Count; i++)
        {
            if (shapes[i].Id == _shape.Id)
            {
                _removedIndex = i;
                shapes.RemoveAt(i);
                return;
            }
        }
    }

    public void Undo(IList<IShape> shapes)
    {
        if (shapes.Any(s => s.Id == _shape.Id)) return;
        int idx = _removedIndex >= 0 && _removedIndex <= shapes.Count ? _removedIndex : shapes.Count;
        shapes.Insert(idx, _shape);
    }
}
