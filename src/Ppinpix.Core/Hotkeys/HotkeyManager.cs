using System.Runtime.InteropServices;
using Ppinpix.Core.Display;
using Ppinpix.Core.Interop;
using Ppinpix.Core.Settings;

namespace Ppinpix.Core.Hotkeys;

/// <summary>一个已注册的全局快捷键。</summary>
public sealed record RegisteredHotkey(string Name, HotkeyGesture Gesture, int Id);

/// <summary>注册失败的原因。</summary>
public enum HotkeyRegistrationStatus
{
    Registered = 0,

    /// <summary>该组合键已被其他程序占用。</summary>
    AlreadyTaken = 1,

    /// <summary>组合键本身不合法（例如只有一个修饰键）。</summary>
    Invalid = 2,

    /// <summary>注册时发生未知错误。</summary>
    Failed = 3,
}

public sealed record HotkeyRegistrationResult(
    string Name,
    HotkeyGesture Gesture,
    HotkeyRegistrationStatus Status,
    int Win32Error = 0)
{
    public bool IsRegistered => Status == HotkeyRegistrationStatus.Registered;
}

/// <summary>
/// 全局热键管理。基于 RegisterHotKey + 消息窗口，不安装键盘钩子（避免杀软误报）。
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
    private const int HotkeyIdBase = 0x5050; // 'PP'

    private readonly MessageWindow _window;
    private readonly Dictionary<int, RegisteredHotkey> _byId = [];
    private readonly Dictionary<string, RegisteredHotkey> _byName = new(StringComparer.Ordinal);
    private int _nextId = HotkeyIdBase;

    public HotkeyManager()
    {
        _window = new MessageWindow("Ppinpix.Hotkeys");
        _window.Message += OnMessage;
    }

    /// <summary>某个热键被按下。参数为热键名称（如 "Capture"）。</summary>
    public event Action<string>? Pressed;

    public IReadOnlyCollection<RegisteredHotkey> Registered => _byId.Values;

    public bool IsSuspended { get; private set; }

    /// <summary>注册一批热键；已经注册的全部先注销。返回每个热键的结果，失败的不会中断其余注册。</summary>
    public IReadOnlyList<HotkeyRegistrationResult> RegisterAll(IEnumerable<(string Name, string GestureText)> hotkeys)
    {
        UnregisterAll();

        var results = new List<HotkeyRegistrationResult>();

        foreach ((string name, string text) in hotkeys)
        {
            if (!HotkeyGesture.TryParse(text, out var gesture) || gesture.IsEmpty)
            {
                results.Add(new HotkeyRegistrationResult(name, HotkeyGesture.None, HotkeyRegistrationStatus.Invalid));
                continue;
            }

            results.Add(Register(name, gesture));
        }

        IsSuspended = false;
        return results;
    }

    public HotkeyRegistrationResult Register(string name, HotkeyGesture gesture)
    {
        int id = _nextId++;
        uint modifiers = (uint)(gesture.Modifiers | HotkeyModifiers.NoRepeat);

        if (HotkeyApi.RegisterHotKey(_window.Handle, id, modifiers, (uint)gesture.VirtualKey))
        {
            var hotkey = new RegisteredHotkey(name, gesture, id);
            _byId[id] = hotkey;
            _byName[name] = hotkey;
            return new HotkeyRegistrationResult(name, gesture, HotkeyRegistrationStatus.Registered);
        }

        int error = Marshal.GetLastWin32Error();
        var status = error == ERROR_HOTKEY_ALREADY_REGISTERED
            ? HotkeyRegistrationStatus.AlreadyTaken
            : HotkeyRegistrationStatus.Failed;

        Log.Warn($"注册全局热键失败：{name} = {gesture}，Win32 错误码 {error}。");
        return new HotkeyRegistrationResult(name, gesture, status, error);
    }

    public void Unregister(string name)
    {
        if (!_byName.Remove(name, out var hotkey)) return;
        _byId.Remove(hotkey.Id);
        HotkeyApi.UnregisterHotKey(_window.Handle, hotkey.Id);
    }

    public void UnregisterAll()
    {
        foreach (var hotkey in _byId.Values)
        {
            HotkeyApi.UnregisterHotKey(_window.Handle, hotkey.Id);
        }
        _byId.Clear();
        _byName.Clear();
        _nextId = HotkeyIdBase;
    }

    /// <summary>临时停用全部热键（例如正在录制新快捷键，或用户选择暂停）。</summary>
    public void Suspend()
    {
        if (IsSuspended) return;

        foreach (var hotkey in _byId.Values)
        {
            HotkeyApi.UnregisterHotKey(_window.Handle, hotkey.Id);
        }

        IsSuspended = true;
        Log.Info("已暂停全部全局热键。");
    }

    public void Resume()
    {
        if (!IsSuspended) return;

        foreach (var hotkey in _byId.Values)
        {
            uint modifiers = (uint)(hotkey.Gesture.Modifiers | HotkeyModifiers.NoRepeat);
            if (!HotkeyApi.RegisterHotKey(_window.Handle, hotkey.Id, modifiers, (uint)hotkey.Gesture.VirtualKey))
            {
                Log.Warn($"恢复热键失败：{hotkey.Name} = {hotkey.Gesture}");
            }
        }

        IsSuspended = false;
        Log.Info("已恢复全部全局热键。");
    }

    private void OnMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg != WindowApi.WM_HOTKEY) return;

        int id = (int)wParam;
        if (_byId.TryGetValue(id, out var hotkey))
        {
            Pressed?.Invoke(hotkey.Name);
        }
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.Message -= OnMessage;
        _window.Dispose();
    }
}

internal static class HotkeyApi
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(nint hWnd, int id);
}
