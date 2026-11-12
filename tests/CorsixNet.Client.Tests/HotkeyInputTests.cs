using CorsixNet.Client.Input;
using CorsixNet.Content.Settings;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Tests;

public sealed class HotkeyInputTests
{
    [Theory]
    [InlineData("a", Keys.A)]
    [InlineData("Z", Keys.Z)]
    [InlineData("0", Keys.D0)]
    [InlineData("9", Keys.D9)]
    [InlineData("f1", Keys.F1)]
    [InlineData("f12", Keys.F12)]
    [InlineData("=", Keys.OemPlus)]
    [InlineData("-", Keys.OemMinus)]
    [InlineData("return", Keys.Enter)]
    [InlineData("escape", Keys.Escape)]
    public void MapsTheFilesKeyNames(string name, Keys key) => Assert.Equal(key, HotkeyInput.ToKeys(name));

    [Theory]
    [InlineData("a")]
    [InlineData("9")]
    [InlineData("f12")]
    [InlineData("=")]
    [InlineData("return")]
    [InlineData("right")]
    public void NamesTheKeysItMaps(string name) => Assert.Equal(name, HotkeyInput.KeyName(HotkeyInput.ToKeys(name)!.Value));

    [Fact]
    public void CapturesTheNewKeyWithTheModifiersHeld()
    {
        var before = new KeyboardState(Keys.LeftControl);

        Assert.Null(HotkeyInput.Captured(before, default));
        Assert.Equal(new Hotkey("k", Ctrl: true), HotkeyInput.Captured(new KeyboardState(Keys.LeftControl, Keys.K), before));
        Assert.Null(HotkeyInput.Captured(new KeyboardState(Keys.K), new KeyboardState(Keys.K)));
        Assert.Null(HotkeyInput.Captured(new KeyboardState(Keys.PrintScreen), default));
    }

    [Fact]
    public void EveryDefaultKeyIsOneTheKeyboardHas()
    {
        foreach (var (_, key) in Hotkeys.Defaults)
        {
            Assert.NotNull(HotkeyInput.ToKeys(key.Key));
        }
    }

    [Fact]
    public void AHotkeyNeedsExactlyItsModifiers()
    {
        var hotkey = new Hotkey("h", Ctrl: true);
        bool Pressed(Keys key) => key == Keys.H;

        Assert.True(HotkeyInput.Pressed(hotkey, new KeyboardState(Keys.LeftControl, Keys.H), Pressed));
        Assert.False(HotkeyInput.Pressed(hotkey, new KeyboardState(Keys.H), Pressed));
        Assert.False(HotkeyInput.Pressed(hotkey, new KeyboardState(Keys.LeftControl, Keys.LeftShift, Keys.H), Pressed));
        Assert.False(HotkeyInput.Pressed(new Hotkey("h"), new KeyboardState(Keys.RightControl, Keys.H), Pressed));
        Assert.True(HotkeyInput.Held(new Hotkey("h"), new KeyboardState(Keys.H)));
        Assert.False(HotkeyInput.Pressed(null, new KeyboardState(Keys.H), Pressed));
    }
}
