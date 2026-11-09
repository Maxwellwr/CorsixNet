using CorsixNet.Content.Settings;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Input;

/// <summary>Matches the hotkeys file's keys against the keyboard.</summary>
internal static class HotkeyInput
{
    /// <summary>The MonoGame key for one of the hotkeys file's key names, or null for a name it does not know.</summary>
    public static Keys? ToKeys(string name)
    {
        var key = name.ToLowerInvariant();
        if (key.Length == 1)
        {
            return key[0] switch
            {
                >= 'a' and <= 'z' => Keys.A + (key[0] - 'a'),
                >= '0' and <= '9' => Keys.D0 + (key[0] - '0'),
                '=' => Keys.OemPlus,
                '-' => Keys.OemMinus,
                _ => null,
            };
        }

        if (key[0] == 'f' && int.TryParse(key[1..], out var number) && number is >= 1 and <= 12)
        {
            return Keys.F1 + (number - 1);
        }

        return key switch
        {
            "escape" => Keys.Escape,
            "return" => Keys.Enter,
            "space" => Keys.Space,
            "tab" => Keys.Tab,
            "delete" => Keys.Delete,
            "up" => Keys.Up,
            "down" => Keys.Down,
            "left" => Keys.Left,
            "right" => Keys.Right,
            _ => null,
        };
    }

    /// <summary>Whether the hotkey's modifiers, and only those, are held.</summary>
    public static bool ModifiersHeld(Hotkey hotkey, KeyboardState keys) =>
        hotkey.Ctrl == (keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl))
        && hotkey.Shift == (keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift))
        && hotkey.Alt == (keys.IsKeyDown(Keys.LeftAlt) || keys.IsKeyDown(Keys.RightAlt));

    /// <summary>Whether the hotkey was pressed this frame, with exactly its modifiers held.</summary>
    /// <param name="pressed">Whether a key went down this frame.</param>
    public static bool Pressed(Hotkey? hotkey, KeyboardState keys, Func<Keys, bool> pressed) =>
        hotkey is not null && ToKeys(hotkey.Key) is { } key && pressed(key) && ModifiersHeld(hotkey, keys);

    /// <summary>Whether the hotkey is held down, with exactly its modifiers.</summary>
    public static bool Held(Hotkey? hotkey, KeyboardState keys) =>
        hotkey is not null && ToKeys(hotkey.Key) is { } key && keys.IsKeyDown(key) && ModifiersHeld(hotkey, keys);
}
