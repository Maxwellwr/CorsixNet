using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim.Maps;
using Microsoft.Xna.Framework;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class TownMapScreenTests
{
    [Fact]
    public void TheMapHasThreePixelsATile()
    {
        var map = new LevelMap(128, 128, 1);
        Assert.Equal(new TilePosition(0, 0), TownMapScreen.TileAt(map, new Point(227, 25)));
        Assert.Equal(new TilePosition(10, 5), TownMapScreen.TileAt(map, new Point(227 + 31, 25 + 17)));
        Assert.Null(TownMapScreen.TileAt(map, new Point(226, 25)));
        Assert.Null(TownMapScreen.TileAt(map, new Point(227 + (128 * 3), 30)));
    }

    [Fact]
    public void TheHeatingBillFollowsTheRadiators()
    {
        Assert.Equal(75, TownMapScreen.HeatingBill(5, 2, freeBuild: false));
        Assert.Equal(22, TownMapScreen.HeatingBill(3, 1, freeBuild: false));
        Assert.Equal(0, TownMapScreen.HeatingBill(5, 2, freeBuild: true));
    }
}
