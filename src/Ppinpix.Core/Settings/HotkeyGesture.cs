namespace Ppinpix.Core.Settings;

/// <summary>全局快捷键。存的是虚拟键码，与 UI 框架无关。</summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    public bool IsEmpty => VirtualKey == 0;

    public static readonly HotkeyGesture None = new(HotkeyModifiers.None, 0);

    public override string ToString() => HotkeyFormatter.Format(this);

    /// <summary>解析 "Ctrl+Shift+T" / "F1" / "Win+Alt+P" 这类写法。</summary>
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        var modifiers = HotkeyModifiers.None;
        int vk = 0;

        foreach (string raw in parts)
        {
            string part = raw.Trim();
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= HotkeyModifiers.Control;
                    continue;
                case "SHIFT":
                    modifiers |= HotkeyModifiers.Shift;
                    continue;
                case "ALT":
                    modifiers |= HotkeyModifiers.Alt;
                    continue;
                case "WIN":
                case "WINDOWS":
                case "META":
                    modifiers |= HotkeyModifiers.Win;
                    continue;
            }

            if (vk != 0) return false; // 出现第二个主键
            if (!HotkeyFormatter.TryParseKey(part, out vk)) return false;
        }

        if (vk == 0) return false;

        gesture = new HotkeyGesture(modifiers, vk);
        return true;
    }

    public static HotkeyGesture ParseOrDefault(string? text, HotkeyGesture fallback) =>
        TryParse(text, out var g) ? g : fallback;
}

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,

    /// <summary>按住不放时不重复触发（MOD_NOREPEAT）。</summary>
    NoRepeat = 0x4000,
}

/// <summary>虚拟键码与可读文本之间的转换。仅覆盖快捷键常用的键，不追求完整键表。</summary>
internal static class HotkeyFormatter
{
    private static readonly Dictionary<string, int> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BACKSPACE"] = 0x08, ["TAB"] = 0x09, ["ENTER"] = 0x0D, ["RETURN"] = 0x0D,
        ["ESC"] = 0x1B, ["ESCAPE"] = 0x1B, ["SPACE"] = 0x20,
        ["PAGEUP"] = 0x21, ["PAGEDOWN"] = 0x22, ["END"] = 0x23, ["HOME"] = 0x24,
        ["LEFT"] = 0x25, ["UP"] = 0x26, ["RIGHT"] = 0x27, ["DOWN"] = 0x28,
        ["INSERT"] = 0x2D, ["DELETE"] = 0x2E, ["DEL"] = 0x2E,
        ["PRINTSCREEN"] = 0x2C,
        ["OEM_3"] = 0xC0, // `
        ["OEM_MINUS"] = 0xBD, ["OEM_PLUS"] = 0xBB,
        ["OEM_4"] = 0xDB, ["OEM_6"] = 0xDD,
        ["OEM_5"] = 0xDC, ["OEM_1"] = 0xBA, ["OEM_7"] = 0xDE,
        ["OEM_COMMA"] = 0xBC, ["OEM_PERIOD"] = 0xBE, ["OEM_2"] = 0xBF,
    };

    private static readonly Dictionary<int, string> NamedKeysReverse =
        BuildReverse();

    private static Dictionary<int, string> BuildReverse()
    {
        var map = new Dictionary<int, string>();
        foreach ((string name, int vk) in NamedKeys)
        {
            // 只保留首选名字（例如 ENTER 优先于 RETURN）。
            if (!map.ContainsKey(vk)) map[vk] = name;
        }
        map[0x0D] = "Enter";
        map[0x1B] = "Esc";
        map[0x2E] = "Delete";
        return map;
    }

    public static bool TryParseKey(string token, out int virtualKey)
    {
        virtualKey = 0;
        if (token.Length == 0) return false;

        // F1..F24
        if ((token[0] is 'F' or 'f') && int.TryParse(token.AsSpan(1), out int f) && f is >= 1 and <= 24)
        {
            virtualKey = 0x70 + f - 1;
            return true;
        }

        // 0-9
        if (token.Length == 1 && char.IsAsciiDigit(token[0]))
        {
            virtualKey = token[0];
            return true;
        }

        // A-Z
        if (token.Length == 1 && char.IsAsciiLetter(token[0]))
        {
            virtualKey = char.ToUpperInvariant(token[0]);
            return true;
        }

        if (NamedKeys.TryGetValue(token, out int named))
        {
            virtualKey = named;
            return true;
        }

        return false;
    }

    public static string FormatKey(int virtualKey)
    {
        if (NamedKeysReverse.TryGetValue(virtualKey, out string? name)) return name;
        if (virtualKey is >= 0x70 and <= 0x87) return $"F{virtualKey - 0x70 + 1}";
        if (virtualKey is >= 0x30 and <= 0x39) return ((char)virtualKey).ToString();
        if (virtualKey is >= 0x41 and <= 0x5A) return ((char)virtualKey).ToString();
        return $"VK{virtualKey:X2}";
    }

    public static string Format(HotkeyGesture gesture)
    {
        if (gesture.IsEmpty) return "未设置";

        var parts = new List<string>(4);
        if ((gesture.Modifiers & HotkeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((gesture.Modifiers & HotkeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((gesture.Modifiers & HotkeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((gesture.Modifiers & HotkeyModifiers.Win) != 0) parts.Add("Win");
        parts.Add(FormatKey(gesture.VirtualKey));
        return string.Join("+", parts);
    }
}
