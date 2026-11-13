using Ppinpix.Capture.Annotation.Shapes;

namespace Ppinpix.Capture.Annotation.Commands;

/// <summary>无上限 Undo / Redo 命令栈。</summary>
public sealed class CommandStack
{
    private readonly Stack<IAnnotationCommand> _undo = new();
    private readonly Stack<IAnnotationCommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoCount => _undo.Count;

    public int RedoCount => _redo.Count;

    public event Action? Changed;

    public void Execute(IAnnotationCommand command, IList<IShape> shapes)
    {
        command.Execute(shapes);
        _undo.Push(command);
        _redo.Clear();
        Changed?.Invoke();
    }

    public bool Undo(IList<IShape> shapes)
    {
        if (_undo.Count == 0) return false;
        var cmd = _undo.Pop();
        cmd.Undo(shapes);
        _redo.Push(cmd);
        Changed?.Invoke();
        return true;
    }

    public bool Redo(IList<IShape> shapes)
    {
        if (_redo.Count == 0) return false;
        var cmd = _redo.Pop();
        cmd.Execute(shapes);
        _undo.Push(cmd);
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }
}
