using OpenWards.Client.Maps;
using OpenWards.Sim;
using OpenWards.Sim.Definitions;
using OpenWards.Sim.Diseases;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Objects;
using OpenWards.Sim.People;
using static OpenWards.Sim.Definitions.FootprintFlags;

namespace OpenWards.Client.Tests;

/// <summary>
/// The frames the map draws of an object use's animations (CorsixTH's entity.lua <c>Entity:tick</c>: the frame
/// advances before the timer that sets the next animation, so every animation is drawn from its first frame to its
/// last, once each).
/// </summary>
public class ActionAnimationFrameTests
{
    private static readonly AnimationRef Begin = new(1000, 3);
    private static readonly AnimationRef InUse = new(1001, 4);
    private static readonly AnimationRef Finish = new(1002, 2);

    private static readonly ObjectDefinition Machine = new(
        "test_machine",
        "Test Machine",
        200,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.South] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition)], AnimateFromUsePosition: true),
        },
        UsageSouth: new Dictionary<HumanoidType, UseAnimations>
        {
            [HumanoidType.StandardMalePatient] = new(Begin, [InUse], Finish),
        });

    [Fact]
    public void EachAnimationIsDrawnFromItsFirstFrameToItsLastOnce()
    {
        var simulation = NewSimulation();
        var placed = ObjectPlacer.Place(simulation, Machine, new TilePosition(4, 2), Direction.South);
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, new TilePosition(4, 3), simulation.Random, PatientLook.Standard, 0));
        humanoid.Type = HumanoidType.StandardMalePatient;

        // Started during the update of a tick, as a behaviour does; the map draws once that tick is over.
        var use = ObjectUse.Start(simulation, humanoid, placed, prolonged: false);
        var drawn = new List<(int Animation, long Frame)>();
        for (var i = 0; i < 20 && use.Phase != ObjectUsePhase.Done; i++)
        {
            simulation.Step();
            if (humanoid.ActionAnimation is { } animation)
            {
                drawn.Add((animation.Id, HumanoidDrawing.ActionElapsed(simulation.Tick, humanoid.ActionAnimationStart) % animation.Length));
            }

            use.Update(simulation, humanoid);
        }

        Assert.Equal(
            [(1000, 0), (1000, 1), (1000, 2), (1001, 0), (1001, 1), (1001, 2), (1001, 3), (1002, 0), (1002, 1)],
            drawn);
    }

    [Fact]
    public void ADrawBeforeTheTickTheAnimationStartedIsAtItsFirstFrame()
    {
        Assert.Equal(0, HumanoidDrawing.ActionElapsed(tick: 5, start: 5));
        Assert.Equal(0, HumanoidDrawing.ActionElapsed(tick: 6, start: 5));
        Assert.Equal(1, HumanoidDrawing.ActionElapsed(tick: 7, start: 5));
    }

    private static Simulation NewSimulation()
    {
        const int size = 10;
        var map = new LevelMap(size, size, playerCount: 1);
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                ref var tile = ref map[x, y];
                tile.Floor = 1;
                tile.Parcel = 1;
                tile.Set(TileFlags.Passable | TileFlags.Buildable | TileFlags.Hospital | TileFlags.BuildableNorth | TileFlags.BuildableEast | TileFlags.BuildableSouth | TileFlags.BuildableWest, true);
            }
        }

        map.UpdatePathfinding();
        return new Simulation(map, seed: 1, new GameContent([], [114], [Machine]), new LevelRules(10_000, [], []));
    }
}
