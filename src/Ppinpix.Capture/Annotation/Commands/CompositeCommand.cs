using Ppinpix.Capture.Annotation.Shapes;

namespace Ppinpix.Capture.Annotation.Commands;

/// <summary>组合命令（例如单次橡皮擦划过删除多个图形时一次性撤销/重做）。</summary>
public sealed class CompositeCommand : IAnnotationCommand
{
    private readonly IReadOnlyList<IAnnotationCommand> _commands;

    public CompositeCommand(IEnumerable<IAnnotationCommand> commands)
    {
        _commands = commands.ToList();
    }

    public bool IsEmpty => _commands.Count == 0;

    public void Execute(IList<IShape> shapes)
    {
        foreach (var cmd in _commands)
        {
            cmd.Execute(shapes);
        }
    }

    public void Undo(IList<IShape> shapes)
    {
        for (int i = _commands.Count - 1; i >= 0; i--)
        {
            _commands[i].Undo(shapes);
        }
    }
}
