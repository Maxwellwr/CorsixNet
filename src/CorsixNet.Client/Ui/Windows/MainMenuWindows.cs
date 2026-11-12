// The main menu and its custom game lists, ported from CorsixTH CorsixTH/Lua/dialogs/resizables/main_menu.lua (UIMainMenu),
// file_browsers/custom_game.lua (UICustomGame) and custom_campaign.lua (UICustomCampaign). Texts are CorsixTH's
// (languages/english.lua: main_menu, tooltip.main_menu, custom_game_window, custom_campaign_window).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Content.Levels;
using Microsoft.Xna.Framework;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// <c>UIMainMenu</c>: shown when the game starts, over the first level, paused; Esc does not close it
/// (<c>esc_closes = false</c>). Its items go to <paramref name="action"/> by name: <c>newgame</c>, <c>campaign</c>,
/// <c>scenario</c>, <c>continue</c>, <c>load</c>, <c>options</c>, <c>mapeditor</c> and <c>exit</c>.
/// </summary>
internal sealed class MainMenuWindow(Action<string> action, string version) : UiWindow
{
    public override string Title => "Main menu";

    public override bool IsPinned => true;

    /// <summary>A quarter down the screen, as <c>setDefaultPosition(0.5, 0.25)</c>.</summary>
    public override Vector2? DefaultPosition => new(0.5f, 0.25f);

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        yield return Item("new_game", "Campaign", "newgame", "Start the first level on the campaign");
        yield return Item("custom_campaign", "Custom Campaign", "campaign", "Play a campaign created by the community");
        yield return Item("custom_level", "Single Scenario", "scenario", "Build your hospital in a single scenario");
        yield return Item("continue", "Continue Game", "continue", "Continue your latest saved game");
        yield return Item("load_game", "Load Game", "load", "Load a saved game");
        yield return Item("options", "Settings", "options", "Tweak various settings");
        yield return Item("map_edit", "Map Editor", "mapeditor", "Create a custom map");
        yield return Item("exit", "Exit", "exit", "No, no, please don't leave!");
        yield return WindowRow.Gap;
        yield return WindowCell.Line(CorsixText.Get(host, "main_menu.version", "Version: ") + version, dim: true);

        // main_menu.* and tooltip.main_menu.* in CorsixTH's language files.
        WindowRow Item(string key, string text, string name, string tooltip) =>
            WindowRow.Of(WindowCell.Button(CorsixText.Get(host, "main_menu." + key, text), () => action(name)) with
            {
                Tooltip = CorsixText.Get(host, "tooltip.main_menu." + key, tooltip),
            });
    }
}

/// <summary>
/// <c>UICustomGame</c>: the player's own levels; a click on one shows its briefing, Start plays it, with Free Build if
/// ticked.
/// </summary>
internal sealed class CustomLevelWindow(IReadOnlyList<CustomLevelEntry> levels, bool freeBuild, Action<CustomLevelEntry, bool> start) : UiWindow
{
    private CustomLevelEntry? _selected = levels.Count > 0 ? levels[0] : null;
    private bool _freeBuild = freeBuild;

    public override string Title => "Custom Game";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        if (levels.Count == 0)
        {
            yield return WindowCell.Line("No levels found: put .level and .map files in the levels folder (levels in config.txt).", dim: true);
        }

        foreach (var level in levels)
        {
            yield return WindowRow.Of(WindowCell.Button(level.Info.Name, () => _selected = level, highlight: level == _selected) with { Tooltip = "Click a level to read more about it" });
        }

        if (_selected?.Info.Briefing is { Length: > 0 } briefing)
        {
            yield return WindowRow.Gap;
            foreach (var line in BottomPanel.Wrap(briefing, 60))
            {
                yield return WindowCell.Line(line, dim: true);
            }
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(
            WindowCell.Button("Free Build", () => _freeBuild = !_freeBuild, highlight: _freeBuild) with { Tooltip = "Tick this box if you want to play without money or winning and losing conditions" },
            WindowCell.Button("Start", () =>
            {
                host.Close(this);
                start(_selected!, _freeBuild);
            }, enabled: _selected is not null) with
            { Tooltip = "Load and play the selected level" },
            WindowCell.Button("Cancel", () => host.Close(this)));
    }
}

/// <summary><c>UICustomCampaign</c>: the player's own campaigns; a click on one shows its description, Start plays its first level.</summary>
internal sealed class CustomCampaignWindow(IReadOnlyList<CustomCampaignEntry> campaigns, Action<CustomCampaignEntry> start) : UiWindow
{
    private CustomCampaignEntry? _selected = campaigns.Count > 0 ? campaigns[0] : null;

    public override string Title => "Custom Campaign";

    /// <summary>A campaign's name, or its file's when it gives none.</summary>
    public static string Name(CustomCampaignEntry entry) => entry.Campaign.Name ?? Path.GetFileNameWithoutExtension(entry.Path);

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        if (campaigns.Count == 0)
        {
            yield return WindowCell.Line("No campaigns found: put .campaign files in the campaigns folder (campaigns in config.txt).", dim: true);
        }

        foreach (var campaign in campaigns)
        {
            yield return WindowRow.Of(WindowCell.Button(Name(campaign), () => _selected = campaign, highlight: campaign == _selected) with { Tooltip = "Choose a campaign to read more about it" });
        }

        if (_selected?.Campaign.Description is { Length: > 0 } description)
        {
            yield return WindowRow.Gap;
            foreach (var line in BottomPanel.Wrap(description, 60))
            {
                yield return WindowCell.Line(line, dim: true);
            }
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(
            WindowCell.Button("Start campaign", () =>
            {
                host.Close(this);
                start(_selected!);
            }, enabled: _selected is not null) with
            { Tooltip = "Load the first level of this campaign" },
            WindowCell.Button("Cancel", () => host.Close(this)));
    }
}
