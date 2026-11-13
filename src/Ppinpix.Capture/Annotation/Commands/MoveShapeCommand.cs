using Ppinpix.Capture.Annotation.Shapes;

namespace Ppinpix.Capture.Annotation.Commands;

/// <summary>平移单个图形。</summary>
public sealed class MoveShapeCommand : IAnnotationCommand
{
    private readonly Guid _shapeId;
    private readonly double _dx;
    private readonly double _dy;

    public MoveShapeCommand(Guid shapeId, double dx, double dy)
    {
        _shapeId = shapeId;
        _dx = dx;
        _dy = dy;
    }

    public void Execute(IList<IShape> shapes)
    {
        var shape = shapes.FirstOrDefault(s => s.Id == _shapeId);
        shape?.Translate(_dx, _dy);
    }

    public void Undo(IList<IShape> shapes)
    {
        var shape = shapes.FirstOrDefault(s => s.Id == _shapeId);
        shape?.Translate(-_dx, -_dy);
    }
}
