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
        yield return WindowRow.Of("Player name", string.IsNullOrEmpty(playerName) ? "(player_name in config.txt)" : playerName);
        yield return WindowRow.Of(
            [
                "Difficulty",
                .. available.Select(difficulty => WindowCell.Button(Name(difficulty), () => Selected = difficulty, highlight: difficulty == Selected).WithTooltip(Description(difficulty))),
            ]);
        yield return WindowCell.Line(Description(Selected), dim: true);
        yield return WindowRow.Of(
            "Tutorial",
            WindowCell.Button(Tutorial ? "On" : "Off", () => Tutorial = !Tutorial, highlight: Tutorial)
                .WithTooltip("Click this box if you want some help to get started in the game"));
        yield return WindowRow.Gap;
        yield return WindowRow.Of(
            WindowCell.Button("Start", () =>
            {
                host.Close(this);
                start(Selected, Tutorial);
            }, enabled: available.Count > 0).WithTooltip("Start the game with the chosen settings"),
            WindowCell.Button("Cancel", () => host.Close(this)).WithTooltip("Oh, I didn't really mean to start a new game!"));
    }
}
