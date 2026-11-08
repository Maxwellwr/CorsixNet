using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.Rooms;
using static CorsixNet.Sim.Definitions.FootprintFlags;

namespace CorsixNet.Sim.Tests;

public class ObjectPlacerTests
{
    private static readonly WallSet Walls = new(82, 83, 90, 91);

    /// <summary>One occupied tile, used from the tile in front (north: the tile south of it).</summary>
    private static readonly ObjectDefinition Cabinet = new("cabinet", "Cabinet", 2, new Dictionary<Direction, ObjectOrientation>
    {
        [Direction.North] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition)]),
        [Direction.South] = new([new(0, 0, CompleteCell), new(0, -1, UsePosition)]),
    });

    /// <summary>A single occupied tile with no use position.</summary>
    private static readonly ObjectDefinition Pillar = new("pillar", "Pillar", 99, new Dictionary<Direction, ObjectOrientation>
    {
        [Direction.North] = new([new(0, 0, CompleteCell)]),
    });

    private static readonly ObjectDefinition Bench = new("bench", "Bench", 4, new Dictionary<Direction, ObjectOrientation>
    {
        [Direction.North] = new([new(0, 0, CompleteCell)]),
    }, CorridorObject: true);

    private static readonly RoomDefinition Office = new("office", "Office", 3, 18, Walls, new Dictionary<string, int> { ["cabinet"] = 1, ["pillar"] = 2 });
    private static readonly GameContent Content = new([Office], [], [Cabinet, Pillar, Bench]);

    // A 3x3 room at (2, 2)..(4, 4); its door is on the south side, so (3, 4) is just inside it.
    private static readonly RoomPlan Plan = new("office", new TileRect(2, 2, 3, 3), new DoorPlacement(new(3, 5), EdgeOrientation.North));

    [Fact]
    public void PlacesAnObjectAndMarksItsFootprint()
    {
        var simulation = NewSimulationWithRoom();

        Place(simulation, "cabinet", new(2, 2), Direction.North);

        var cabinet = Assert.Single(simulation.Entities.OfType<RoomObject>());
        var room = Assert.Single(simulation.Entities.OfType<Room>());
        Assert.Equal(("cabinet", room.Id), (cabinet.ObjectType, cabinet.RoomId));

        var occupied = simulation.Map[2, 2];
        Assert.False(occupied.Has(TileFlags.Passable));
        Assert.False(occupied.Has(TileFlags.Buildable));
        Assert.Equal(cabinet.Id, occupied.ObjectId);

        var use = simulation.Map[2, 3];
        Assert.True(use.Has(TileFlags.Passable));
        Assert.False(use.Has(TileFlags.Buildable));
        Assert.False(use.Has(TileFlags.BuildableNorth));
        Assert.Equal(0, use.ObjectId);
        Assert.Equal([new TilePosition(2, 3)], ObjectPlacer.UsePositions(simulation, cabinet));
    }

    [Fact]
    public void KeepsTheDoorwayClear()
    {
        var simulation = NewSimulationWithRoom();

        Assert.False(simulation.Map[3, 5].Has(TileFlags.Buildable));
        Assert.False(simulation.Map[3, 4].Has(TileFlags.Buildable));
        Assert.Equal(ObjectPlacementProblem.Blocked, Check(simulation, "pillar", new(3, 4), Direction.North));
    }

    [Fact]
    public void RejectsObjectsOutsideTheirPlace()
    {
        var simulation = NewSimulationWithRoom();

        Assert.Equal(ObjectPlacementProblem.UnknownObject, Check(simulation, "sofa", new(3, 3), Direction.North));
        Assert.Equal(ObjectPlacementProblem.NoSuchOrientation, Check(simulation, "pillar", new(3, 3), Direction.East));
        Assert.Equal(ObjectPlacementProblem.NotAllowedHere, Check(simulation, "cabinet", new(6, 6), Direction.North));
        Assert.Equal(ObjectPlacementProblem.NotAllowedHere, Check(simulation, "bench", new(3, 3), Direction.North));
        Assert.Equal(ObjectPlacementProblem.None, Check(simulation, "bench", new(6, 6), Direction.North));

        // The footprint would reach out of the room through its wall.
        Assert.Equal(ObjectPlacementProblem.NotAllowedHere, Check(simulation, "cabinet", new(2, 4), Direction.North));
    }

    [Fact]
    public void RejectsOverlaps()
    {
        var simulation = NewSimulationWithRoom();
        Place(simulation, "cabinet", new(2, 2), Direction.North);

        Assert.Equal(ObjectPlacementProblem.Blocked, Check(simulation, "pillar", new(2, 2), Direction.North));

        // A pillar on the cabinet's use position would block it.
        Assert.Equal(ObjectPlacementProblem.Blocked, Check(simulation, "pillar", new(2, 3), Direction.North));

        // A second cabinet whose use position overlaps the first one's is not allowed either.
        Assert.Equal(ObjectPlacementProblem.Blocked, Check(simulation, "cabinet", new(2, 4), Direction.South));
    }

    [Fact]
    public void RejectsPlacementsThatCutOffUsePositions()
    {
        var simulation = NewSimulationWithRoom();
        Place(simulation, "pillar", new(2, 4), Direction.North);
        Place(simulation, "pillar", new(3, 3), Direction.North);

        // The cabinet's use position (2, 3) would be walled in by the pillars and the cabinet itself.
        Assert.Equal(ObjectPlacementProblem.WouldBlockAccess, Check(simulation, "cabinet", new(2, 2), Direction.North));
        Assert.Equal(ObjectPlacementProblem.None, Check(simulation, "cabinet", new(4, 2), Direction.North));
    }

    [Fact]
    public void RejectsPlacementOnOtherPlayersLand()
    {
        var simulation = NewSimulationWithRoom();

        Assert.Equal(ObjectPlacementProblem.NotOwned, Check(simulation, "cabinet", new(2, 2), Direction.North, player: 1));
    }

    [Fact]
    public void IgnoresInvalidCommands()
    {
        var simulation = NewSimulationWithRoom();
        simulation.Schedule(0, new PlaceObjectCommand("cabinet", new(2, 2), Direction.North));
        simulation.Schedule(0, new PlaceObjectCommand("cabinet", new(2, 2), Direction.North));
        simulation.Step();

        Assert.Single(simulation.Entities.OfType<RoomObject>());
    }

    [Fact]
    public void SaveAndLoadKeepObjects()
    {
        var original = NewSimulationWithRoom();
        Place(original, "cabinet", new(2, 2), Direction.North);

        using var stream = new MemoryStream();
        original.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, Content);

        var cabinet = Assert.Single(restored.Entities.OfType<RoomObject>());
        Assert.Equal((new TilePosition(2, 2), Direction.North), (cabinet.Tile, cabinet.Orientation));
        Assert.Equal(cabinet.Id, restored.Map[2, 2].ObjectId);
        Assert.Equal(original.ComputeStateHash(), restored.ComputeStateHash());
    }

    private static ObjectPlacementProblem Check(Simulation simulation, string type, TilePosition tile, Direction orientation, int player = 0)
    {
        return ObjectPlacer.Check(simulation, type, tile, orientation, player);
    }

    private static void Place(Simulation simulation, string type, TilePosition tile, Direction orientation)
    {
        Assert.Equal(ObjectPlacementProblem.None, Check(simulation, type, tile, orientation));
        simulation.Schedule(0, new PlaceObjectCommand(type, tile, orientation));
        simulation.Step();
    }

    private static Simulation NewSimulationWithRoom()
    {
        var map = TestMaps.FromRows(
            "........",
            "........",
            "........",
            "........",
            "........",
            "........",
            "........",
            "........");
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Parcel = 1;
                map[x, y].Set(TileFlags.Buildable | TileFlags.Hospital | TileFlags.BuildableNorth | TileFlags.BuildableEast | TileFlags.BuildableSouth | TileFlags.BuildableWest, true);
            }
        }

        var simulation = new Simulation(map, seed: 5, Content);
        simulation.Schedule(0, new BuildRoomCommand(Plan));
        simulation.Step();
        Assert.Single(simulation.Entities.OfType<Room>());
        return simulation;
    }
}
