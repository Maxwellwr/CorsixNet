// The new game dialog, ported from CorsixTH CorsixTH/Lua/dialogs/resizables/new_game.lua (UINewGame): the original
// campaign from its first level, at a difficulty whose level files are installed. Texts are CorsixTH's
// (languages/english.lua, new_game_window).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Economy;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// <c>UINewGame</c>: the player's name, a difficulty, the tutorial (off at first, as <c>start_tutorial = false</c>) and
/// Start, which begins the campaign at its first level.
/// </summary>
/// <param name="available">The difficulties whose level files are installed, easiest first.</param>
/// <param name="playerName">The player's name from the settings.</param>
/// <param name="start">Starts the campaign at a difficulty, with the tutorial or not.</param>
internal sealed class NewGameWindow(IReadOnlyList<Difficulty> available, string playerName, Action<Difficulty, bool> start) : UiWindow
{
    /// <summary>The difficulty chosen: medium when it is there, as CorsixTH picks.</summary>
    public Difficulty Selected { get; private set; } = available.Contains(Difficulty.Medium) ? Difficulty.Medium : available.FirstOrDefault(Difficulty.Medium);

    public override string Title => "Campaign";

    /// <summary>Whether the tutorial starts with the first level (<c>buttonTutorial</c>).</summary>
    public bool Tutorial { get; private set; }

    /// <summary>The name of a difficulty, as the new game dialog shows it.</summary>
    public static string Name(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => "Junior (Easy)",
        Difficulty.Hard => "Consultant (Hard)",
        _ => "Doctor (Medium)",
    };

    /// <summary>The difficulty's tooltip, shown under the choice.</summary>
    public static string Description(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => "If you are new to simulation games this is the option for you",
        Difficulty.Hard => "If you are used to this kind of game and want more of a challenge, pick this option",
        _ => "This is the middle way to go if you are unsure what to choose",
    };

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

        yield return WindowRow.Of(Text("player_name", "Player name"), string.IsNullOrEmpty(playerName) ? "(player_name in config.txt)" : playerName);
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
                host.Close(this);
                start(Selected, Tutorial);
            }, enabled: available.Count > 0).WithTooltip(Tip("start", "Start the game with the chosen settings")),
            WindowCell.Button(Text("cancel", "Cancel"), () => host.Close(this)).WithTooltip(Tip("cancel", "Oh, I didn't really mean to start a new game!")));
    }
}
