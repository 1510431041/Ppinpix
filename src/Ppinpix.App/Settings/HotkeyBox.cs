using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ppinpix.Core.Settings;

namespace Ppinpix.App.Settings;

/// <summary>可视化快捷键录制控件，点击后监听组合键按下、拦截单独修饰键与 Esc、支持退格清除。</summary>
public sealed class HotkeyBox : Border
{
    private readonly TextBlock _text;
    private bool _recording;
    private string _gestureText = string.Empty;

    public HotkeyBox()
    {
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(12, 0, 12, 0);
        MinWidth = 190;
        MinHeight = 32;
        Cursor = Cursors.Hand;
        Focusable = true;

        _text = new TextBlock
        {
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Child = _text;

        SetResourceReference(BackgroundProperty, "Pp.Settings.Input");
        SetResourceReference(BorderBrushProperty, "Pp.Settings.InputBorder");
        _text.SetResourceReference(TextBlock.ForegroundProperty, "Pp.Settings.Text");

        MouseLeftButtonDown += (_, e) =>
        {
            Focus();
            StartRecording();
            e.Handled = true;
        };

        LostKeyboardFocus += (_, _) => StopRecording(cancel: true);
        PreviewKeyDown += OnKeyRecorded;
        UpdateVisual();
    }

    public string GestureText
    {
        get => _gestureText;
        set
        {
            _gestureText = value ?? string.Empty;
            UpdateVisual();
        }
    }

    public event Action<string>? GestureChanged;

    private void StartRecording()
    {
        _recording = true;
        UpdateVisual();
    }

    private void StopRecording(bool cancel)
    {
        if (!_recording) return;
        _recording = false;
        _ = cancel;
        UpdateVisual();
    }

    private void OnKeyRecorded(object sender, KeyEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            StopRecording(cancel: true);
            return;
        }

        if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            _gestureText = string.Empty;
            _recording = false;
            UpdateVisual();
            GestureChanged?.Invoke(_gestureText);
            return;
        }

        if (IsModifierKey(key))
        {
            return;
        }

        var mods = HotkeyModifiers.None;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) mods |= HotkeyModifiers.Control;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) mods |= HotkeyModifiers.Alt;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) mods |= HotkeyModifiers.Shift;
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) mods |= HotkeyModifiers.Win;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;

        var gesture = new HotkeyGesture(mods, (int)vk);
        _gestureText = gesture.ToString();
        _recording = false;
        UpdateVisual();
        GestureChanged?.Invoke(_gestureText);
    }

    private void UpdateVisual()
    {
        if (_recording)
        {
            _text.Text = "请按下新快捷键（Esc 取消）…";
            SetResourceReference(BorderBrushProperty, "Pp.Accent");
        }
        else
        {
            _text.Text = string.IsNullOrWhiteSpace(_gestureText) ? "未绑定" : _gestureText;
            SetResourceReference(BorderBrushProperty, "Pp.Settings.InputBorder");
        }
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin;
}
