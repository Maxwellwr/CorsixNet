// The hotkey assignment dialog, after CorsixTH CorsixTH/Lua/dialogs/resizables/hotkey_assign.lua (UIHotkeyAssign and
// UIHotkeyAssignKeyPane) and its English texts in languages/english.lua (hotkey_window, tooltip.hotkey_window).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Content.Settings;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>A window that takes the next key pressed, such as the hotkey assignment dialog while it waits for a key.</summary>
internal interface IKeyCaptureWindow
{
    /// <summary>Whether the window waits for a key; the game sees no keys then.</summary>
    bool IsCapturing { get; }

    /// <summary>The key pressed, with the modifiers held.</summary>
    void Capture(Hotkey key);
}

/// <summary>
/// CorsixTH's <c>UIHotkeyAssign</c>, from the options: a button for each pane of keys, the pane's actions with their
/// keys, and Accept, Reset to Defaults and Cancel. Clicking a key waits for the next key pressed (the hotkey box);
/// Escape or Return on their own leave the key as it was. The changes are kept apart until Accept, which saves them.
/// </summary>
/// <remarks>
/// Only the actions this game reads from <c>hotkeys.txt</c> are listed. The cheat window's key, in CorsixTH's debug
/// pane, is with the general keys here.
/// </remarks>
internal sealed class HotkeyAssignWindow(Hotkeys current, Action<Hotkeys> accept, Action? back = null) : UiWindow, IKeyCaptureWindow
{
    /// <summary>The panes (<c>key_options</c>): an id, the button's title and tooltip, and the sections' titles and actions.</summary>
    internal static readonly IReadOnlyList<(string Id, string Title, string Tooltip, IReadOnlyList<(string Title, IReadOnlyList<string> Keys)> Sections)> Panes = BuildPanes();

    private Hotkeys _draft = current.Copy();
    private string _pane = Panes[0].Id;
    private string? _listening;

    public override string Title => "Hotkey Assignment";

    /// <summary>The action waiting for a key, or null.</summary>
    internal string? Listening => _listening;

    public bool IsCapturing => _listening is not null;

    public void Capture(Hotkey key)
    {
        if (_listening is { } action)
        {
            _draft.Assign(action, key);
            _listening = null;
        }
    }

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var buttons = Panes.Select(pane => WindowCell.Button(pane.Title, () => ShowPane(pane.Id), highlight: pane.Id == _pane) with { Tooltip = pane.Tooltip }).ToList();
        for (var i = 0; i < buttons.Count; i += 3)
        {
            yield return new WindowRow(buttons.Skip(i).Take(3).ToList());
        }

        foreach (var (title, keys) in Panes.First(pane => pane.Id == _pane).Sections)
        {
            yield return WindowRow.Gap;
            yield return WindowCell.Line(title, highlight: true);
            foreach (var action in keys)
            {
                var listening = _listening == action;
                yield return WindowRow.Of(
                    Label(action),
                    WindowCell.Button(listening ? "..." : _draft[action]?.ToString().ToUpperInvariant() ?? string.Empty, () => _listening = listening ? null : action, highlight: listening));
            }
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(
            WindowCell.Button("Accept", () => Accept(host)) with { Tooltip = "Accept and save hotkey assignments" },
            WindowCell.Button("Reset to Defaults", ResetToDefaults) with { Tooltip = "Reset all hotkeys to the program's defaults" },
            WindowCell.Button("Cancel", () => Leave(host)) with { Tooltip = "Cancel the assignment and go back to the options menu" });
    }

    /// <summary><c>buttonAccept</c>: the new keys are handed over to be saved.</summary>
    private void Accept(IWindowHost host)
    {
        accept(_draft);
        Leave(host);
    }

    /// <summary><c>buttonDefaults</c>: every key back to its default, still to be accepted.</summary>
    private void ResetToDefaults()
    {
        _draft = new Hotkeys();
        _listening = null;
    }

    /// <summary><c>showKeyPane</c>: shows a pane; a key waited for is given up.</summary>
    private void ShowPane(string id)
    {
        _pane = id;
        _listening = null;
    }

    /// <summary>Accept and Cancel close the dialog and go back to the options.</summary>
    private void Leave(IWindowHost host)
    {
        host.Close(this);
        back?.Invoke();
    }

    /// <summary>The action's name as <c>hotkey_window</c> gives it.</summary>
    internal static string Label(string action) => action switch
    {
        "ingame_scroll_up" => "Scroll Up",
        "ingame_scroll_down" => "Scroll Down",
        "ingame_scroll_left" => "Scroll Left",
        "ingame_scroll_right" => "Scroll Right",
        "ingame_zoom_in" => "Zoom In",
        "ingame_zoom_in_more" => "Zoom In More",
        "ingame_zoom_out" => "Zoom Out",
        "ingame_zoom_out_more" => "Zoom Out More",
        "ingame_reset_zoom" => "Reset Zoom",
        "ingame_showCheatWindow" => "Cheat Menu",
        "ingame_openFirstMessage" => "Level Message",
        "ingame_quickSave" => "Quick Save",
        "ingame_quickLoad" => "Quick Load",
        "ingame_pause" => "Pause",
        "ingame_gamespeed_slowest" => "Slowest",
        "ingame_gamespeed_slower" => "Slower",
        "ingame_gamespeed_normal" => "Normal",
        "ingame_gamespeed_max" => "Max",
        "ingame_gamespeed_thensome" => "Then Some",
        "ingame_gamespeed_speedup" => "Speed Up",
        "ingame_panel_bankManager" => "Bank Manager",
        "ingame_panel_bankStats" => "Bank Status",
        "ingame_panel_staffManage" => "Manage Staff",
        "ingame_panel_townMap" => "Town Map",
        "ingame_panel_casebook" => "Casebook",
        "ingame_panel_research" => "Research",
        "ingame_panel_status" => "Status",
        "ingame_panel_charts" => "Charts",
        "ingame_panel_policy" => "Policy",
        "ingame_panel_machineMenu" => "Machines Menu",
        "ingame_panel_adviserHistory" => "Adviser History",
        "ingame_panel_buildRoom" => "Build Room",
        "ingame_panel_hireStaff" => "Hire Staff",
        "ingame_toggleAdvisor" => "Advisor",
        _ when action.StartsWith("ingame_storePosition_", StringComparison.Ordinal) || action.StartsWith("ingame_recallPosition_", StringComparison.Ordinal) =>
            action[^1] == '0' ? "10" : action[^1].ToString(),
        _ => action,
    };

    /// <summary>CorsixTH's panes in its order, with only the actions this game has; any other action goes with the general keys.</summary>
    private static List<(string, string, string, IReadOnlyList<(string, IReadOnlyList<string>)>)> BuildPanes()
    {
        var known = Hotkeys.Defaults.Select(pair => pair.Name).ToHashSet(StringComparer.Ordinal);
        var digits = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
        var panes = new List<(string Id, string Title, string Tooltip, (string Title, string[] Keys)[] Sections)>
        {
            ("ingame", "General In-Game Keys", "Assign general in-game hotkeys", [("General In-Game Keys", ["ingame_openFirstMessage", "ingame_quickSave", "ingame_quickLoad", "ingame_showCheatWindow"])]),
            ("speed", "Game Speed Keys", "Assign hotkeys to control the game speed", [("Game Speed Keys", ["ingame_pause", "ingame_gamespeed_slowest", "ingame_gamespeed_slower", "ingame_gamespeed_normal", "ingame_gamespeed_max", "ingame_gamespeed_thensome", "ingame_gamespeed_speedup"])]),
            ("scroll", "Scroll Keys", "Assign hotkeys to scroll the screen", [("Scroll Keys", ["ingame_scroll_up", "ingame_scroll_down", "ingame_scroll_left", "ingame_scroll_right"])]),
            ("zoom", "Zoom Keys", "Assign hotkeys to zoom in and out", [("Zoom Keys", ["ingame_zoom_in", "ingame_zoom_in_more", "ingame_zoom_out", "ingame_zoom_out_more", "ingame_reset_zoom"])]),
            ("toggle", "Toggle Keys", "Assign hotkeys to toggle options", [("Toggle Keys", ["ingame_toggleAdvisor"])]),
            ("panels", "Panel Keys", "Assign hotkeys to open panels", [("Panel Keys", ["ingame_panel_bankManager", "ingame_panel_bankStats", "ingame_panel_staffManage", "ingame_panel_townMap", "ingame_panel_casebook", "ingame_panel_research", "ingame_panel_status", "ingame_panel_charts", "ingame_panel_policy", "ingame_panel_machineMenu", "ingame_panel_adviserHistory", "ingame_panel_buildRoom", "ingame_panel_hireStaff"])]),
            ("recall", "Recall Position Keys", "Assign hotkeys to save and recall camera positions", [
                ("Store Position Keys", digits.Select(digit => "ingame_storePosition_" + digit).ToArray()),
                ("Recall Position Keys", digits.Select(digit => "ingame_recallPosition_" + digit).ToArray())]),
        };

        var listed = panes.SelectMany(pane => pane.Sections).SelectMany(section => section.Keys).ToHashSet(StringComparer.Ordinal);
        var rest = Hotkeys.Defaults.Select(pair => pair.Name).Where(name => !listed.Contains(name)).ToArray();
        if (rest.Length > 0)
        {
            var general = panes[0];
            panes[0] = general with { Sections = [(general.Sections[0].Title, [.. general.Sections[0].Keys, .. rest])] };
        }

        return panes
            .Select(pane => (pane.Id, pane.Title, pane.Tooltip, (IReadOnlyList<(string, IReadOnlyList<string>)>)pane.Sections
                .Select(section => (section.Title, (IReadOnlyList<string>)section.Keys.Where(known.Contains).ToList()))
                .Where(section => section.Item2.Count > 0)
                .ToList()))
            .Where(pane => pane.Item4.Count > 0)
            .ToList();
    }
}
