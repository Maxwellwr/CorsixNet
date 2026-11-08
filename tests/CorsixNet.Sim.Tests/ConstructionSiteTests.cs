using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;

namespace CorsixNet.Sim.Tests;

/// <summary>Clearing the area of a new blueprint before the room is built (CorsixTH's clear_area phase).</summary>
public class ConstructionSiteTests
{
    private static readonly RoomDefinition Cupboard = new("cupboard", "Cupboard", 3, 19, new WallSet(82, 83, 90, 91));
    private static readonly GameContent Content = new([Cupboard], [], []);

    // A 4x4 room at (4, 2)..(7, 5) with its door on the south side, opening onto (5, 6).
    private static readonly RoomPlan Plan = new("cupboard", new TileRect(4, 2, 4, 4), new DoorPlacement(new(5, 6), EdgeOrientation.North));

    [Fact]
    public void MarkingABlueprintMakesItsTilesAvoidedUntilUnmarked()
    {
        var simulation = NewSimulation();

        Run(simulation, new MarkBlueprintCommand("cupboard", Plan.Area));
        Assert.True(simulation.Map[4, 2].Has(TileFlags.AvoidTile));
        Assert.True(simulation.Map[7, 5].Has(TileFlags.AvoidTile));
        Assert.False(simulation.Map[8, 5].Has(TileFlags.AvoidTile));
        Assert.NotNull(ConstructionSite.Of(simulation, 0));

        Run(simulation, new UnmarkBlueprintCommand());
        Assert.False(simulation.Map[4, 2].Has(TileFlags.AvoidTile));
        Assert.Null(ConstructionSite.Of(simulation, 0));
        Assert.Empty(simulation.Entities.OfType<ConstructionSite>());
    }

    [Fact]
    public void MarkingAnotherAreaFreesThePreviousOne()
    {
        var simulation = NewSimulation();

        Run(simulation, new MarkBlueprintCommand("cupboard", Plan.Area));
        Run(simulation, new MarkBlueprintCommand("cupboard", new TileRect(1, 1, 3, 3)));

        Assert.False(simulation.Map[5, 3].Has(TileFlags.AvoidTile));
        Assert.True(simulation.Map[2, 2].Has(TileFlags.AvoidTile));
        Assert.Single(simulation.Entities.OfType<ConstructionSite>());
    }

    [Fact]
    public void WithNobodyInTheWayTheRoomIsBuiltAtOnceAndTheTilesAreFreed()
    {
        var simulation = NewSimulation();
        Run(simulation, new MarkBlueprintCommand("cupboard", Plan.Area));

        Run(simulation, new BuildRoomCommand(Plan));

        Assert.Single(simulation.Entities.OfType<Room>());
        Assert.Empty(simulation.Entities.OfType<ConstructionSite>());
        Assert.False(simulation.Map[5, 3].Has(TileFlags.AvoidTile));
    }

    [Fact]
    public void SomeoneOnTheBlueprintLeavesBeforeTheRoomIsBuilt()
    {
        var simulation = NewSimulation();
        Run(simulation, new SpawnHumanoidCommand(new(5, 3)));
        var humanoid = simulation.Entities.OfType<Humanoid>().Single();

        Run(simulation, new BuildRoomCommand(Plan));

        Assert.Empty(simulation.Entities.OfType<Room>());
        var site = ConstructionSite.Of(simulation, 0);
        Assert.NotNull(site);
        Assert.True(site.IsWaiting);
        Assert.Equal([humanoid.Id], site.Watched);
        Assert.True(humanoid.IsMoving);

        Step(simulation, 200);

        var room = Assert.Single(simulation.Entities.OfType<Room>());
        Assert.False(room.Area.Contains(humanoid.Tile));
        Assert.Empty(simulation.Entities.OfType<ConstructionSite>());
        Assert.False(simulation.Map[5, 3].Has(TileFlags.AvoidTile));
    }

    [Fact]
    public void SomeoneOnTheBlueprintStartsLeavingAsSoonAsItIsMarked()
    {
        var simulation = NewSimulation();
        Run(simulation, new SpawnHumanoidCommand(new(5, 3)));
        var humanoid = simulation.Entities.OfType<Humanoid>().Single();

        Run(simulation, new MarkBlueprintCommand("cupboard", Plan.Area));
        Step(simulation, 200);
        Assert.False(humanoid.IsMoving);
        Assert.False(humanoid.IsObscuringArea(Plan.Area.X - 1, Plan.Area.Right, Plan.Area.Y - 1, Plan.Area.Bottom));

        // The people told to leave have gone, so the room goes up when the plan is complete.
        Run(simulation, new BuildRoomCommand(Plan));
        Assert.Single(simulation.Entities.OfType<Room>());
    }

    [Fact]
    public void GoingBackWhileWaitingBuildsNothing()
    {
        var simulation = NewSimulation();
        Run(simulation, new SpawnHumanoidCommand(new(5, 3)));
        Run(simulation, new BuildRoomCommand(Plan));

        Run(simulation, new UnmarkBlueprintCommand());
        Step(simulation, 200);

        Assert.Empty(simulation.Entities.OfType<Room>());
        Assert.Empty(simulation.Entities.OfType<ConstructionSite>());
        Assert.False(simulation.Map[5, 3].Has(TileFlags.AvoidTile));
    }

    [Fact]
    public void SomeoneJustOutsideIsOnlyInTheWayWhenWalkingIn()
    {
        var simulation = NewSimulation();
        Run(simulation, new SpawnHumanoidCommand(new(3, 3)));
        var humanoid = simulation.Entities.OfType<Humanoid>().Single();
        var (x1, x2, y1, y2) = (Plan.Area.X - 1, Plan.Area.Right, Plan.Area.Y - 1, Plan.Area.Bottom);

        Assert.False(humanoid.IsObscuringArea(x1, x2, y1, y2));

        Run(simulation, new WalkToCommand(humanoid.Id, new(6, 3)));
        Assert.True(humanoid.IsObscuringArea(x1, x2, y1, y2));
    }

    [Fact]
    public void PathsGoAroundAMarkedBlueprint()
    {
        var simulation = NewSimulation();
        Run(simulation, new MarkBlueprintCommand("cupboard", Plan.Area));

        var path = new List<TilePosition>();
        Assert.True(simulation.Pathfinder.TryFindPath(simulation.Map, new(2, 3), new(10, 3), path));

        Assert.DoesNotContain(path, Plan.Area.Contains);
    }

    [Fact]
    public void SomeoneWalkingAcrossTheBlueprintGoesAroundIt()
    {
        var simulation = NewSimulation();
        Run(simulation, new SpawnHumanoidCommand(new(2, 3)));
        var humanoid = simulation.Entities.OfType<Humanoid>().Single();
        Run(simulation, new WalkToCommand(humanoid.Id, new(10, 3)));
        Assert.Contains(humanoid.RemainingPath, Plan.Area.Contains);

        // The walk was planned before the blueprint; on reaching it, the humanoid plans again.
        Run(simulation, new MarkBlueprintCommand("cupboard", Plan.Area));
        for (var i = 0; i < 200; i++)
        {
            Assert.False(Plan.Area.Contains(humanoid.Tile));
            simulation.Step();
        }

        Assert.Equal(new TilePosition(10, 3), humanoid.Tile);
    }

    [Fact]
    public void IdleTilesAreNeverInABlueprint()
    {
        var simulation = NewSimulation();
        Run(simulation, new MarkBlueprintCommand("cupboard", Plan.Area));

        for (var n = 0; n < 30; n++)
        {
            Assert.True(simulation.Pathfinder.TryFindIdleTile(simulation.Map, new(5, 3), n, out var tile));
            Assert.False(Plan.Area.Contains(tile));
        }
    }

    [Fact]
    public void ASavedGameWaitingForTheAreaToClearContinuesIdentically()
    {
        var original = NewSimulation();
        Run(original, new SpawnHumanoidCommand(new(5, 3)));
        Run(original, new BuildRoomCommand(Plan));
        Step(original, 3);

        using var stream = new MemoryStream();
        original.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, Content);
        Assert.Equal(original.ComputeStateHash(), restored.ComputeStateHash());

        Step(original, 200);
        Step(restored, 200);

        Assert.Equal(original.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Single(restored.Entities.OfType<Room>());
    }

    private static void Run(Simulation simulation, Command command, int player = 0)
    {
        simulation.Schedule(player, command);
        simulation.Step();
    }

    private static void Step(Simulation simulation, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            simulation.Step();
        }
    }

    private static Simulation NewSimulation()
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
        return new Simulation(map, seed: 7, Content);
    }
}
