// The new game dialog, ported from CorsixTH CorsixTH/Lua/dialogs/resizables/new_game.lua (UINewGame): the original
// campaign from its first level, at a difficulty whose level files are installed. Texts are CorsixTH's
// (languages/english.lua, new_game_window).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Content.Settings;
using CorsixNet.Sim.Economy;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// <c>UINewGame</c>: the player's name in a text box, a difficulty, the tutorial (off at first, as
/// <c>start_tutorial = false</c>) and Start, which begins the campaign at its first level. A click on the name types a
/// new one (letters, digits, space, hyphen and plus, up to 15); Return or Start keeps it and saves it in the settings,
/// unless it is blank, Escape puts the old one back.
/// </summary>
/// <param name="available">The difficulties whose level files are installed, easiest first.</param>
/// <param name="playerName">The player's name from the settings.</param>
/// <param name="start">Starts the campaign at a difficulty, with the tutorial or not.</param>
/// <param name="saveName">Saves a new name in the settings (<c>UINewGame:saveToConfig</c>).</param>
internal sealed class NewGameWindow(IReadOnlyList<Difficulty> available, string playerName, Action<Difficulty, bool> start, Action<string>? saveName = null)
    : UiWindow, ITextInputWindow
{
    /// <summary>The name box's <c>characterLimit</c>.</summary>
    public const int NameLimit = 15;

    private string _typed = string.Empty;

    /// <summary>
    /// The player's name: the settings' one, else the system's user name, else PLAYER, as <c>UINewGame</c> starts it.
    /// </summary>
    public string PlayerName { get; private set; } = DefaultName(playerName);

    /// <summary>Whether the name is being typed.</summary>
    public bool IsCapturing { get; private set; }

    /// <summary>The name as typed so far.</summary>
    public string TypedName => _typed;

    /// <summary>The difficulty chosen: medium when it is there, as CorsixTH picks.</summary>
    public Difficulty Selected { get; private set; } = available.Contains(Difficulty.Medium) ? Difficulty.Medium : available.FirstOrDefault(Difficulty.Medium);

    public override string Title => UiText.Get("new_game_window.caption", "Campaign");

    /// <summary>Whether the tutorial starts with the first level (<c>buttonTutorial</c>).</summary>
    public bool Tutorial { get; private set; }

    /// <summary>The name of a difficulty, as the new game dialog shows it.</summary>
    public static string Name(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => UiText.Get("new_game_window.easy", "Junior (Easy)"),
        Difficulty.Hard => UiText.Get("new_game_window.hard", "Consultant (Hard)"),
        _ => UiText.Get("new_game_window.medium", "Doctor (Medium)"),
    };

    /// <summary>The difficulty's tooltip, shown under the choice.</summary>
    public static string Description(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => UiText.Get("tooltip.new_game_window.easy", "If you are new to simulation games this is the option for you"),
        Difficulty.Hard => UiText.Get("tooltip.new_game_window.hard", "If you are used to this kind of game and want more of a challenge, pick this option"),
        _ => UiText.Get("tooltip.new_game_window.medium", "This is the middle way to go if you are unsure what to choose"),
    };

    private static string DefaultName(string name) =>
        name is { Length: > 0 } ? name
        : Environment.GetEnvironmentVariable("USER") is { Length: > 0 } user ? user
        : Environment.GetEnvironmentVariable("USERNAME") is { Length: > 0 } userName ? userName
        : "PLAYER";

    /// <summary>Starts typing a name in the box, as a click on it does.</summary>
    public void StartTyping()
    {
        _typed = PlayerName;
        IsCapturing = true;
    }

    /// <summary>
    /// The text box's confirm: a blank name puts the old one back; another is kept and saved.
    /// </summary>
    public void ConfirmName()
    {
        if (!IsCapturing)
        {
            return;
        }

        IsCapturing = false;
        if (_typed.Trim().Length > 0 && _typed != PlayerName)
        {
            PlayerName = _typed;
            saveName?.Invoke(PlayerName);
        }
    }

    public void Capture(Hotkey key)
    {
        switch (key.Key)
        {
            case "escape":
                IsCapturing = false;
                break;
            case "backspace":
                _typed = _typed.Length > 0 ? _typed[..^1] : _typed;
                break;
            case "return":
                ConfirmName();
                break;
        }
    }

    /// <summary><c>Textbox:input</c> with <c>alpha</c>, <c>numbers</c> and <c>misc</c>: ASCII letters and digits, space, hyphen and plus.</summary>
    public void Type(char character)
    {
        if (IsCapturing && _typed.Length < NameLimit && (char.IsAsciiLetterOrDigit(character) || character is ' ' or '-' or '+'))
        {
            _typed += character;
        }
    }

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        // new_game_window.* and tooltip.new_game_window.* in CorsixTH's language files.
        string Text(string key, string fallback) => CorsixText.Get(host, "new_game_window." + key, fallback);
        string Tip(string key, string fallback) => CorsixText.Get(host, "tooltip.new_game_window." + key, fallback);
        string Key(Difficulty difficulty) => difficulty switch
        {
            Difficulty.Easy => "easy",
            Difficulty.Hard => "hard",
            _ => "medium",
        };

        yield return WindowRow.Of(
            Text("player_name", "Player name"),
            WindowCell.Button(IsCapturing ? _typed + "_" : PlayerName, () =>
            {
                if (IsCapturing)
                {
                    ConfirmName();
                }
                else
                {
                    StartTyping();
                }
            }, highlight: IsCapturing).WithTooltip(Tip("player_name", "Enter the name you wish to be referred to as in the game")));
        yield return WindowRow.Of(
            [
                Text("difficulty", "Difficulty"),
                .. available.Select(difficulty => WindowCell.Button(Text(Key(difficulty), Name(difficulty)), () => Selected = difficulty, highlight: difficulty == Selected)
                    .WithTooltip(Tip(Key(difficulty), Description(difficulty)))),
            ]);
        yield return WindowCell.Line(Tip(Key(Selected), Description(Selected)), dim: true);
        yield return WindowRow.Of(
            Text("tutorial", "Tutorial"),
            WindowCell.Button(Tutorial ? Text("option_on", "On") : Text("option_off", "Off"), () => Tutorial = !Tutorial, highlight: Tutorial)
                .WithTooltip(Tip("tutorial", "Click this box if you want some help to get started in the game")));
        yield return WindowRow.Gap;
        yield return WindowRow.Of(
            WindowCell.Button(Text("start", "Start"), () =>
            {
                ConfirmName();
                host.Close(this);
                start(Selected, Tutorial);
            }, enabled: available.Count > 0).WithTooltip(Tip("start", "Start the game with the chosen settings")),
            WindowCell.Button(Text("cancel", "Cancel"), () =>
            {
                ConfirmName();
                host.Close(this);
            }).WithTooltip(Tip("cancel", "Oh, I didn't really mean to start a new game!")));
    }
}
