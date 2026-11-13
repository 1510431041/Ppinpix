using Ppinpix.Capture.Annotation.Shapes;

namespace Ppinpix.Capture.Annotation.Commands;

/// <summary>通过快照替换执行无浮点漂移的缩放/控制点拖动命令。</summary>
public sealed class ResizeShapeCommand : IAnnotationCommand
{
    private readonly IShape _beforeSnapshot;
    private readonly IShape _afterSnapshot;

    public ResizeShapeCommand(IShape beforeSnapshot, IShape afterSnapshot)
    {
        _beforeSnapshot = beforeSnapshot.Clone(newId: false);
        _afterSnapshot = afterSnapshot.Clone(newId: false);
    }

    public void Execute(IList<IShape> shapes) => Replace(shapes, _afterSnapshot.Clone(newId: false));

    public void Undo(IList<IShape> shapes) => Replace(shapes, _beforeSnapshot.Clone(newId: false));

    private static void Replace(IList<IShape> shapes, IShape replacement)
    {
        for (int i = 0; i < shapes.Count; i++)
        {
            if (shapes[i].Id == replacement.Id)
            {
                shapes[i] = replacement;
                return;
            }
        }
    }
}
