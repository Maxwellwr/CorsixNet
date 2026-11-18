using Microsoft.Xna.Framework;
using OpenWards.Client.Ui.Windows;
using OpenWards.Sim.Maps;
using Xunit;

namespace OpenWards.Client.Tests;

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

    [Fact]
    public void TheTownIsTheLevelsNameOrItsFilesName()
    {
        Assert.Null(OpenWards.Client.Maps.MapViewScreen.LevelTitle("LEVEL.L3", null));
        Assert.Equal("MY TOWN", OpenWards.Client.Maps.MapViewScreen.LevelTitle("my town.level", null));
    }
    [Fact]
    public void TheWallsOutsideTheHospitalAreDrawnToo()
    {
        // A building's south wall stands on the street tile below it, outside any parcel.
        var map = new LevelMap(8, 8, 1) { ParcelCount = 3 };
        map[2, 2].Parcel = 1;
        map[2, 3].NorthWall = 114;
        map[3, 3].WestWall = 115;
        map[4, 4].NorthWall = 181;
        map[5, 5].Parcel = 2;
        map[5, 5].NorthWall = 116;
        map.KeepOriginalTiles();
        map.SetParcelOwner(1, 1);
        map.SetParcelOwner(2, 0);

        Assert.Equal((true, false), TownMapScreen.WallsDrawn(map, 2, 3));
        Assert.Equal((false, true), TownMapScreen.WallsDrawn(map, 3, 3));

        // Hedges and fences on the wall layer are not walls.
        Assert.Equal((false, false), TownMapScreen.WallsDrawn(map, 4, 4));

        // A parcel for sale shows as grass on the main map, but its building's walls as in the level file.
        Assert.NotEqual(116, map[5, 5].NorthWall);
        Assert.Equal((true, false), TownMapScreen.WallsDrawn(map, 5, 5));
    }
}
