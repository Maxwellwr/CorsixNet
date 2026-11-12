using CorsixNet.Client.Ui.Windows;
using CorsixNet.Content.Settings;

namespace CorsixNet.Client.Tests;

/// <summary>The hotkey assignment dialog (<c>dialogs/resizables/hotkey_assign.lua</c>).</summary>
public sealed class HotkeyAssignWindowTests
{
    [Fact]
    public void EveryActionIsOnAPane()
    {
        var listed = HotkeyAssignWindow.Panes.SelectMany(pane => pane.Sections).SelectMany(section => section.Keys).ToList();

        Assert.Equal(Hotkeys.Defaults.Select(pair => pair.Name).Order(), listed.Order());
        Assert.DoesNotContain(listed, action => HotkeyAssignWindow.Label(action) == action);
    }

    [Fact]
    public void AKeyClickedTakesTheNextKeyAndAcceptGivesTheNewKeys()
    {
        var host = new WindowTestHost();
        var current = new Hotkeys();
        Hotkeys? accepted = null;
        var back = 0;
        var window = new HotkeyAssignWindow(current, keys => accepted = keys, () => back++);
        host.Opened.Add(window);

        host.Click(window, "Game Speed Keys");
        Assert.Contains("Pause|P", host.Lines(window));
        host.Click(window, "P");
        Assert.True(window.IsCapturing);
        Assert.Contains("Pause|...", host.Lines(window));

        window.Capture(new Hotkey("escape"));
        Assert.False(window.IsCapturing);
        Assert.Contains("Pause|P", host.Lines(window));

        host.Click(window, "P");
        window.Capture(new Hotkey("1"));
        Assert.Contains("Pause|1", host.Lines(window));
        Assert.Contains("Slowest|P", host.Lines(window));
        Assert.Equal(new Hotkey("p"), current["ingame_pause"]);

        host.Click(window, "Accept");
        Assert.NotNull(accepted);
        Assert.Equal(new Hotkey("1"), accepted["ingame_pause"]);
        Assert.Equal(new Hotkey("p"), accepted["ingame_gamespeed_slowest"]);
        Assert.Empty(host.Opened);
        Assert.Equal(1, back);
    }

    [Fact]
    public void ResetGoesBackToTheDefaultsAndCancelKeepsTheOldKeys()
    {
        var host = new WindowTestHost();
        var current = Hotkeys.Parse("ingame_panel_bankManager = \"g\"");
        Hotkeys? accepted = null;
        var window = new HotkeyAssignWindow(current, keys => accepted = keys);
        host.Opened.Add(window);

        host.Click(window, "Panel Keys");
        Assert.Contains("Bank Manager|G", host.Lines(window));
        host.Click(window, "Reset to Defaults");
        Assert.Contains("Bank Manager|F1", host.Lines(window));
        host.Click(window, "Cancel");

        Assert.Null(accepted);
        Assert.Empty(host.Opened);
        Assert.Equal(new Hotkey("g"), current["ingame_panel_bankManager"]);
    }

    [Fact]
    public void TheOptionsOpenTheDialog()
    {
        var host = new WindowTestHost();
        var opened = 0;
        var options = new OptionsWindow(new GameConfig(), _ => { }, hotkeys: () => opened++);
        host.Opened.Add(options);

        host.Click(options, "Assign");

        Assert.Equal(1, opened);
        Assert.Empty(host.Opened);
    }
}
