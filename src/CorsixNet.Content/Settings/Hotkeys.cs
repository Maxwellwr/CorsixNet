// The hotkeys file, after CorsixTH CorsixTH/Lua/config_finder.lua (hotkeys.txt: load_hotkeys, save_hotkeys,
// new_hotkeys_defaults) and dialogs/resizables/hotkey_assign.lua (hotkey_input). The names are CorsixTH's; the default
// keys are this game's own where it differs.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Collections.Frozen;
using System.Text;
using CorsixNet.Formats;

namespace CorsixNet.Content.Settings;

/// <summary>A key with the modifier keys held with it, such as <c>f1</c> or Ctrl+H, by CorsixTH's key names.</summary>
public sealed record Hotkey(string Key, bool Ctrl = false, bool Shift = false, bool Alt = false)
{
    /// <summary>How the hotkeys file writes it: <c>"f1"</c>, or <c>{"ctrl", "h"}</c> with modifiers.</summary>
    public string ToLua()
    {
        if (!Ctrl && !Shift && !Alt)
        {
            return $"\"{Key}\"";
        }

        var parts = new List<string>();
        if (Ctrl)
        {
            parts.Add("\"ctrl\"");
        }

        if (Shift)
        {
            parts.Add("\"shift\"");
        }

        if (Alt)
        {
            parts.Add("\"alt\"");
        }

        parts.Add($"\"{Key}\"");
        return "{" + string.Join(", ", parts) + "}";
    }

    /// <summary>As shown to the player, such as F1 or Ctrl+H.</summary>
    public override string ToString()
    {
        var text = new StringBuilder();
        if (Ctrl)
        {
            text.Append("Ctrl+");
        }

        if (Shift)
        {
            text.Append("Shift+");
        }

        if (Alt)
        {
            text.Append("Alt+");
        }

        text.Append(Key.Length == 1 ? Key.ToUpperInvariant() : char.ToUpperInvariant(Key[0]) + Key[1..]);
        return text.ToString();
    }
}

/// <summary>
/// The hotkeys (CorsixTH's <c>hotkeys.txt</c>): an action's name, such as <c>ingame_panel_bankManager</c>, to its key.
/// Actions the file leaves out, or gives something that is not a key, keep their defaults.
/// </summary>
public sealed class Hotkeys
{
    /// <summary>The hotkeys file's name, next to the settings file.</summary>
    public const string FileName = "hotkeys.txt";

    private static readonly string[] Modifiers = ["ctrl", "shift", "alt"];

    /// <summary>The actions that can be given keys, with their default keys, in the file's order.</summary>
    public static readonly IReadOnlyList<(string Name, Hotkey Key)> Defaults =
    [
        ("ingame_showCheatWindow", new("f11")),
        ("ingame_pause", new("p")),
        ("ingame_gamespeed_slowest", new("1")),
        ("ingame_gamespeed_slower", new("2")),
        ("ingame_gamespeed_normal", new("3")),
        ("ingame_gamespeed_max", new("4")),
        ("ingame_gamespeed_thensome", new("5")),
        ("ingame_gamespeed_speedup", new("z")),
        ("ingame_scroll_up", new("up")),
        ("ingame_scroll_down", new("down")),
        ("ingame_scroll_left", new("left")),
        ("ingame_scroll_right", new("right")),
        ("ingame_zoom_in", new("=")),
        ("ingame_zoom_in_more", new("=", Shift: true)),
        ("ingame_zoom_out", new("-")),
        ("ingame_zoom_out_more", new("-", Shift: true)),
        ("ingame_reset_zoom", new("0")),
        ("ingame_panel_bankManager", new("f1")),
        ("ingame_panel_bankStats", new("f2")),
        ("ingame_panel_staffManage", new("f3", Shift: true)),
        ("ingame_panel_townMap", new("f4")),
        ("ingame_panel_casebook", new("f5")),
        ("ingame_panel_research", new("f6")),
        ("ingame_panel_status", new("f7")),
        ("ingame_panel_charts", new("f8")),
        ("ingame_panel_policy", new("f9")),
        ("ingame_panel_machineMenu", new("f10")),
        ("ingame_panel_adviserHistory", new("h", Ctrl: true)),
        ("ingame_panel_hireStaff", new("y")),
        ("ingame_panel_buildRoom", new("b")),
        ("ingame_openFirstMessage", new("c")),
        ("ingame_setTransparent", new("x")),
        ("ingame_toggleTransparent", new("x", Shift: true)),
        ("ingame_toggleAdvisor", new("a", Shift: true)),
        ("ingame_quickSave", new("s", Shift: true, Alt: true)),
        ("ingame_quickLoad", new("l", Shift: true, Alt: true)),
        ("ingame_storePosition_1", new("1", Alt: true)),
        ("ingame_storePosition_2", new("2", Alt: true)),
        ("ingame_storePosition_3", new("3", Alt: true)),
        ("ingame_storePosition_4", new("4", Alt: true)),
        ("ingame_storePosition_5", new("5", Alt: true)),
        ("ingame_storePosition_6", new("6", Alt: true)),
        ("ingame_storePosition_7", new("7", Alt: true)),
        ("ingame_storePosition_8", new("8", Alt: true)),
        ("ingame_storePosition_9", new("9", Alt: true)),
        ("ingame_storePosition_0", new("0", Alt: true)),
        ("ingame_recallPosition_1", new("1", Ctrl: true)),
        ("ingame_recallPosition_2", new("2", Ctrl: true)),
        ("ingame_recallPosition_3", new("3", Ctrl: true)),
        ("ingame_recallPosition_4", new("4", Ctrl: true)),
        ("ingame_recallPosition_5", new("5", Ctrl: true)),
        ("ingame_recallPosition_6", new("6", Ctrl: true)),
        ("ingame_recallPosition_7", new("7", Ctrl: true)),
        ("ingame_recallPosition_8", new("8", Ctrl: true)),
        ("ingame_recallPosition_9", new("9", Ctrl: true)),
        ("ingame_recallPosition_0", new("0", Ctrl: true)),
    ];

    private static readonly FrozenDictionary<string, Hotkey> DefaultMap = Defaults.ToFrozenDictionary(pair => pair.Name, pair => pair.Key, StringComparer.Ordinal);

    private readonly Dictionary<string, Hotkey> _keys;

    /// <summary>The defaults.</summary>
    public Hotkeys()
        : this(new Dictionary<string, Hotkey>(DefaultMap, StringComparer.Ordinal))
    {
    }

    private Hotkeys(Dictionary<string, Hotkey> keys) => _keys = keys;

    /// <summary>The key for an action, or null for an action that has none.</summary>
    public Hotkey? this[string action] => _keys.GetValueOrDefault(action);

    /// <summary>A copy to change, such as the hotkey assignment dialog's.</summary>
    public Hotkeys Copy() => new(new Dictionary<string, Hotkey>(_keys, StringComparer.Ordinal));

    /// <summary>Whether two sets give every action the same key.</summary>
    public bool SameAs(Hotkeys other) => _keys.All(pair => other._keys.TryGetValue(pair.Key, out var key) && key == pair.Value);

    /// <summary>
    /// <c>hotkey_input</c>: gives an action a key. Escape and Return on their own are kept for closing and confirming,
    /// and are refused; the scroll keys lose their modifiers. An action that had the key already gets this action's old
    /// one, so no key does two things.
    /// </summary>
    /// <returns>False when the key is refused and nothing changed.</returns>
    /// <exception cref="ArgumentException">The action is not one that takes a key.</exception>
    public bool Assign(string action, Hotkey key)
    {
        if (!_keys.TryGetValue(action, out var old))
        {
            throw new ArgumentException($"There is no hotkey action {action}.", nameof(action));
        }

        if (!key.Ctrl && !key.Shift && !key.Alt && key.Key is "escape" or "return")
        {
            return false;
        }

        if (action.StartsWith("ingame_scroll_", StringComparison.Ordinal))
        {
            key = new Hotkey(key.Key);
        }

        foreach (var (other, otherKey) in _keys)
        {
            if (other != action && otherKey == key)
            {
                _keys[other] = old;
                break;
            }
        }

        _keys[action] = key;
        return true;
    }

    /// <summary>
    /// <c>load_hotkeys</c>: reads the hotkeys file, writing one with the defaults first if there is none.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a hotkeys file.</exception>
    public static Hotkeys Load(string path)
    {
        if (!File.Exists(path))
        {
            var defaults = new Hotkeys();
            try
            {
                defaults.Save(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // As CorsixTH, carry on with the defaults when the file can't be written.
            }

            return defaults;
        }

        return Parse(File.ReadAllText(path));
    }

    /// <summary>Reads the hotkeys from a hotkeys file's text.</summary>
    /// <exception cref="InvalidDataException">The text is not a hotkeys file.</exception>
    public static Hotkeys Parse(string text)
    {
        var values = LuaAssignments.Parse(text);
        var keys = new Dictionary<string, Hotkey>(DefaultMap, StringComparer.Ordinal);
        foreach (var (name, value) in values)
        {
            if (DefaultMap.ContainsKey(name) && ToHotkey(value) is { } hotkey)
            {
                keys[name] = hotkey;
            }
        }

        return new Hotkeys(keys);
    }

    /// <summary><c>save_hotkeys</c>: writes every action's key.</summary>
    public void Save(string path)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } folder)
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllText(path, ToText());
    }

    /// <summary>The hotkeys file's text.</summary>
    public string ToText()
    {
        var text = new StringBuilder();
        text.Append("""
            ----------------------------------------------- Hotkeys ---------------------------------------------------
            -- Each action takes a key, such as "f1", or a table with modifiers first, such as {"ctrl", "h"}.
            -- Keys are letters, digits, f1 to f12, and escape, return, space, tab, delete, up, down, left, right, = and -.
            -- Modifiers are ctrl, shift and alt. Delete a line to get its default back.

            """);
        foreach (var (name, _) in Defaults)
        {
            text.Append(name).Append(" = ").Append(_keys[name].ToLua()).Append('\n');
        }

        return text.ToString();
    }

    private static Hotkey? ToHotkey(object? value)
    {
        switch (value)
        {
            case string key when IsKeyName(key):
                return new Hotkey(key.ToLowerInvariant());
            case LuaTable { Fields.Count: 0, Items.Count: > 0 } table when table.Items.All(item => item is string):
                var parts = table.Items.Cast<string>().Select(part => part.ToLowerInvariant()).ToList();
                var last = parts[^1];
                var modifiers = parts[..^1];
                if (!IsKeyName(last) || modifiers.Any(m => !Modifiers.Contains(m)))
                {
                    return null;
                }

                return new Hotkey(last, modifiers.Contains("ctrl"), modifiers.Contains("shift"), modifiers.Contains("alt"));
            default:
                return null;
        }
    }

    /// <summary>Whether the name is a key this game knows (see the file's header).</summary>
    public static bool IsKeyName(string key)
    {
        var name = key.ToLowerInvariant();
        return (name.Length == 1 && (char.IsAsciiLetterLower(name[0]) || char.IsAsciiDigit(name[0]) || name is "=" or "-"))
            || name is "escape" or "return" or "space" or "tab" or "delete" or "up" or "down" or "left" or "right"
            || (name.Length is 2 or 3 && name[0] == 'f' && int.TryParse(name[1..], out var number) && number is >= 1 and <= 12);
    }
}
