using OpenWards.Client.Rendering;
using OpenWards.Client.Ui.Windows;
using OpenWards.Content.Settings;

namespace OpenWards.Client.Tests;

/// <summary>The window sizes the graphics settings offer: the monitor's own modes.</summary>
public class DisplayModesTests
{
    [Fact]
    public void OnlyTheMonitorsModesThatFitTheDesktopAreOfferedOnceSmallestFirst()
    {
        var sizes = DisplayModes.WindowSizes([(2560, 1600), (1920, 1200), (1920, 1200), (3840, 2160), (640, 400), (1280, 800)], (2560, 1600));

        Assert.Equal([(1280, 800), (1920, 1200), (2560, 1600)], sizes);
    }

    [Fact]
    public void WithoutModesTheFixedListIsTrimmedToTheDesktop()
    {
        var sizes = DisplayModes.WindowSizes([], (1366, 768));

        Assert.Contains((1366, 768), sizes);
        Assert.DoesNotContain(sizes, size => size.Width > 1366 || size.Height > 768);
    }

    [Fact]
    public void TheWindowSizeRowShowsAndStepsThroughPixelsButKeepsPoints()
    {
        (int, int)[] modes = [(1280, 800), (1920, 1200), (2560, 1600)];
        var dialog = new SettingsDialog(SettingsDialogKind.Graphics, new GameConfig() with { Width = 1280, Height = 800 }, _ => { }, () => { }, windowSizes: () => modes, density: 1.5);
        var row = dialog.Settings()[1];
        Assert.Equal("1920x1200", row.Value);

        row.Change(1);
        Assert.Equal((1707, 1067), (dialog.Config.Width, dialog.Config.Height));
        Assert.Equal("2560x1600", dialog.Settings()[1].Value);

        dialog.Settings()[1].Change(1);
        Assert.Equal((853, 533), (dialog.Config.Width, dialog.Config.Height));
    }

    [Fact]
    public void ASizeNotOfferedGoesToTheNextLargerOne()
    {
        (int, int)[] modes = [(1280, 800), (1920, 1200), (2560, 1600)];
        var dialog = new SettingsDialog(SettingsDialogKind.Graphics, new GameConfig() with { Width = 1500, Height = 900 }, _ => { }, () => { }, windowSizes: () => modes);

        dialog.Settings()[1].Change(1);

        Assert.Equal((1920, 1200), (dialog.Config.Width, dialog.Config.Height));
    }
}
