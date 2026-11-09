using CorsixNet.Content.Levels;
using CorsixNet.Formats;
using CorsixNet.Formats.Tests;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.Rooms;

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

            // Every player starts with a hospital entrance: one linked pair of door halves.
            var simulation = loaded.StartSimulation(seed: 1);
            var doors = simulation.Entities.OfType<EntranceDoor>().ToList();
            Assert.True(doors.Count >= 2 * map.PlayerCount, $"{path}: {doors.Count} door halves for {map.PlayerCount} players");
            Assert.All(doors, door => Assert.NotEqual(0, door.PartnerId));
        }
    }

    [Fact]
    public void GivesEachPlayerTheParcelWithTheirNumber()
    {
        var map = Load(new MapFileBuilder(playerCount: 2)
            .Tile(1, 1, parcel: 1)
            .Tile(2, 1, parcel: 2)
            .Tile(3, 1, parcel: 3)).Map;

        Assert.Equal(0, map.GetParcelOwner(0));
        Assert.Equal(1, map.GetParcelOwner(1));
        Assert.Equal(2, map.GetParcelOwner(2));
        Assert.Equal(0, map.GetParcelOwner(3));
    }

    [Fact]
    public void StartSimulationPlacesDoorsOnOwnedParcelsOnly()
    {
        var loaded = Load(new MapFileBuilder()
            .Tile(10, 10, objectType: EntranceDoor.LeftObjectType, parcel: 1)
            .Tile(11, 10, objectType: EntranceDoor.RightObjectType, parcel: 1)
            .Tile(30, 30, objectType: EntranceDoor.LeftObjectType, parcel: 2)
            .Tile(31, 30, objectType: EntranceDoor.RightObjectType, parcel: 2));

        var simulation = loaded.StartSimulation(seed: 1);

        Assert.Equal(2, simulation.Entities.OfType<EntranceDoor>().Count());
        Assert.Equal(2, simulation.DelayedMapObjects.Count);
    }

    [Fact]
    public void FreeBuildModeIsForLevelsOutsideTheCampaignOnly()
    {
        var loaded = Load(new MapFileBuilder().Tile(1, 1, parcel: 1));
        Assert.True(loaded.WithFreeBuild().StartSimulation(seed: 1).Rules.FreeBuild);

        var custom = loaded with { Rules = new LevelRules(40_000, [], []) };
        Assert.Equal(0, custom.WithFreeBuild().StartSimulation(seed: 1).Hospitals[0].Balance);

        var campaign = loaded with { Rules = new LevelRules(40_000, [], [], levelNumber: 3) };
        Assert.Same(campaign, campaign.WithFreeBuild());
    }

    [Fact]
    public void ParcelWithoutOwnerShowsAsGrassUntilBought()
    {
        var builder = new MapFileBuilder();
        for (var y = 0; y < 20; y++)
        {
            for (var x = 0; x < 20; x++)
            {
                builder.Tile(x, y, floor: 16, parcel: (ushort)(x < 10 ? 1 : 2));
            }
        }

        var loaded = Load(builder.Tile(12, 5, floor: 16, northWall: BrickWallCode, westWall: BrickWallCode, parcel: 2));
        var map = loaded.Map;

        // Parcel 2 is for sale: mown grass, a bush where x and y are both multiples of 8, no walls and no flags.
        Assert.Equal(1, map[12, 5].Floor);
        Assert.Equal(3, map[13, 5].Floor);
        Assert.Equal(0, map[12, 5].NorthWall);
        Assert.Equal(0, map[12, 5].WestWall);
        Assert.False(map[12, 5].Has(TileFlags.Passable));
        Assert.False(map[12, 5].Has(TileFlags.Hospital));
        Assert.Equal(192 + ((16 ^ 8) % 9), map[16, 8].NorthWall);
        Assert.Equal(17, map[5, 5].Floor);

        // A saved game keeps the original tiles, to give them back when the parcel is bought.
        using var stream = new MemoryStream();
        loaded.StartSimulation(seed: 1).Save(stream);
        stream.Position = 0;
        var simulation = Simulation.Load(stream);
        map = simulation.Map;
        Assert.Equal(1, map[12, 5].Floor);
        Assert.True(simulation.IsParcelPurchasable(2, 0));
        simulation.SetParcelOwner(2, 1);

        Assert.Equal(17, map[12, 5].Floor);
        Assert.Equal(115, map[12, 5].NorthWall);
        Assert.Equal(115, map[12, 5].WestWall);
        Assert.True(map[12, 5].Has(TileFlags.Passable | TileFlags.Hospital | TileFlags.Buildable));
        Assert.False(map[12, 5].Has(TileFlags.CanTravelNorth));
        Assert.Equal(0, map[16, 8].NorthWall);
    }

    [Fact]
    public void DividerWallsStandBetweenIndoorParcelsOfDifferentOwners()
    {
        var builder = new MapFileBuilder();
        for (var y = 0; y < 20; y++)
        {
            for (var x = 0; x < 20; x++)
            {
                builder.Tile(x, y, floor: 16, westWall: x == 10 ? DividerWallCode : (byte)0, parcel: (ushort)(x < 10 ? 1 : 2));
            }
        }

        var simulation = Load(builder).StartSimulation(seed: 1);
        var map = simulation.Map;

        // Seen from the owned side the divider is sprite 145, from the side for sale 143.
        Assert.Equal(143, map[10, 3].WestWall);
        Assert.False(map[9, 3].Has(TileFlags.CanTravelEast));

        simulation.SetParcelOwner(2, 1);

        Assert.Equal(0, map[10, 3].WestWall);
        Assert.True(map[9, 3].Has(TileFlags.CanTravelEast));
    }

    [Fact]
    public void RoomAgainstADividerWallKeepsItsWallWhenTheParcelIsBought()
    {
        var builder = new MapFileBuilder();
        for (var y = 0; y < 20; y++)
        {
            for (var x = 0; x < 20; x++)
            {
                builder.Tile(x, y, floor: 16, westWall: x == 10 ? DividerWallCode : (byte)0, parcel: (ushort)(x < 10 ? 1 : 2));
            }
        }

        var simulation = Load(builder).StartSimulation(seed: 1);
        var plan = new RoomPlan("gp", new TileRect(6, 2, 4, 4), new DoorPlacement(new(7, 6), EdgeOrientation.North));
        Assert.Equal(RoomPlanProblem.None, RoomBuilder.Check(simulation.Map, simulation.Content, plan, player: 0));
        simulation.Schedule(0, new BuildRoomCommand(plan));
        simulation.Step();
        var room = Assert.Single(simulation.Entities.OfType<Room>());

        simulation.SetParcelOwner(2, 1);

        var walls = simulation.Content.FindRoom("gp")!.Walls;
        var map = simulation.Map;
        for (var y = 2; y <= 5; y++)
        {
            Assert.Equal(room.Id, map[9, y].RoomId);
            Assert.Equal(walls.OutsideWest, map[10, y].WestWall);
            Assert.False(map[9, y].Has(TileFlags.CanTravelEast));
        }

        Assert.Equal(0, map[10, 6].WestWall);
    }

    private static LoadedLevelMap Load(MapFileBuilder builder) => LevelMapLoader.Load(MapFile.Parse(builder.Build()));
}
