using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Staff;
using static CorsixNet.Sim.Definitions.FootprintFlags;

namespace CorsixNet.Sim.Tests;

/// <summary>Plants drooping without water (CorsixTH's objects/plant.lua).</summary>
public class PlantTests
{
    private static readonly ObjectDefinition Plant = new("plant", "Plant", 45, new Dictionary<Direction, ObjectOrientation>
    {
        [Direction.North] = new([new(0, 0, CompleteCell)]),
        [Direction.South] = new([new(0, 0, CompleteCell)]),
    }, CorridorObject: true);

    private static readonly ObjectDefinition Bin = new("bin", "Bin", 50, new Dictionary<Direction, ObjectOrientation>
    {
        [Direction.North] = new([new(0, 0, OnlySide)]),
    }, CorridorObject: true);

    [Fact]
    public void APlantCountsDownOneDayPlusTheWarmthOfItsTile()
    {
        var state = new PlantState();
        Assert.Equal(PlantState.Healthy, state.State);
        Assert.False(state.NeedsWatering);
        Assert.Equal(5, state.PleasingFactor);

        state.OnEndDay(0.25m);

        Assert.Equal(PlantState.DaysBetweenStates - 1.25m, state.DaysLeft);
    }

    [Fact]
    public void EverySixtyFourDaysAtNoWarmthItGetsAStateWorseUntilDead()
    {
        var state = new PlantState();
        for (var day = 0; day < 63; day++)
        {
            state.OnEndDay(0m);
        }

        Assert.Equal(PlantState.Healthy, state.State);
        Assert.True(state.IsDying); // One day left.

        state.OnEndDay(0m);
        Assert.Equal(1, state.State);
        Assert.True(state.NeedsWatering);
        Assert.Equal(PlantState.DaysBetweenStates, state.DaysLeft);

        for (var day = 0; day < 64 * 10; day++)
        {
            state.OnEndDay(0m);
        }

        Assert.Equal(PlantState.Dead, state.State);
        Assert.Equal(1, state.PleasingFactor);
    }

    [Fact]
    public void APlacedPlantDroopsDayByDayAndSavesKeepIt()
    {
        var simulation = NewSimulation();
        Place(simulation, "plant", new TilePosition(3, 3));
        var plant = Assert.Single(simulation.Entities.OfType<RoomObject>()).Plant;
        Assert.NotNull(plant);

        while (simulation.Date.Day == 1)
        {
            simulation.Step();
        }

        Assert.InRange(plant.DaysLeft, 62.8m, 62.9m); // 1 + about 0.125 for the tile's 6 °C.
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, simulation.Content);

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(plant.DaysLeft, Assert.Single(restored.Entities.OfType<RoomObject>()).Plant!.DaysLeft);
    }

    [Fact]
    public void OtherObjectsHaveNoPlantState()
    {
        var simulation = NewSimulation();
        Place(simulation, "bin", new TilePosition(3, 3), Direction.North);

        Assert.Null(Assert.Single(simulation.Entities.OfType<RoomObject>()).Plant);
    }

    [Fact]
    public void ADeadPlantMakesStaffUnhappy()
    {
        var simulation = NewSimulation();
        Place(simulation, "plant", new TilePosition(3, 3));
        var plant = Assert.Single(simulation.Entities.OfType<RoomObject>()).Plant!;
        for (var day = 0; day < 64 * 4; day++)
        {
            plant.OnEndDay(0m);
        }

        simulation.Schedule(0, new HireStaffCommand(StaffClass.Handyman, 0, new TilePosition(4, 4)));
        simulation.Step();
        var handyman = simulation.Entities.OfType<Humanoid>().Single();
        handyman.Happiness = 0.5m;
        handyman.Warmth = 0.3m;

        Feelings.OnEndDay(simulation, handyman);

        // Comfortable 0.005, and the dead plant −0.003 + 1 × 0.001.
        Assert.Equal(0.5m + 0.005m - 0.002m, handyman.Happiness);
    }

    private static void Place(Simulation simulation, string type, TilePosition tile, Direction orientation = Direction.South)
    {
        Assert.Equal(ObjectPlacementProblem.None, ObjectPlacer.Check(simulation, type, tile, orientation, 0));
        simulation.Schedule(0, new PlaceObjectCommand(type, tile, orientation));
        simulation.Step();
    }

    private static Simulation NewSimulation()
    {
        var map = TestMaps.FromRows([.. Enumerable.Repeat("........", 8)]);
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
        return new Simulation(map, seed: 3, new GameContent([], [], [Plant, Bin]));
    }
}
