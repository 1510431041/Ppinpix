using System.Runtime.InteropServices;
using Ppinpix.Core.Interop;

namespace Ppinpix.Core.Display;

/// <summary>
/// 仅用于接收窗口消息的不可见窗口（HWND_MESSAGE）。
/// 全局热键、显示配置变化、单实例广播都复用它。
/// </summary>
public sealed class MessageWindow : IDisposable
{
    private const string ClassName = "PpinpixMessageWindow";

    // 静态持有：类注册表的窗口过程必须保持存活，否则 GC 后回调会崩溃。
    private static readonly Dictionary<nint, MessageWindow> Instances = [];
    private static readonly WindowApi.WndProc WndProcDelegate = StaticWndProc;
    private static ushort _classAtom;
    private static readonly Lock ClassLock = new();

    private readonly string _name;
    private nint _hwnd;
    private bool _disposed;

    public MessageWindow(string name)
    {
        _name = name;
        EnsureClassRegistered();

        _hwnd = WindowApi.CreateWindowEx(
            0, ClassName, name, 0, 0, 0, 0, 0,
            WindowApi.HWND_MESSAGE, 0, WindowApi.GetModuleHandle(null), 0);

        if (_hwnd == 0)
        {
            throw new InvalidOperationException(
                $"创建消息窗口失败，Win32 错误码 {Marshal.GetLastWin32Error()}");
        }

        lock (ClassLock)
        {
            Instances[_hwnd] = this;
        }
    }

    /// <summary>窗口句柄，可用于 RegisterHotKey、SendMessage 等。</summary>
    public nint Handle => _hwnd;

    public string Name => _name;

    /// <summary>收到窗口消息时触发（消息号、wParam、lParam）。</summary>
    public event Action<uint, nint, nint>? Message;

    private static void EnsureClassRegistered()
    {
        lock (ClassLock)
        {
            if (_classAtom != 0) return;

            var wcex = new WindowApi.WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WindowApi.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcDelegate),
                hInstance = WindowApi.GetModuleHandle(null),
                lpszClassName = ClassName,
            };

            _classAtom = WindowApi.RegisterClassEx(ref wcex);
            if (_classAtom == 0)
            {
                int err = Marshal.GetLastWin32Error();
                const int ERROR_CLASS_ALREADY_EXISTS = 1410;
                if (err != ERROR_CLASS_ALREADY_EXISTS)
                {
                    throw new InvalidOperationException($"注册窗口类失败，Win32 错误码 {err}");
                }
            }
        }
    }

    private static nint StaticWndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        MessageWindow? target;
        lock (ClassLock)
        {
            Instances.TryGetValue(hWnd, out target);
        }

        target?.Message?.Invoke(msg, wParam, lParam);

        return WindowApi.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwnd != 0)
        {
            lock (ClassLock)
            {
                Instances.Remove(_hwnd);
            }
            WindowApi.DestroyWindow(_hwnd);
            _hwnd = 0;
        }

        GC.SuppressFinalize(this);
    }
}
