using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Sim.Tests;

/// <summary>The network game's poaching of another hospital's staff (the original's NETPLAY.TXT, "Poaching Staff").</summary>
public class PoachingTests
{
    [Fact]
    public void AHappyMemberOfStaffSaysNoAndAnUnhappyOneDemandsDoubleTheirWage()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation);
        doctor.Happiness = 0.9m;

        Run(simulation, new PoachStaffCommand(doctor.Id), player: 1);
        Assert.Equal((doctor.Id, false), (simulation.Hospitals[1].LastPoach!.StaffId, simulation.Hospitals[1].LastPoach!.Listened));
        Assert.Equal(-1, doctor.PoachedBy);

        // Their own hospital cannot poach them.
        doctor.Happiness = 0.2m;
        Run(simulation, new PoachStaffCommand(doctor.Id), player: 0);
        Assert.Equal(0, doctor.RaiseRequest);

        Run(simulation, new PoachStaffCommand(doctor.Id), player: 1);
        Assert.True(simulation.Hospitals[1].LastPoach!.Listened);
        Assert.Equal(1, doctor.PoachedBy);
        Assert.Equal(doctor.Profile!.Wage, doctor.RaiseRequest);
    }

    [Fact]
    public void MetTheyStayRefusedTheyJoinThePoacher()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation);
        doctor.Happiness = 0.2m;
        var wage = doctor.Profile!.Wage;
        Run(simulation, new PoachStaffCommand(doctor.Id), player: 1);

        Run(simulation, new AnswerRaiseCommand(doctor.Id, Grant: true), player: 0);
        Assert.Equal((0, -1, 2 * wage), (doctor.Employer, doctor.PoachedBy, doctor.Profile.Wage));

        var nurse = Hire(simulation, StaffClass.Nurse);
        nurse.Happiness = 0.2m;
        Run(simulation, new PoachStaffCommand(nurse.Id), player: 1);
        Run(simulation, new AnswerRaiseCommand(nurse.Id, Grant: false), player: 0);

        Assert.Equal((1, 1, false, false), (nurse.Employer, nurse.HospitalPlayer, nurse.IsLeavingForPoacher, nurse.IsFired));
        Assert.Equal(2, simulation.Map.GetParcelOwner(simulation.Map[nurse.Tile.X, nurse.Tile.Y].Parcel));
    }

    [Fact]
    public void APoachedMemberOfStaffIsSaved()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation);
        doctor.Happiness = 0.2m;
        Run(simulation, new PoachStaffCommand(doctor.Id), player: 1);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(1, ((Humanoid)restored.FindEntity(doctor.Id)!).PoachedBy);
        Assert.True(restored.Hospitals[1].LastPoach!.Listened);
    }

    private static Humanoid Hire(Simulation simulation, StaffClass staffClass = StaffClass.Doctor)
    {
        Run(simulation, new HireStaffCommand(staffClass, 0, new TilePosition(4, 4)), player: 0);
        return simulation.Entities.OfType<Humanoid>().Last();
    }

    private static void Run(Simulation simulation, Command command, int player)
    {
        simulation.Schedule(player, command);
        simulation.Step();
    }

    /// <summary>Two hospitals: player 1's parcel in the middle, player 2's along the right edge.</summary>
    private static Simulation NewSimulation()
    {
        var map = new LevelMap(12, 10, playerCount: 2);
        map.ParcelCount = 3;
        map.SetParcelOwner(1, 1);
        map.SetParcelOwner(2, 2);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                ref var tile = ref map[x, y];
                tile.Floor = 1;
                tile.Set(TileFlags.Passable, true);
                if (x is > 1 and < 8 && y is > 1 and < 8)
                {
                    tile.Parcel = 1;
                    tile.Set(TileFlags.Buildable | TileFlags.Hospital, true);
                }
                else if (x >= 10)
                {
                    tile.Parcel = 2;
                    tile.Set(TileFlags.Buildable | TileFlags.Hospital, true);
                }
            }
        }

        return new Simulation(map, seed: 1, rules: new LevelRules(10_000, [], []));
    }
}
