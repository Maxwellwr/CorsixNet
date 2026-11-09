using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Staff;
using static CorsixNet.Sim.Definitions.FootprintFlags;

namespace CorsixNet.Sim.Tests;

/// <summary>Handymen sweeping litter and watering plants (CorsixTH's handyman.lua, sweep_floor.lua, plant.lua).</summary>
public class HandymanTests
{
    private static readonly ObjectDefinition Plant = new("plant", "Plant", 45, new Dictionary<Direction, ObjectOrientation>
    {
        [Direction.North] = new([new(0, 0, CompleteCell)]),
        [Direction.South] = new([new(0, 0, CompleteCell)]),
    }, CorridorObject: true);

    [Fact]
    public void AHandymanSweepsUpTheLitterOfHisHospital()
    {
        var simulation = NewSimulation();
        var mine = Litter.Drop(simulation, 0, LitterType.Puke, new TilePosition(10, 10), isMirrored: false)!;
        var other = Litter.Drop(simulation, 0, LitterType.SodaCan, new TilePosition(2, 12), isMirrored: false)!;
        var soot = Litter.Drop(simulation, 0, LitterType.SootFloor, new TilePosition(12, 2), isMirrored: false)!;
        var handyman = Hire(simulation, new TilePosition(3, 3));

        var swept = false;
        for (var tick = 0; tick < 2000 && !(mine.IsRemoved && other.IsRemoved); tick++)
        {
            simulation.Step();
            swept |= handyman is { Behaviour: HandymanBehaviour { Phase: HandymanPhase.Sweeping }, ActionAnimation.Id: 1874 or 1878 };
        }

        Assert.True(swept);
        Assert.True(mine.IsRemoved);
        Assert.True(other.IsRemoved);
        Assert.False(soot.IsRemoved);

        // With nothing left to do he wanders about again.
        for (var tick = 0; tick < 100; tick++)
        {
            simulation.Step();
        }

        Assert.Equal(HandymanPhase.Meandering, Assert.IsType<HandymanBehaviour>(handyman.Behaviour).Phase);
        Assert.Null(handyman.ActionAnimation);
    }

    [Fact]
    public void AHandymanWatersAThirstyPlantBackToHealth()
    {
        var simulation = NewSimulation();
        Assert.Equal(ObjectPlacementProblem.None, ObjectPlacer.Check(simulation, "plant", new TilePosition(9, 9), Direction.South, 0));
        simulation.Schedule(0, new PlaceObjectCommand("plant", new TilePosition(9, 9), Direction.South));
        simulation.Step();
        var plant = Assert.Single(simulation.Entities.OfType<RoomObject>()).Plant!;
        for (var day = 0; day < 64 * 2; day++)
        {
            plant.OnEndDay(0m);
        }

        Assert.Equal(2, plant.State);
        var handyman = Hire(simulation, new TilePosition(3, 3));

        // He waters it from a tile beside it, back to health as he starts.
        TilePosition? wateredFrom = null;
        for (var tick = 0; tick < 2000 && wateredFrom is null; tick++)
        {
            simulation.Step();
            if (handyman.ActionAnimation?.Id is 1972 or 1974 or 1980 or 1982)
            {
                wateredFrom = handyman.Tile;
            }
        }

        Assert.NotNull(wateredFrom);
        Assert.Equal(1, Math.Abs(wateredFrom.Value.X - 9) + Math.Abs(wateredFrom.Value.Y - 9));
        Assert.Equal(PlantState.Healthy, plant.State);
        Assert.Equal(PlantState.DaysBetweenStates, plant.DaysLeft);
    }

    [Fact]
    public void WhatAHandymanIsDoingIsSaved()
    {
        var simulation = NewSimulation();
        Litter.Drop(simulation, 0, LitterType.Paper, new TilePosition(12, 12), isMirrored: false);
        var handyman = Hire(simulation, new TilePosition(3, 3));
        for (var tick = 0; tick < 50; tick++)
        {
            simulation.Step();
        }

        Assert.Equal(HandymanPhase.WalkingToLitter, Assert.IsType<HandymanBehaviour>(handyman.Behaviour).Phase);
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, simulation.Content);

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        var copy = Assert.IsType<HandymanBehaviour>(restored.Entities.OfType<Humanoid>().Single().Behaviour);
        Assert.Equal(HandymanPhase.WalkingToLitter, copy.Phase);
        Assert.NotEqual(0, copy.TargetId);
    }

    private static Humanoid Hire(Simulation simulation, TilePosition tile)
    {
        simulation.Schedule(0, new HireStaffCommand(StaffClass.Handyman, 0, tile));
        simulation.Step();
        return simulation.Entities.OfType<Humanoid>().Single(h => h.Profile?.Class == StaffClass.Handyman);
    }

    private static Simulation NewSimulation()
    {
        var map = TestMaps.FromRows([.. Enumerable.Repeat("................", 16)]);
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
        return new Simulation(map, seed: 3, new GameContent([], [], [Plant]));
    }
}
