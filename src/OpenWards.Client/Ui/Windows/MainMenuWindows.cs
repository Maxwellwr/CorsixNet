// The main menu and its custom game lists, ported from CorsixTH CorsixTH/Lua/dialogs/resizables/main_menu.lua (UIMainMenu),
// file_browsers/custom_game.lua (UICustomGame) and custom_campaign.lua (UICustomCampaign). Texts are CorsixTH's
// (languages/english.lua: main_menu, tooltip.main_menu, custom_game_window, custom_campaign_window).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using Microsoft.Xna.Framework;
using OpenWards.Content.Levels;

namespace OpenWards.Client.Ui.Windows;

/// <summary>
/// <c>UIMainMenu</c>: shown when the game starts, over the first level, paused; Esc does not close it
/// (<c>esc_closes = false</c>). Its items go to <paramref name="action"/> by name: <c>newgame</c>, <c>campaign</c>,
/// <c>scenario</c>, <c>continue</c>, <c>load</c>, <c>options</c>, <c>mapeditor</c> and <c>exit</c>.
/// </summary>
internal sealed class MainMenuWindow(Action<string> action, string version) : UiWindow
{
    /// <summary>A window CorsixTH added to the game, drawn in its own look.</summary>
    public override bool UsesResizableLook => true;

    public override string Title => UiText.Get("openwards.main_menu.caption", "Main menu");

    public override bool IsPinned => true;

    /// <summary>The menu's items: the language files' names, the action's name, and the English stand-ins.</summary>
    private static readonly (string Key, string Text, string Name, string Tooltip)[] Items =
    [
        ("new_game", "Campaign", "newgame", "Start the first level on the campaign"),
        ("custom_campaign", "Custom Campaign", "campaign", "Play a campaign created by the community"),
        ("custom_level", "Single Scenario", "scenario", "Build your hospital in a single scenario"),
        ("continue", "Continue Game", "continue", "Continue your latest saved game"),
        ("load_game", "Load Game", "load", "Load a saved game"),
        ("options", "Settings", "options", "Tweak various settings"),
        ("map_edit", "Map Editor", "mapeditor", "Create a custom map"),
        ("exit", "Exit", "exit", "No, no, please don't leave!"),
    ];

    /// <summary>A menu item's height (<c>menu_item_height</c>).</summary>
    private const int ItemHeight = 40;

    /// <summary>
    /// <c>UIMainMenu</c>'s size: 200 wide; 20 above the items, 50 for each, and a line of 15 for the version under them.
    /// </summary>
    public static readonly Point LookSize = new(200, 20 + ((ItemHeight + 10) * 8) + 15);

    private readonly ResizablePanels _panels = new();

    public override bool IsFullscreen(IWindowHost host) => ResizablePanels.IsAvailable(host);

    public override Point FullscreenSize => LookSize;

    public override Vector2? FullscreenPosition => new(0.5f, 0.25f);

    /// <summary><c>addMenuItem</c>: a button 160 by 40 for each item, 20 in, 50 apart from 20 down.</summary>
    private List<DialogPanel> Panels(IWindowHost host) =>
    [
        .. Items.Select((item, i) => DialogPanel.Button(
            new Rectangle(20, 20 + (i * (ItemHeight + 10)), 160, ItemHeight),
            CorsixText.Get(host, "main_menu." + item.Key, item.Text),
            _ => action(item.Name),
            CorsixText.Get(host, "tooltip.main_menu." + item.Key, item.Tooltip))),
    ];

    /// <summary><c>UIMainMenu:draw</c>: the items, then the version at the bottom right, 15 up.</summary>
    public override void DrawFullscreen(Microsoft.Xna.Framework.Graphics.SpriteBatch batch, IWindowHost host, Point origin, Microsoft.Xna.Framework.Input.MouseState mouse)
    {
        _panels.Draw(batch, host, origin, LookSize, Panels(host), mouse);
        var font = ResizablePanels.Font(host);
        var text = CorsixText.Get(host, "main_menu.version", "Version: ") + version;
        font.Draw(batch, text, new Vector2(origin.X + 5 + 190 - font.Measure(text).Width, origin.Y + LookSize.Y - 15), tint: new Color(255, 255, 255, 192));
    }

    public override void FullscreenMouse(IWindowHost host, Point local, Microsoft.Xna.Framework.Input.MouseState mouse, Microsoft.Xna.Framework.Input.MouseState previous) =>
        _panels.Mouse(host, Panels(host), local, mouse, previous);

    public override string? FullscreenTooltip(IWindowHost host, Point local) => ResizablePanels.Tooltip(Panels(host), local);

    public override Point? FindFullscreenButton(IWindowHost host, string text, int nth) => ResizablePanels.FindButton(Panels(host), text, nth);

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
    /// <summary>A window CorsixTH added to the game, drawn in its own look.</summary>
    public override bool UsesResizableLook => true;

    private CustomLevelEntry? _selected = levels.Count > 0 ? levels[0] : null;
    private bool _freeBuild = freeBuild;

    public override string Title => UiText.Get("custom_game_window.caption", "Custom Game");

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        if (levels.Count == 0)
        {
            yield return WindowCell.Line(UiText.Get("openwards.custom_game.none", "No levels found: put .level and .map files in the levels folder (levels in config.yaml)."), dim: true);
        }

        foreach (var level in levels)
        {
            yield return WindowRow.Of(WindowCell.Button(level.Info.Name, () => _selected = level, highlight: level == _selected) with { Tooltip = UiText.Get("tooltip.custom_game_window.choose_game", "Click a level to read more about it") });
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
            WindowCell.Button(UiText.Get("custom_game_window.free_build", "Free Build"), () => _freeBuild = !_freeBuild, highlight: _freeBuild) with { Tooltip = UiText.Get("tooltip.custom_game_window.free_build", "Tick this box if you want to play without money or winning and losing conditions") },
            WindowCell.Button(UiText.Get("custom_game_window.load_selected_level", "Start"), () =>
            {
                host.Close(this);
                start(_selected!, _freeBuild);
            }, enabled: _selected is not null) with
            { Tooltip = UiText.Get("tooltip.custom_game_window.load_selected_level", "Load and play the selected level") },
            WindowCell.Button(UiText.Get("new_game_window.cancel", "Cancel"), () => host.Close(this)));
    }
}

/// <summary><c>UICustomCampaign</c>: the player's own campaigns; a click on one shows its description, Start plays its first level.</summary>
internal sealed class CustomCampaignWindow(IReadOnlyList<CustomCampaignEntry> campaigns, Action<CustomCampaignEntry> start) : UiWindow
{
    /// <summary>A window CorsixTH added to the game, drawn in its own look.</summary>
    public override bool UsesResizableLook => true;

    private CustomCampaignEntry? _selected = campaigns.Count > 0 ? campaigns[0] : null;

    public override string Title => UiText.Get("custom_campaign_window.caption", "Custom Campaign");

    /// <summary>A campaign's name, or its file's when it gives none.</summary>
    public static string Name(CustomCampaignEntry entry) => entry.Campaign.Name ?? Path.GetFileNameWithoutExtension(entry.Path);

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        if (campaigns.Count == 0)
        {
            yield return WindowCell.Line(UiText.Get("openwards.custom_campaign.none", "No campaigns found: put .campaign.yaml files in the campaigns folder (campaigns in config.yaml)."), dim: true);
        }

        foreach (var campaign in campaigns)
        {
            yield return WindowRow.Of(WindowCell.Button(Name(campaign), () => _selected = campaign, highlight: campaign == _selected) with { Tooltip = UiText.Get("tooltip.custom_campaign_window.choose_campaign", "Choose a campaign to read more about it") });
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
            WindowCell.Button(UiText.Get("custom_campaign_window.start_selected_campaign", "Start campaign"), () =>
            {
                host.Close(this);
                start(_selected!);
            }, enabled: _selected is not null) with
            { Tooltip = UiText.Get("tooltip.custom_campaign_window.start_selected_campaign", "Load the first level of this campaign") },
            WindowCell.Button(UiText.Get("new_game_window.cancel", "Cancel"), () => host.Close(this)));
    }
}
