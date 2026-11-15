using CorsixNet.Client.Ui;
using CorsixNet.Client.Ui.Windows;
using CorsixNet.Content.Settings;
using Microsoft.Xna.Framework;

namespace CorsixNet.Client.Tests;

/// <summary>CorsixTH's own dialogs laid out as resizable.lua's buildDialog lays them out.</summary>
public class ResizableDialogTests
{
    [Fact]
    public void ASettingsDialogHasItsTitleSettingsAndBackButtonWhereCorsixThPutsThem()
    {
        var (size, panels) = SettingsDialogLayout.Build("Title", [new DialogSetting("A", "On", _ => { }), new DialogSetting("B", "Off", _ => { })], [new DialogNavButton("Back", () => { })]);

        // 15 + 135 + 20 + 135 + 15 wide; two rows of 30 and the title, the back button and margins (105).
        Assert.Equal(new Point(320, 165), size);
        Assert.Equal(new Rectangle(75, 15, 170, 20), panels[0].Area);
        Assert.Equal(new Rectangle(15, 45, 135, 20), panels[1].Area);
        Assert.Equal(new Rectangle(155, 45, 135, 20), panels[2].Area);
        Assert.Equal(new Rectangle(15, 75, 135, 20), panels[3].Area);
        Assert.Equal(new Rectangle(15, 110, 290, 40), panels[^1].Area);
    }

    [Fact]
    public void TheOptionsBigButtonsGoTwoARowAndAnOddLastOneAcross()
    {
        var (_, panels) = SettingsDialogLayout.Build("Options", [new DialogSetting("A", "x", _ => { })], [.. "abc".Select(c => new DialogNavButton(c.ToString(), () => { }))], extraHeight: 250);

        Assert.Equal(new Rectangle(15, 80, 135, 40), panels[^3].Area);
        Assert.Equal(new Rectangle(160, 80, 135, 40), panels[^2].Area);
        Assert.Equal(new Rectangle(15, 130, 280, 40), panels[^1].Area);
    }

    [Fact]
    public void ASettingAppliesAtOnceAndSteppingGoesRoundBothWays()
    {
        GameConfig? applied = null;
        var dialog = new SettingsDialog(SettingsDialogKind.Accessibility, new GameConfig(), config => applied = config, () => { });
        var scroll = dialog.Settings().Single(setting => setting.Label == "Scroll Speed");

        scroll.Change(-1);
        Assert.Equal(1m, applied!.ScrollSpeed);
        dialog.Settings().Single(setting => setting.Label == "Scroll Speed").Change(-1);
        Assert.Equal(10m, dialog.Config.ScrollSpeed);

        var shake = dialog.Settings().Single(setting => setting.Label == "Screen Shake");
        Assert.True(shake.On);
        shake.Change(1);
        Assert.False(dialog.Config.EnableScreenShake);
    }

    [Fact]
    public void AVolumeGoesFromOffThroughTheTenths()
    {
        var dialog = new SettingsDialog(SettingsDialogKind.Sound, new GameConfig() with { SoundVolume = 1m }, _ => { }, () => { });
        dialog.Settings()[0].Change(1);
        Assert.False(dialog.Config.PlaySounds);
        dialog.Settings()[0].Change(1);
        Assert.Equal((true, 0.1m), (dialog.Config.PlaySounds, dialog.Config.SoundVolume));
    }

    [Fact]
    public void ALabelTooWideIsClippedWithDots()
    {
        Assert.Equal("Short", ResizableLook.ClipLine("Short", 100, text => text.Length * 10));
        Assert.Equal("ABCD...", ResizableLook.ClipLine("ABCDEFGHIJKL", 80, text => text.Length * 10));
    }

    [Fact]
    public void TheSoundDialogChoosesTheMidiApiAndPort()
    {
        var dialog = new SettingsDialog(SettingsDialogKind.Sound, new GameConfig(), _ => { }, () => { }, midiPorts: () => ["Synth A", "Synth B"]);
        var port = dialog.Settings().Single(setting => setting.Label == "MIDI Port");
        Assert.Equal("Default", port.Value);
        port.Change(1);
        Assert.Equal("Synth A", dialog.Config.MidiPort);
        dialog.Settings().Single(setting => setting.Label == "MIDI Port").Change(-1);
        Assert.Equal(string.Empty, dialog.Config.MidiPort);
        dialog.Settings().Single(setting => setting.Label == "MIDI API").Change(1);
        Assert.Equal("Windows MM", dialog.Config.MidiApi);

        // Without MIDI outputs there is nothing to choose.
        Assert.DoesNotContain(new SettingsDialog(SettingsDialogKind.Sound, new GameConfig(), _ => { }, () => { }).Settings(), setting => setting.Label == "MIDI API");
    }
}
