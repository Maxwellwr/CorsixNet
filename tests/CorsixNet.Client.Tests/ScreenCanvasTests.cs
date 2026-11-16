using CorsixNet.Client.Rendering;
using CorsixNet.Client.Ui.Windows;
using Microsoft.Xna.Framework;

namespace CorsixNet.Client.Tests;

public class ScreenCanvasTests
{
    [Theory]
    [InlineData(1.0, 1)]
    [InlineData(1.25, 1)]
    [InlineData(1.5, 2)]
    [InlineData(1.75, 2)]
    [InlineData(2.0, 2)]
    [InlineData(2.5, 3)]
    [InlineData(6.0, 4)]
    [InlineData(0.0, 1)]
    public void TheAutomaticScaleIsTheDisplayScaleRoundedHalvesUp(double displayScale, int expected) =>
        Assert.Equal(expected, ScreenCanvas.ScaleFor(displayScale));

    [Theory]
    [InlineData(0, 2, 1920, 1200, 2)]
    [InlineData(1, 2, 1920, 1200, 1)]
    [InlineData(3, 1, 1920, 1200, 2)]
    [InlineData(4, 1, 3840, 2160, 4)]
    [InlineData(2, 1, 1280, 800, 1)]
    [InlineData(0, 2, 1280, 960, 2)]
    [InlineData(0, 2, 1279, 960, 1)]
    public void TheScaleNeverMakesThePictureSmallerThanTheGamesSmallest(int requested, int automatic, int width, int height, int expected) =>
        Assert.Equal(expected, ScreenCanvas.ScaleFor(requested, automatic, new Point(width, height)));

    [Fact]
    public void TheSettingsShowTheAutomaticScaleWithWhatItComesTo()
    {
        Assert.Equal("Auto (2x)", SettingsDialog.UiScaleName(0, 2));
        Assert.Equal("3x", SettingsDialog.UiScaleName(3, 2));
    }
}
