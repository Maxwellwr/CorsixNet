using CorsixNet.Client.Maps;
using CorsixNet.Client.Ui.Windows;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class RoomEditScreenTests
{
    [Fact]
    public void ThePanelGrowsByALineForEachObjectToPlace()
    {
        Assert.Equal(167, RoomEditScreen.Height(0));
        Assert.Equal(167 + 58, RoomEditScreen.Height(2));
    }

    [Fact]
    public void WithoutTheGraphicsTheRoomPanelIsATable()
    {
        var tool = new BuildRoomTool();
        Assert.False(new RoomEditWindow(tool, _ => { }).IsFullscreen(new WindowTestHost()));
        Assert.False(new RoomBuyWindow(tool).IsFullscreen(new WindowTestHost()));
    }
}
