using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Sim.Tests;

/// <summary>How fast staff walk (CorsixTH's Staff:updateSpeed and walk.lua's fast steps).</summary>
public class StaffSpeedTests
{
    [Fact]
    public void TiredStaffSlowDownButReceptionistsDoNot()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor, profile => !profile.IsJunior && !profile.IsConsultant);
        var receptionist = Hire(simulation, StaffClass.Receptionist, profile => !profile.IsJunior && !profile.IsConsultant);
        Assert.False(doctor.IsSlow || doctor.IsFast);

        // Very tired (700): one step slower, which is slow.
        doctor.Fatigue = 0.75m;
        receptionist.Fatigue = 0.9m;
        Tiredness.UpdateSpeed(simulation, doctor);
        Tiredness.UpdateSpeed(simulation, receptionist);
        Assert.True(doctor.IsSlow);
        Assert.False(receptionist.IsSlow || receptionist.IsFast);

        // The no-rest cheat makes everyone fast.
        simulation.Schedule(0, new CheatCommand(CheatKind.ToggleNoRest));
        simulation.Step();
        Assert.True(doctor.IsFast);
        Assert.False(doctor.IsSlow);
        Assert.True(receptionist.IsFast);
    }

    [Fact]
    public void JuniorsAreSlowAndConsultantsFast()
    {
        for (var seed = 1UL; seed < 50; seed++)
        {
            var simulation = NewSimulation(seed);
            var candidates = simulation.AvailableStaff(StaffClass.Doctor);
            if (candidates.Any(profile => profile.IsJunior) && candidates.Any(profile => profile.IsConsultant))
            {
                Assert.True(Hire(simulation, StaffClass.Doctor, profile => profile.IsJunior).IsSlow);
                Assert.True(Hire(simulation, StaffClass.Doctor, profile => profile.IsConsultant).IsFast);
                return;
            }
        }

        Assert.Fail("No seed offered both a junior and a consultant.");
    }

    [Fact]
    public void AFastStepTakesHalfTheTicks()
    {
        // Both walks start with a tick to set off.
        Assert.Equal(1 + (4 * Humanoid.TicksPerStep), TicksToWalk(fast: false));
        Assert.Equal(1 + (4 * Humanoid.TicksPerStep / 2), TicksToWalk(fast: true));
    }

    private static int TicksToWalk(bool fast)
    {
        var simulation = NewSimulation();
        var walker = simulation.AddEntity(id => new Humanoid(id, new TilePosition(0, 0), HumanoidType.Doctor) { IsFast = fast });
        walker.WalkTo(simulation, new TilePosition(4, 0));

        var ticks = 0;
        while (walker.IsMoving && ticks < 100)
        {
            simulation.Step();
            ticks++;
        }

        Assert.Equal(new TilePosition(4, 0), walker.Tile);
        return ticks;
    }

    private static Humanoid Hire(Simulation simulation, StaffClass staffClass, Func<StaffProfile, bool> which)
    {
        var index = simulation.AvailableStaff(staffClass).ToList().FindIndex(profile => which(profile));
        Assert.True(index >= 0, $"No {staffClass} candidate of that kind.");
        simulation.Schedule(0, new HireStaffCommand(staffClass, index, new TilePosition(7, 7)));
        simulation.Step();
        var hired = simulation.Entities.OfType<Humanoid>().Last();
        hired.PlaceAt(new TilePosition(7, 7));
        hired.Fatigue = 0m;
        return hired;
    }

    private static Simulation NewSimulation(ulong seed = 1)
    {
        var map = TestMaps.FromRows("........", "........", "........", "........", "........", "........", "........", "........");
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

        return new Simulation(map, seed, new GameContent([], [], []), new LevelRules(1_000_000, [], []));
    }
}
