using Ppinpix.Capture.Annotation.Shapes;

namespace Ppinpix.Capture.Annotation.Commands;

/// <summary>标注编辑器命令接口（支持无限步撤销与重做）。</summary>
public interface IAnnotationCommand
{
    void Execute(IList<IShape> shapes);

    void Undo(IList<IShape> shapes);
}
