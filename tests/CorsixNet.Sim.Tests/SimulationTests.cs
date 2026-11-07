using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Sim.Tests;

public class SimulationTests
{
    [Fact]
    public void ExecutesCommandsOnTheirTickBeforeEntitiesUpdate()
    {
        var simulation = NewSimulation();
        simulation.Schedule(0, new SpawnHumanoidCommand(new(1, 1)), tick: 2);

        simulation.Step();
        simulation.Step();
        Assert.Empty(simulation.Entities);

        simulation.Step();
        var humanoid = Assert.IsType<Humanoid>(Assert.Single(simulation.Entities));
        Assert.Equal(1, humanoid.Id);
        Assert.Equal(3, simulation.Tick);
    }

    [Fact]
    public void OrdersCommandsBySequenceWithinATick()
    {
        var simulation = NewSimulation();
        simulation.Schedule(new ScheduledCommand(0, Sequence: 5, Player: 0, new WalkToCommand(1, new(4, 1))));
        simulation.Schedule(new ScheduledCommand(0, Sequence: 2, Player: 0, new SpawnHumanoidCommand(new(1, 1))));

        Assert.IsType<SpawnHumanoidCommand>(simulation.PendingCommands[0].Command);

        simulation.Step();

        // The spawn ran first, so the walk found its humanoid.
        var humanoid = (Humanoid)simulation.Entities[0];
        Assert.Equal(new TilePosition(4, 1), humanoid.Destination);
    }

    [Fact]
    public void RejectsCommandsForPastTicks()
    {
        var simulation = NewSimulation();
        simulation.Step();

        Assert.Throws<ArgumentException>(() => simulation.Schedule(new ScheduledCommand(0, 0, 0, new SpawnHumanoidCommand(new(1, 1)))));
    }

    [Fact]
    public void IgnoresInvalidCommands()
    {
        var simulation = NewSimulation();
        simulation.Schedule(0, new SpawnHumanoidCommand(new(0, 0)));     // impassable
        simulation.Schedule(0, new SpawnHumanoidCommand(new(99, 99)));   // outside
        simulation.Schedule(0, new WalkToCommand(42, new(1, 1)));        // no such entity

        simulation.Step();

        Assert.Empty(simulation.Entities);
    }

    [Fact]
    public void HumanoidWalksOneTilePerStep()
    {
        var simulation = NewSimulation();
        var humanoid = Spawn(simulation, new(1, 1));
        simulation.Schedule(0, new WalkToCommand(humanoid.Id, new(3, 1)));

        simulation.Step();
        Assert.True(humanoid.IsStepping);
        Assert.Equal(Direction.East, humanoid.Facing);
        Assert.Equal(new TilePosition(1, 1), humanoid.Tile);

        for (var i = 0; i < Humanoid.TicksPerStep; i++)
        {
            simulation.Step();
        }

        Assert.Equal(new TilePosition(2, 1), humanoid.Tile);
        Assert.True(humanoid.IsStepping);

        for (var i = 0; i < Humanoid.TicksPerStep; i++)
        {
            simulation.Step();
        }

        Assert.Equal(new TilePosition(3, 1), humanoid.Tile);
        Assert.False(humanoid.IsStepping);
        Assert.Null(humanoid.Destination);
    }

    [Fact]
    public void HumanoidReplansWhenTheMapChanges()
    {
        var simulation = NewSimulation();
        var humanoid = Spawn(simulation, new(1, 1));
        simulation.Schedule(0, new WalkToCommand(humanoid.Id, new(4, 1)));
        simulation.Step();

        // Block the direct route; the humanoid must detour through row 2.
        TestMaps.AddWestWall(simulation.Map, 3, 1);
        RunUntilIdle(simulation, humanoid);

        Assert.Equal(new TilePosition(4, 1), humanoid.Tile);
    }

    [Fact]
    public void RemovesEntitiesWithoutReusingIds()
    {
        var simulation = NewSimulation();
        var first = Spawn(simulation, new(1, 1));

        Assert.True(simulation.RemoveEntity(first.Id));
        Assert.False(simulation.RemoveEntity(first.Id));

        var second = Spawn(simulation, new(1, 1));
        Assert.Equal(first.Id + 1, second.Id);
        Assert.Same(second, simulation.FindEntity(second.Id));
        Assert.Null(simulation.FindEntity(first.Id));
    }

    [Fact]
    public void SameCommandsGiveSameState()
    {
        var a = Scripted();
        var b = Scripted();

        Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
    }

    [Fact]
    public void DifferentCommandsGiveDifferentState()
    {
        var a = Scripted();
        var b = Scripted();
        b.Schedule(0, new SpawnHumanoidCommand(new(1, 2)));
        b.Step();
        a.Step();

        Assert.NotEqual(a.ComputeStateHash(), b.ComputeStateHash());
    }

    [Fact]
    public void StateHashIgnoresCommandsNotYetExecuted()
    {
        var a = Scripted();
        var b = Scripted();
        b.Schedule(0, new SpawnHumanoidCommand(new(2, 2)), tick: b.Tick + 10);

        Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
    }

    [Fact]
    public void SaveAndLoadRoundTripsMidWalk()
    {
        var original = Scripted();
        original.Schedule(0, new SpawnHumanoidCommand(new(3, 3)), tick: original.Tick + 5);

        using var stream = new MemoryStream();
        original.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);

        Assert.Equal(original.ComputeStateHash(), restored.ComputeStateHash());

        // Both continue identically, including the pending command.
        for (var i = 0; i < 40; i++)
        {
            original.Step();
            restored.Step();
        }

        Assert.Equal(original.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(original.Entities.Count, restored.Entities.Count);
    }

    [Fact]
    public void LoadRejectsForeignData()
    {
        using var stream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.Throws<InvalidDataException>(() => Simulation.Load(stream));
    }

    [Fact]
    public void LoadRejectsTruncatedData()
    {
        using var full = new MemoryStream();
        Scripted().Save(full);
        using var truncated = new MemoryStream(full.ToArray()[..^3]);

        Assert.Throws<InvalidDataException>(() => Simulation.Load(truncated));
    }

    private static Simulation NewSimulation()
    {
        return new Simulation(
            TestMaps.FromRows(
                "######",
                "#....#",
                "#....#",
                "#....#",
                "######"),
            seed: 1234);
    }

    /// <summary>A fixed scenario: two humanoids, one mid-walk.</summary>
    private static Simulation Scripted()
    {
        var simulation = NewSimulation();
        simulation.Schedule(0, new SpawnHumanoidCommand(new(1, 1)));
        simulation.Schedule(0, new SpawnHumanoidCommand(new(4, 3)));
        simulation.Schedule(0, new WalkToCommand(1, new(4, 1)), tick: 1);
        simulation.Schedule(0, new WalkToCommand(2, new(1, 3)), tick: 3);
        for (var i = 0; i < 13; i++)
        {
            simulation.Step();
        }

        return simulation;
    }

    private static Humanoid Spawn(Simulation simulation, TilePosition tile)
    {
        return simulation.AddEntity(id => new Humanoid(id, tile));
    }

    private static void RunUntilIdle(Simulation simulation, Humanoid humanoid)
    {
        for (var i = 0; i < 1000 && humanoid.Destination is not null; i++)
        {
            simulation.Step();
        }
    }
}
