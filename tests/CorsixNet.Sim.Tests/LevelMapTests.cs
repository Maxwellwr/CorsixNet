using CorsixNet.Sim.Maps;

namespace CorsixNet.Sim.Tests;

public class LevelMapTests
{
    private const ushort Wall = 100;

    [Fact]
    public void RejectsTilesOutsideTheMap()
    {
        var map = new LevelMap(4, 3, playerCount: 1);

        Assert.True(map.Contains(3, 2));
        Assert.False(map.Contains(4, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => map[0, 3].Floor);
    }

    [Fact]
    public void TilesAreMutableByReference()
    {
        var map = new LevelMap(2, 2, playerCount: 1);

        map[1, 0].Floor = 5;
        map[1, 0].Set(TileFlags.Passable, true);

        Assert.Equal(5, map[1, 0].Floor);
        Assert.True(map[1, 0].Has(TileFlags.Passable));
    }

    [Fact]
    public void WestWallShadesTileAndWrapsAroundCorner()
    {
        var map = new LevelMap(4, 4, playerCount: 1);
        map[2, 2].WestWall = Wall;

        map.UpdateShadows();

        Assert.True(map[2, 2].Has(TileFlags.ShadowHalf));
        Assert.True(map[2, 1].Has(TileFlags.ShadowFull));
        Assert.True(map[1, 1].Has(TileFlags.ShadowFull));
        Assert.False(map[2, 2].Has(TileFlags.ShadowWall));
    }

    [Fact]
    public void CornerShadesNorthWallInsteadOfFloor()
    {
        var map = new LevelMap(4, 4, playerCount: 1);
        map[2, 2].WestWall = Wall;
        map[2, 2].NorthWall = Wall;

        map.UpdateShadows();

        Assert.True(map[2, 2].Has(TileFlags.ShadowHalf | TileFlags.ShadowWall));
        Assert.False(map[2, 1].Has(TileFlags.ShadowFull));
    }

    [Fact]
    public void ShadowStopsAtNeighbouringWestWall()
    {
        var map = new LevelMap(4, 4, playerCount: 1);
        map[2, 2].WestWall = Wall;
        map[2, 1].WestWall = Wall;

        map.UpdateShadows();

        Assert.True(map[2, 1].Has(TileFlags.ShadowFull));
        Assert.False(map[1, 1].Has(TileFlags.ShadowFull));
    }

    [Fact]
    public void NonWallSpritesCastNoShadow()
    {
        var map = new LevelMap(4, 4, playerCount: 1);
        map[2, 2].WestWall = 71; // pond foliage

        map.UpdateShadows();

        Assert.False(map[2, 2].Has(TileFlags.ShadowHalf));
    }

    [Fact]
    public void UpdateShadowsClearsStaleShadows()
    {
        var map = new LevelMap(4, 4, playerCount: 1);
        map[2, 2].WestWall = Wall;
        map.UpdateShadows();

        map[2, 2].WestWall = 0;
        map.UpdateShadows();

        Assert.False(map[2, 2].Has(TileFlags.ShadowHalf));
        Assert.False(map[2, 1].Has(TileFlags.ShadowFull));
    }
}
