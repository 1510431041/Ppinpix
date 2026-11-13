using Ppinpix.Core.Settings;
using Xunit;

namespace Ppinpix.Core.Tests;

public sealed class HotkeyGestureTests
{
    [Theory]
    [InlineData("F1", HotkeyModifiers.None, 0x70, "F1")]
    [InlineData("Ctrl+Shift+T", HotkeyModifiers.Control | HotkeyModifiers.Shift, 'T', "Ctrl+Shift+T")]
    [InlineData("ctrl + alt + f12", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x7B, "Ctrl+Alt+F12")]
    [InlineData("Win+Shift+S", HotkeyModifiers.Win | HotkeyModifiers.Shift, 'S', "Shift+Win+S")]
    public void TryParse_ValidStrings_ShouldParseAndFormat(
        string input,
        HotkeyModifiers expectedMods,
        int expectedVk,
        string expectedFormatted)
    {
        Assert.True(HotkeyGesture.TryParse(input, out var gesture));
        Assert.Equal(expectedMods, gesture.Modifiers);
        Assert.Equal(expectedVk, gesture.VirtualKey);
        Assert.Equal(expectedFormatted, gesture.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+A+B")]
    [InlineData("UnknownKey+A")]
    public void TryParse_InvalidStrings_ShouldReturnFalse(string input)
    {
        Assert.False(HotkeyGesture.TryParse(input, out _));
    }
}
