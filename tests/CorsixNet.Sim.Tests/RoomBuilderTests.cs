using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Pathfinding;
using CorsixNet.Sim.Rooms;

namespace CorsixNet.Sim.Tests;

public class RoomBuilderTests
{
    private const ushort External = 114;
    private static readonly WallSet Walls = new(InsideNorth: 82, InsideWest: 83, OutsideNorth: 90, OutsideWest: 91);
    private static readonly RoomDefinition Gp = new("gp", "GP's Office", MinimumSize: 3, FloorSprite: 18, Walls);
    private static readonly GameContent Content = new([Gp], [External]);

    // A 3x3 room at (2, 2)..(4, 4) with its door on the south side, opening onto (3, 5).
    private static readonly RoomPlan Plan = new("gp", new TileRect(2, 2, 3, 3), new DoorPlacement(new(3, 5), EdgeOrientation.North));

    [Fact]
    public void AcceptsAValidPlan()
    {
        Assert.Equal(RoomPlanProblem.None, RoomBuilder.Check(NewSimulation().Map, Content, Plan, player: 0));
    }

    [Theory]
    [InlineData("xray", 2, 2, 3, 3, RoomPlanProblem.UnknownRoomType)]
    [InlineData("gp", 2, 2, 2, 3, RoomPlanProblem.TooSmall)]
    [InlineData("gp", 7, 7, 3, 3, RoomPlanProblem.OutsideMap)]
    public void RejectsBadAreas(string type, int x, int y, int width, int height, RoomPlanProblem expected)
    {
        var map = NewSimulation().Map;

        Assert.Equal(expected, RoomBuilder.CheckArea(map, Content.FindRoom(type), new TileRect(x, y, width, height), player: 0));
    }

    [Fact]
    public void RejectsUnbuildableAndForeignTiles()
    {
        var map = NewSimulation().Map;
        var area = new TileRect(2, 2, 3, 3);

        map[3, 3].Set(TileFlags.Buildable, false);
        Assert.Equal(RoomPlanProblem.NotBuildable, RoomBuilder.CheckArea(map, Gp, area, player: 0));

        map[3, 3].Set(TileFlags.Buildable, true);
        map[3, 3].Parcel = 2;
        Assert.Equal(RoomPlanProblem.NotOwned, RoomBuilder.CheckArea(map, Gp, area, player: 0));
        Assert.Equal(RoomPlanProblem.NotOwned, RoomBuilder.CheckArea(map, Gp, area, player: 1));
    }

    [Fact]
    public void RejectsWallsInsideTheArea()
    {
        var map = NewSimulation().Map;
        map[3, 3].WestWall = 83;

        Assert.Equal(RoomPlanProblem.WallInside, RoomBuilder.CheckArea(map, Gp, new TileRect(2, 2, 3, 3), player: 0));
    }

    [Fact]
    public void RejectsBadDoors()
    {
        var map = NewSimulation().Map;

        Assert.Equal(RoomPlanProblem.DoorNotOnPerimeter, RoomBuilder.Check(map, Content, Plan with { Door = new(new(3, 3), EdgeOrientation.North) }, 0));

        map[3, 5].Set(TileFlags.Passable, false);
        Assert.Equal(RoomPlanProblem.DoorBlocked, RoomBuilder.Check(map, Content, Plan, 0));

        map[3, 5].Set(TileFlags.Passable, true);
        map[3, 5].NorthWall = External;
        Assert.Equal(RoomPlanProblem.DoorInWall, RoomBuilder.Check(map, Content, Plan, 0));
    }

    [Fact]
    public void BuildsWallsFloorAndDoor()
    {
        var simulation = NewSimulation();

        simulation.Schedule(0, new BuildRoomCommand(Plan));
        simulation.Step();

        var room = Assert.IsType<Room>(simulation.Entities[0]);
        var door = Assert.IsType<RoomDoor>(simulation.Entities[1]);
        var map = simulation.Map;
        Assert.Equal(door.Id, room.DoorId);
        Assert.Equal(room.Id, door.RoomId);

        for (var x = 2; x <= 4; x++)
        {
            Assert.Equal(82, map[x, 2].NorthWall);
            Assert.Equal(x == 3 ? 0 : 90, map[x, 5].NorthWall);
        }

        for (var y = 2; y <= 4; y++)
        {
            Assert.Equal(83, map[2, y].WestWall);
            Assert.Equal(91, map[5, y].WestWall);
            for (var x = 2; x <= 4; x++)
            {
                Assert.Equal(18, map[x, y].Floor);
                Assert.Equal(room.Id, map[x, y].RoomId);
                Assert.True(map[x, y].Has(TileFlags.Room));
            }
        }

        Assert.True(map[3, 5].Has(TileFlags.DoorNorth | TileFlags.TallNorth));
    }

    [Fact]
    public void PeopleEnterOnlyThroughTheDoor()
    {
        var simulation = NewSimulation();
        simulation.Schedule(0, new BuildRoomCommand(Plan));
        simulation.Step();
        var map = simulation.Map;

        Assert.False(map[2, 1].Has(TileFlags.CanTravelSouth));
        Assert.False(map[4, 4].Has(TileFlags.CanTravelEast));
        Assert.True(map[3, 5].Has(TileFlags.CanTravelNorth));

        var path = new List<TilePosition>();
        Assert.True(new Pathfinder().TryFindPath(map, new(0, 0), new(3, 3), path));
        Assert.Contains(new TilePosition(3, 5), path);
        Assert.Contains(new TilePosition(3, 4), path);
    }

    [Fact]
    public void KeepsOuterWallsAndNeighbouringWalls()
    {
        var simulation = NewSimulation();
        simulation.Map[2, 2].NorthWall = External;
        simulation.Map[5, 3].WestWall = 99;

        simulation.Schedule(0, new BuildRoomCommand(Plan));
        simulation.Step();

        Assert.Equal(External, simulation.Map[2, 2].NorthWall);
        Assert.Equal(82, simulation.Map[3, 2].NorthWall);
        Assert.Equal(99, simulation.Map[5, 3].WestWall);
        Assert.Equal(91, simulation.Map[5, 4].WestWall);
    }

    [Fact]
    public void IgnoresInvalidPlansAndOverlaps()
    {
        var simulation = NewSimulation();
        simulation.Schedule(0, new BuildRoomCommand(Plan));
        simulation.Schedule(0, new BuildRoomCommand(Plan));
        simulation.Schedule(1, new BuildRoomCommand(Plan with { Area = new TileRect(5, 5, 3, 3), Door = new(new(6, 5), EdgeOrientation.North) }));
        simulation.Step();

        // Only the first plan was built: the second overlaps it, the third is on land nobody owns.
        Assert.Single(simulation.Entities.OfType<Room>());
    }

    [Fact]
    public void SaveAndLoadKeepRooms()
    {
        var original = NewSimulation();
        original.Schedule(0, new BuildRoomCommand(Plan));
        original.Step();

        using var stream = new MemoryStream();
        original.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, Content);

        var room = Assert.IsType<Room>(restored.Entities[0]);
        Assert.Equal(("gp", new TileRect(2, 2, 3, 3)), (room.RoomType, room.Area));
        Assert.Equal(original.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Theory]
    [InlineData(3, 2, 3, 2, EdgeOrientation.North)]  // top row
    [InlineData(3, 1, 3, 2, EdgeOrientation.North)]  // just above
    [InlineData(3, 4, 3, 5, EdgeOrientation.North)]  // bottom row
    [InlineData(3, 5, 3, 5, EdgeOrientation.North)]  // just below
    [InlineData(2, 3, 2, 3, EdgeOrientation.West)]   // left column
    [InlineData(5, 3, 5, 3, EdgeOrientation.West)]   // just right
    public void FindsTheDoorNearATile(int x, int y, int doorX, int doorY, EdgeOrientation edge)
    {
        Assert.Equal(new DoorPlacement(new(doorX, doorY), edge), RoomBuilder.DoorNear(new TileRect(2, 2, 3, 3), new(x, y)));
    }

    [Fact]
    public void NoDoorAwayFromThePerimeter()
    {
        Assert.Null(RoomBuilder.DoorNear(new TileRect(2, 2, 3, 3), new(8, 8)));
    }

    private static Simulation NewSimulation()
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
        map.ParcelCount = 3;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Parcel = (ushort)(x >= 5 && y >= 5 ? 2 : 1);
                map[x, y].Set(TileFlags.Buildable | TileFlags.Hospital, true);
            }
        }

        return new Simulation(map, seed: 3, Content);
    }
}
