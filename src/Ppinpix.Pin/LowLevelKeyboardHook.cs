using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Ppinpix.Pin;

/// <summary>
/// 低级键盘钩子（WH_KEYBOARD_LL），仅用于在存在「鼠标穿透」贴图期间监听 Ctrl+Alt 按住/松开，
/// 无任何鼠标穿透贴图时立即卸载，绝不常驻拦截按键。
/// </summary>
public sealed class LowLevelKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_CONTROL = 0x11;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_MENU = 0x12;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;

    private delegate nint HookProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public nuint dwExtraInfo;
    }

    private HookProc? _proc;
    private nint _hook;
    private bool _ctrlAltDown;

    public bool IsInstalled => _hook != 0;

    public event Action<bool>? CtrlAltChanged;

    public void Install()
    {
        if (_hook != 0) return;

        _proc = Callback;
        using var proc = Process.GetCurrentProcess();
        using var mod = proc.MainModule;
        nint hMod = mod is not null ? GetModuleHandle(mod.ModuleName) : 0;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hMod, 0);
    }

    public void Uninstall()
    {
        if (_hook == 0) return;

        UnhookWindowsHookEx(_hook);
        _hook = 0;
        _proc = null;

        if (_ctrlAltDown)
        {
            _ctrlAltDown = false;
            CtrlAltChanged?.Invoke(false);
        }
    }

    private nint Callback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg is WM_KEYDOWN or WM_KEYUP or WM_SYSKEYDOWN or WM_SYSKEYUP)
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                bool relevant = info.vkCode is VK_CONTROL or VK_LCONTROL or VK_RCONTROL or VK_MENU or VK_LMENU or VK_RMENU;
                if (relevant)
                {
                    bool ctrlDown = IsKeyDown(VK_LCONTROL) || IsKeyDown(VK_RCONTROL) || IsKeyDown(VK_CONTROL);
                    bool altDown = IsKeyDown(VK_LMENU) || IsKeyDown(VK_RMENU) || IsKeyDown(VK_MENU);

                    if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                    {
                        if (info.vkCode is VK_LCONTROL or VK_RCONTROL or VK_CONTROL) ctrlDown = true;
                        if (info.vkCode is VK_LMENU or VK_RMENU or VK_MENU) altDown = true;
                    }
                    else
                    {
                        if (info.vkCode is VK_LCONTROL or VK_RCONTROL or VK_CONTROL) ctrlDown = false;
                        if (info.vkCode is VK_LMENU or VK_RMENU or VK_MENU) altDown = false;
                    }

                    bool next = ctrlDown && altDown;
                    if (next != _ctrlAltDown)
                    {
                        _ctrlAltDown = next;
                        CtrlAltChanged?.Invoke(next);
                    }
                }
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose() => Uninstall();
}
