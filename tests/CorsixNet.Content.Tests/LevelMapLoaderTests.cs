using CorsixNet.Content.Levels;
using CorsixNet.Formats;
using CorsixNet.Formats.Tests;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Content.Tests;

public class LevelMapLoaderTests
{
    // Block codes and the VBLK-0 sprites they map to.
    private const byte BrickWallCode = 113;   // sprite 0x73 (115), a wall
    private const byte PondFoliageCode = 70;  // sprite 71
    private const byte DividerWallCode = 140;

    [Fact]
    public void TranslatesBlockCodesToSprites()
    {
        var loaded = Load(new MapFileBuilder().Tile(2, 2, floor: 16, northWall: BrickWallCode, westWall: BrickWallCode));

        var tile = loaded.Map[2, 2];
        Assert.Equal(17, tile.Floor);
        Assert.Equal(115, tile.NorthWall);
        Assert.Equal(115, tile.WestWall);
    }

    [Fact]
    public void WallsBlockTravelInBothDirections()
    {
        var map = Load(new MapFileBuilder().Tile(5, 5, northWall: BrickWallCode, westWall: BrickWallCode)).Map;

        Assert.False(map[5, 5].Has(TileFlags.CanTravelNorth));
        Assert.False(map[5, 4].Has(TileFlags.CanTravelSouth));
        Assert.False(map[5, 5].Has(TileFlags.CanTravelWest));
        Assert.False(map[4, 5].Has(TileFlags.CanTravelEast));
        Assert.True(map[5, 5].Has(TileFlags.CanTravelEast | TileFlags.CanTravelSouth));
    }

    [Fact]
    public void MapEdgesBlockTravel()
    {
        var map = Load(new MapFileBuilder()).Map;

        Assert.False(map[0, 10].Has(TileFlags.CanTravelWest));
        Assert.False(map[127, 10].Has(TileFlags.CanTravelEast));
        Assert.False(map[10, 0].Has(TileFlags.CanTravelNorth));
        Assert.False(map[10, 127].Has(TileFlags.CanTravelSouth));
        Assert.True(map[10, 10].Has(TileFlags.CanTravelAll));
    }

    [Fact]
    public void MovesPondFoliageToWallLayer()
    {
        var tile = Load(new MapFileBuilder().Tile(1, 1, floor: PondFoliageCode)).Map[1, 1];

        Assert.Equal(69, tile.Floor);
        Assert.Equal(71, tile.NorthWall);
        Assert.True(tile.Has(TileFlags.CanTravelNorth));
    }

    [Fact]
    public void IgnoresDividerWalls()
    {
        var tile = Load(new MapFileBuilder().Tile(1, 1, northWall: DividerWallCode, westWall: DividerWallCode + 1)).Map[1, 1];

        Assert.Equal(0, tile.NorthWall);
        Assert.Equal(0, tile.WestWall);
        Assert.True(tile.Has(TileFlags.CanTravelNorth | TileFlags.CanTravelWest));
    }

    [Fact]
    public void DecodesBuildFlags()
    {
        var map = Load(new MapFileBuilder()
            .Tile(1, 1)
            .Tile(2, 1, flags: 0b0000_0001)
            .Tile(3, 1, extraFlags: 0x10)
            .Tile(4, 1, flags: 0b0011_1110)
            .Tile(5, 1, flags: 0b0011_1100, objectType: 9)).Map;

        Assert.True(map[1, 1].Has(TileFlags.Passable | TileFlags.Hospital | TileFlags.Buildable | TileFlags.BuildableNorth));
        Assert.False(map[2, 1].Has(TileFlags.Passable));
        Assert.False(map[2, 1].Has(TileFlags.Hospital));
        Assert.True(map[3, 1].Has(TileFlags.Passable));
        Assert.False(map[3, 1].Has(TileFlags.Hospital));

        // Without an object, edges stay buildable even when the file says otherwise.
        Assert.False(map[4, 1].Has(TileFlags.Buildable));
        Assert.True(map[4, 1].Has(TileFlags.BuildableNorth | TileFlags.BuildableWest));
        Assert.False(map[5, 1].Has(TileFlags.BuildableNorth));
        Assert.False(map[5, 1].Has(TileFlags.BuildableWest));
    }

    [Fact]
    public void CollectsObjectsCamerasAndParcels()
    {
        var loaded = Load(new MapFileBuilder(playerCount: 2)
            .Tile(7, 8, objectType: 12, objectDirection: 3, parcel: 4)
            .Camera(0, 60, 61)
            .Camera(1, 30, 31)
            .Heliport(0, 5, 6));

        Assert.Equal([new MapObjectPlacement(new TilePosition(7, 8), 12, 3)], loaded.Objects);
        Assert.Equal(new TilePosition(30, 31), loaded.Map.GetCameraTile(1));
        Assert.Equal(new TilePosition(5, 6), loaded.Map.GetHeliportTile(0));
        Assert.Equal(5, loaded.Map.ParcelCount);
    }

    [Fact]
    public void ComputesShadows()
    {
        var map = Load(new MapFileBuilder().Tile(10, 10, westWall: BrickWallCode)).Map;

        Assert.True(map[10, 10].Has(TileFlags.ShadowHalf));
        Assert.True(map[10, 9].Has(TileFlags.ShadowFull));
        Assert.True(map[9, 9].Has(TileFlags.ShadowFull));
    }

    [Fact]
    public void LoadsEveryOriginalLevel()
    {
        var install = RealGameData.Require();
        var levels = Directory.EnumerateFiles(install.FindDirectory("LEVELS")!, "LEVEL.L*").ToList();

        Assert.NotEmpty(levels);
        foreach (var path in levels)
        {
            var loaded = LevelMapLoader.LoadOriginal(install, Path.GetFileName(path));
            var map = loaded.Map;
            var camera = map.GetCameraTile(0);

            Assert.True(map.Contains(camera.X, camera.Y), $"{path}: camera outside map");
            Assert.True(map[camera.X, camera.Y].Has(TileFlags.Passable), $"{path}: camera on impassable tile");
            Assert.True(map.ParcelCount > 1, $"{path}: no parcels");
            Assert.NotEmpty(loaded.Objects);
        }
    }

    private static LoadedLevelMap Load(MapFileBuilder builder) => LevelMapLoader.Load(MapFile.Parse(builder.Build()));
}
