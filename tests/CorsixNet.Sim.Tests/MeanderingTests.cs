using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Sim.Tests;

/// <summary>Staff with nothing to do wander about (CorsixTH's humanoid_actions/meander.lua).</summary>
public class MeanderingTests
{
    [Theory]
    [InlineData(StaffClass.Doctor)]
    [InlineData(StaffClass.Nurse)]
    [InlineData(StaffClass.Receptionist)]
    [InlineData(StaffClass.Handyman)]
    public void IdleStaffWalkAndStandByTurns(StaffClass staffClass)
    {
        var simulation = NewSimulation(".........");
        var staff = Hire(simulation, staffClass);

        var tiles = new HashSet<TilePosition> { staff.Tile };
        var longestStill = 0;
        var still = 0;
        for (var tick = 0; tick < 2000; tick++)
        {
            simulation.Step();
            tiles.Add(staff.Tile);
            still = staff.IsMoving ? 0 : still + 1;
            longestStill = Math.Max(longestStill, still);
        }

        Assert.True(tiles.Count > 3, $"Visited only {tiles.Count} tiles.");
        Assert.InRange(longestStill, 5, 42);
    }

    [Fact]
    public void WithNowhereToGoTheyStandStill()
    {
        var simulation = NewSimulation(".");
        var staff = Hire(simulation, StaffClass.Doctor);

        for (var tick = 0; tick < 100; tick++)
        {
            simulation.Step();
            Assert.False(staff.IsMoving);
        }
    }

    [Fact]
    public void MeanderingSurvivesASave()
    {
        var simulation = NewSimulation(".........");
        Hire(simulation, StaffClass.Doctor);
        for (var tick = 0; tick < 57; tick++)
        {
            simulation.Step();
        }

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        for (var tick = 0; tick < 300; tick++)
        {
            simulation.Step();
            restored.Step();
        }

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void AHandymanKeepsToHisParcel()
    {
        // The west half is parcel 1, the east half parcel 2; the player owns both, and a third that does not exist.
        var simulation = NewSimulation("..........");
        var map = simulation.Map;
        map.ParcelCount = 3;
        map.SetParcelOwner(2, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 5; x < map.Width; x++)
            {
                map[x, y].Parcel = 2;
            }
        }

        var handyman = Hire(simulation, StaffClass.Handyman);
        var behaviour = (HandymanBehaviour)handyman.Behaviour!;
        Run(simulation, new SetHandymanParcelCommand(handyman.Id, 3));
        Assert.Equal(0, behaviour.Parcel);
        Run(simulation, new SetHandymanParcelCommand(handyman.Id, 2));
        Assert.Equal(2, behaviour.Parcel);

        // Once he has walked over, he stays.
        for (var tick = 0; tick < 2000; tick++)
        {
            simulation.Step();
            if (tick > 300 && !handyman.IsMoving)
            {
                Assert.Equal(2, map[handyman.Tile.X, handyman.Tile.Y].Parcel);
            }
        }

        Run(simulation, new SetHandymanParcelCommand(handyman.Id, 0));
        Assert.Equal(0, behaviour.Parcel);
    }

    private static void Run(Simulation simulation, Command command)
    {
        simulation.Schedule(0, command);
        simulation.Step();
    }

    private static Humanoid Hire(Simulation simulation, StaffClass staffClass)
    {
        simulation.Schedule(0, new HireStaffCommand(staffClass, 0, new TilePosition(0, 0)));
        simulation.Step();
        return simulation.Entities.OfType<Humanoid>().Single();
    }

    /// <summary>A square hospital of <paramref name="row"/>'s width, all of it the player's.</summary>
    private static Simulation NewSimulation(string row)
    {
        var map = TestMaps.FromRows(Enumerable.Repeat(row, row.Length).ToArray());
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Parcel = 1;
                map[x, y].Set(TileFlags.Buildable | TileFlags.Hospital, true);
            }
        }

        return new Simulation(map, seed: 1, new GameContent([], [], []), new LevelRules(1_000_000, [], []));
    }
}
