using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Serialization;
using static CorsixNet.Sim.Definitions.FootprintFlags;

namespace CorsixNet.Sim.Tests;

/// <summary>Windows, corners, and a room's life from blueprint to confirmation or cancellation.</summary>
public class RoomLifecycleTests
{
    private const ushort External = 114;
    private static readonly WallSet Walls = new(82, 83, 90, 91, WindowNorth: 128, WindowWest: 129);

    /// <summary>One occupied tile, used from the tile south of it.</summary>
    private static readonly ObjectDefinition Cabinet = new("cabinet", "Cabinet", 2, new Dictionary<Direction, ObjectOrientation>
    {
        [Direction.North] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition)]),
    });

    private static readonly RoomDefinition Office = new("office", "Office", 4, 18, Walls, new Dictionary<string, int> { ["cabinet"] = 2 });
    private static readonly RoomDefinition Cupboard = new("cupboard", "Cupboard", 3, 19, new WallSet(82, 83, 90, 91));
    private static readonly GameContent Content = new([Office, Cupboard], [External], [Cabinet]);

    // A 4x4 room at (2, 2)..(5, 5) with its door on the south side, opening onto (3, 6).
    private static readonly RoomPlan Plan = new("office", new TileRect(2, 2, 4, 4), new DoorPlacement(new(3, 6), EdgeOrientation.North));

    [Theory]
    [InlineData(2, 6, EdgeOrientation.North)] // south side, left corner
    [InlineData(5, 6, EdgeOrientation.North)] // south side, bottom corner
    [InlineData(2, 2, EdgeOrientation.West)]  // west side, top corner
    [InlineData(6, 5, EdgeOrientation.West)]  // east side, bottom corner
    public void RejectsDoorsAndWindowsInCorners(int x, int y, EdgeOrientation edge)
    {
        var map = NewSimulation().Map;
        var corner = new DoorPlacement(new(x, y), edge);

        Assert.Equal(RoomPlanProblem.DoorInCorner, RoomBuilder.Check(map, Content, Plan with { Door = corner }, 0));
        Assert.Equal(RoomPlanProblem.WindowInCorner, RoomBuilder.Check(map, Content, Plan with { Windows = [corner] }, 0));
    }

    [Theory]
    [InlineData(2, 2, 200, 10, 3, 2, EdgeOrientation.North)]  // top corner, right half: north wall
    [InlineData(2, 2, 10, 200, 2, 3, EdgeOrientation.West)]   // top corner, left half: west wall
    [InlineData(5, 5, 200, 10, 6, 4, EdgeOrientation.West)]   // bottom corner, right half: east wall
    [InlineData(5, 5, 10, 200, 4, 6, EdgeOrientation.North)]  // bottom corner, left half: south wall
    [InlineData(2, 5, 200, 200, 3, 6, EdgeOrientation.North)] // left corner, lower half: south wall
    [InlineData(2, 5, 10, 10, 2, 4, EdgeOrientation.West)]    // left corner, upper half: west wall
    [InlineData(5, 2, 200, 200, 6, 3, EdgeOrientation.West)]  // right corner, lower half: east wall
    [InlineData(5, 2, 10, 10, 4, 2, EdgeOrientation.North)]   // right corner, upper half: north wall
    [InlineData(1, 2, 128, 128, 2, 3, EdgeOrientation.West)]  // beside a corner, outside: the next wall along
    [InlineData(3, 1, 128, 128, 3, 2, EdgeOrientation.North)] // just outside the north wall
    public void PicksTheNearestWallThatIsNotACorner(int x, int y, int fractionX, int fractionY, int wallX, int wallY, EdgeOrientation edge)
    {
        var wall = RoomBuilder.WallNear(Plan.Area, new(x, y), fractionX, fractionY);

        Assert.Equal(new DoorPlacement(new(wallX, wallY), edge), wall);
        Assert.False(RoomBuilder.IsCorner(Plan.Area, wall!.Value));
    }

    [Fact]
    public void BuildsWindowsInsideAndOutside()
    {
        var simulation = NewSimulation();
        DoorPlacement[] windows = [new(new(4, 2), EdgeOrientation.North), new(new(6, 3), EdgeOrientation.West), new(new(4, 6), EdgeOrientation.North)];

        Run(simulation, new BuildRoomCommand(Plan with { Windows = windows }));

        var map = simulation.Map;
        Assert.Equal(128, map[4, 2].NorthWall);
        Assert.Equal(129, map[6, 3].WestWall);
        Assert.Equal(128, map[4, 6].NorthWall);
        Assert.Equal(82, map[3, 2].NorthWall);
        Assert.False(map[4, 1].Has(TileFlags.CanTravelSouth));
    }

    [Fact]
    public void RejectsBadWindows()
    {
        var map = NewSimulation().Map;
        var window = new DoorPlacement(new(4, 2), EdgeOrientation.North);

        Assert.Equal(RoomPlanProblem.WindowOnDoor, RoomBuilder.Check(map, Content, Plan with { Windows = [Plan.Door] }, 0));
        Assert.Equal(RoomPlanProblem.WindowNotOnPerimeter, RoomBuilder.Check(map, Content, Plan with { Windows = [new(new(4, 4), EdgeOrientation.North)] }, 0));
        Assert.Equal(RoomPlanProblem.WindowInWall, RoomBuilder.Check(map, Content, Plan with { Windows = [window, window] }, 0));
        Assert.Equal(RoomPlanProblem.NoWindows, RoomBuilder.Check(map, Content, new RoomPlan("cupboard", Plan.Area, Plan.Door, [window]), 0));

        map[4, 2].NorthWall = External;
        Assert.Equal(RoomPlanProblem.WindowInWall, RoomBuilder.Check(map, Content, Plan with { Windows = [window] }, 0));
    }

    [Fact]
    public void ARoomWorksOnlyOnceConfirmedWithEverythingItNeeds()
    {
        var simulation = NewSimulation();
        Run(simulation, new BuildRoomCommand(Plan));
        var room = Assert.Single(simulation.Entities.OfType<Room>());
        Assert.False(room.IsActive);
        Assert.Equal([("cabinet", 2)], RoomBuilder.MissingObjects(simulation, room));

        Run(simulation, new PlaceObjectCommand("cabinet", new(2, 2), Direction.North));
        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.False(room.IsActive);
        Assert.Equal([("cabinet", 1)], RoomBuilder.MissingObjects(simulation, room));

        Run(simulation, new PlaceObjectCommand("cabinet", new(5, 2), Direction.North));
        Run(simulation, new ConfirmRoomCommand(room.Id), player: 1);
        Assert.False(room.IsActive);

        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);

        // A working room can no longer be furnished, picked from or cancelled.
        Run(simulation, new PlaceObjectCommand("cabinet", new(4, 3), Direction.North));
        Run(simulation, new PickUpObjectCommand(simulation.Entities.OfType<RoomObject>().First().Id));
        Run(simulation, new CancelRoomCommand(room.Id));
        Assert.Equal(2, simulation.Entities.OfType<RoomObject>().Count());
        Assert.Contains(room, simulation.Entities);
    }

    [Fact]
    public void PickingUpAnObjectFreesItsTiles()
    {
        var simulation = NewSimulation();
        Run(simulation, new BuildRoomCommand(Plan));
        var furnished = SnapshotMap(simulation.Map);
        Run(simulation, new PlaceObjectCommand("cabinet", new(2, 2), Direction.North));
        Run(simulation, new PlaceObjectCommand("cabinet", new(4, 2), Direction.North));
        var first = simulation.Entities.OfType<RoomObject>().First();

        Run(simulation, new PickUpObjectCommand(first.Id), player: 1);
        Assert.Contains(first, simulation.Entities);

        Run(simulation, new PickUpObjectCommand(first.Id));
        Assert.DoesNotContain(first, simulation.Entities);
        var map = simulation.Map;
        Assert.True(map[2, 2].Has(TileFlags.Buildable | TileFlags.Passable));
        Assert.Equal(0, map[2, 2].ObjectId);
        Assert.True(map[2, 3].Has(TileFlags.Buildable | TileFlags.BuildableNorth));
        Assert.False(map[4, 2].Has(TileFlags.Passable));
        Assert.False(map[3, 5].Has(TileFlags.Buildable));

        Run(simulation, new PickUpObjectCommand(simulation.Entities.OfType<RoomObject>().Single().Id));
        Assert.Equal(furnished, SnapshotMap(map));
    }

    [Fact]
    public void CancellingARoomRestoresTheMap()
    {
        var simulation = NewSimulation();
        simulation.Map[2, 2].NorthWall = External;
        simulation.Map.UpdatePathfinding();
        simulation.Map.UpdateShadows();
        var before = SnapshotMap(simulation.Map);

        Run(simulation, new BuildRoomCommand(Plan with { Windows = [new(new(4, 2), EdgeOrientation.North)] }));
        var room = Assert.Single(simulation.Entities.OfType<Room>());
        Run(simulation, new PlaceObjectCommand("cabinet", new(2, 2), Direction.North));

        Run(simulation, new CancelRoomCommand(room.Id), player: 1);
        Assert.Contains(room, simulation.Entities);

        Run(simulation, new CancelRoomCommand(room.Id));
        Assert.Empty(simulation.Entities);
        Assert.Equal(before, SnapshotMap(simulation.Map));
    }

    [Fact]
    public void CancellingARoomGivesSharedWallsBackToTheNeighbour()
    {
        var simulation = NewSimulation();
        // The neighbour's east side has a window at (5, 2).
        var neighbour = new RoomPlan("office", new TileRect(1, 1, 4, 4), new DoorPlacement(new(2, 5), EdgeOrientation.North), [new(new(5, 2), EdgeOrientation.West)]);
        Run(simulation, new BuildRoomCommand(neighbour));
        var before = SnapshotMap(simulation.Map);

        // The new room's west side replaces the neighbour's outside walls with its own inside walls,
        // keeping the window a window.
        Run(simulation, new BuildRoomCommand(new RoomPlan("office", new TileRect(5, 1, 4, 5), new DoorPlacement(new(6, 6), EdgeOrientation.North))));
        var room = simulation.Entities.OfType<Room>().Last();
        Assert.Equal(83, simulation.Map[5, 3].WestWall);
        Assert.Equal(129, simulation.Map[5, 2].WestWall);
        Run(simulation, new CancelRoomCommand(room.Id));

        Assert.Single(simulation.Entities.OfType<Room>());
        Assert.Equal(before, SnapshotMap(simulation.Map));
    }

    [Fact]
    public void SaveAndLoadKeepRoomsUnderConstruction()
    {
        var original = NewSimulation();
        Run(original, new BuildRoomCommand(Plan with { Windows = [new(new(2, 4), EdgeOrientation.West)] }));
        Run(original, new PlaceObjectCommand("cabinet", new(2, 2), Direction.North));

        using var stream = new MemoryStream();
        original.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, Content);

        var room = Assert.Single(restored.Entities.OfType<Room>());
        Assert.False(room.IsActive);
        Assert.Equal(original.ComputeStateHash(), restored.ComputeStateHash());

        // The restored room can still be cancelled back to the original tiles.
        Run(original, new CancelRoomCommand(room.Id));
        Run(restored, new CancelRoomCommand(room.Id));
        Assert.Equal(SnapshotMap(original.Map), SnapshotMap(restored.Map));
    }

    [Fact]
    public void AnEmptyRoomCanBeEditedAndConfirmedAgainWithoutPayingTwice()
    {
        var simulation = NewSimulation(new LevelRules(10_000, [KeyValuePair.Create("office", 1000)], [KeyValuePair.Create("cabinet", 300)]));
        var room = BuildOffice(simulation);
        var hospital = simulation.Hospitals[0];
        var balance = hospital.Balance;

        Run(simulation, new EditRoomCommand(room.Id), player: 1);
        Assert.True(room.IsActive);

        Run(simulation, new EditRoomCommand(room.Id));
        Assert.False(room.IsActive);
        Assert.False(room.IsClosing);
        Assert.True(room.IsPaid);

        // Being edited, its objects can be moved.
        var cabinet = simulation.Entities.OfType<RoomObject>().First();
        Run(simulation, new PickUpObjectCommand(cabinet.Id));
        Assert.DoesNotContain(cabinet, simulation.Entities);
        Run(simulation, new PlaceObjectCommand("cabinet", new(2, 2), Direction.North));

        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        Assert.Equal(balance, hospital.Balance);
    }

    [Fact]
    public void TakingDownAPaidRoomGivesHalfItsCostBack()
    {
        var simulation = NewSimulation(new LevelRules(10_000, [KeyValuePair.Create("office", 1000)], [KeyValuePair.Create("cabinet", 300)]));
        var room = BuildOffice(simulation);
        var hospital = simulation.Hospitals[0];
        var balance = hospital.Balance;

        Run(simulation, new EditRoomCommand(room.Id));
        Run(simulation, new CancelRoomCommand(room.Id));

        // Half of 1000 + 2 * 300, less half a cabinet once for its kind.
        Assert.DoesNotContain(room, simulation.Entities);
        Assert.Equal(balance + 800 - 150, hospital.Balance);
    }

    [Fact]
    public void ARoomWithSomeoneInsideWaitsForThemToLeave()
    {
        var simulation = NewSimulation();
        var room = BuildOffice(simulation);
        var visitor = simulation.AddEntity(id => new Humanoid(id, new TilePosition(3, 3)));

        Run(simulation, new EditRoomCommand(room.Id));
        Assert.True(room.IsClosing);
        Run(simulation, new PickUpObjectCommand(simulation.Entities.OfType<RoomObject>().First().Id));
        Run(simulation, new CancelRoomCommand(room.Id));
        Assert.Equal(2, simulation.Entities.OfType<RoomObject>().Count());
        Assert.Contains(room, simulation.Entities);

        visitor.PlaceAt(new TilePosition(9, 9));
        simulation.Step();
        Assert.False(room.IsClosing);
        Assert.False(room.IsActive);
    }

    [Fact]
    public void SavesKeepWhetherARoomIsPaidAndClosing()
    {
        var simulation = NewSimulation();
        var room = BuildOffice(simulation);
        simulation.AddEntity(id => new Humanoid(id, new TilePosition(3, 3)));
        Run(simulation, new EditRoomCommand(room.Id));

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, Content);

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        var copy = (Room)restored.FindEntity(room.Id)!;
        Assert.True(copy.IsPaid);
        Assert.True(copy.IsClosing);

        using var wire = new MemoryStream();
        new EditRoomCommand(5).Write(new SimWriter(wire));
        wire.Position = 0;
        Assert.Equal(new EditRoomCommand(5), Command.Read(new SimReader(wire)));
    }

    private static Room BuildOffice(Simulation simulation)
    {
        Run(simulation, new BuildRoomCommand(Plan));
        var room = Assert.Single(simulation.Entities.OfType<Room>());
        Run(simulation, new PlaceObjectCommand("cabinet", new(2, 2), Direction.North));
        Run(simulation, new PlaceObjectCommand("cabinet", new(5, 2), Direction.North));
        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        return room;
    }

    private static void Run(Simulation simulation, Command command, int player = 0)
    {
        simulation.Schedule(player, command);
        simulation.Step();
    }

    private static List<MapTile> SnapshotMap(LevelMap map)
    {
        var tiles = new List<MapTile>();
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                tiles.Add(map[x, y]);
            }
        }

        return tiles;
    }

    private static Simulation NewSimulation(LevelRules? rules = null)
    {
        var map = TestMaps.FromRows(
            "............",
            "............",
            "............",
            "............",
            "............",
            "............",
            "............",
            "............",
            "............",
            "............");
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

        map.UpdatePathfinding();
        map.UpdateShadows();
        return new Simulation(map, seed: 7, Content, rules);
    }
}
