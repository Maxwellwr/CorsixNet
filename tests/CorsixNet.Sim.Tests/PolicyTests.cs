using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Sim.Tests;

/// <summary>A hospital's policies (CorsixTH's hospital.policies and its policy window).</summary>
public class PolicyTests
{
    [Fact]
    public void PoliciesStartAtCorsixThsDefaultsAndTheLevelsTiredness()
    {
        var simulation = NewSimulation(new TirednessRules(450, 700, 800));
        var policies = simulation.Hospitals[0].Policies;

        Assert.Equal(100, policies.SendHome);
        Assert.Equal(900, policies.GuessCure);
        Assert.Equal(1000, policies.StopProcedure);
        Assert.Equal(450, policies.GoToStaffRoom);
        Assert.True(policies.StaffAllowedToMove);
        Assert.False(policies.GrantWageIncrease);
    }

    [Fact]
    public void SlidersStayInTheirRanges()
    {
        var simulation = NewSimulation();
        var policies = simulation.Hospitals[0].Policies;

        // Sending home and guessing cannot pass each other.
        Run(simulation, new SetPolicyCommand(PolicyKind.SendHome, 950));
        Assert.Equal(900, policies.SendHome);
        Run(simulation, new SetPolicyCommand(PolicyKind.GuessCure, 500));
        Assert.Equal(900, policies.GuessCure);
        Run(simulation, new SetPolicyCommand(PolicyKind.SendHome, 200));
        Run(simulation, new SetPolicyCommand(PolicyKind.GuessCure, 500));
        Assert.Equal(500, policies.GuessCure);

        // Stopping is from 1.0 to 2.0.
        Run(simulation, new SetPolicyCommand(PolicyKind.StopProcedure, 500));
        Assert.Equal(1000, policies.StopProcedure);
        Run(simulation, new SetPolicyCommand(PolicyKind.StopProcedure, 2500));
        Assert.Equal(2000, policies.StopProcedure);

        Run(simulation, new SetPolicyCommand(PolicyKind.GoToStaffRoom, -5));
        Assert.Equal(0, policies.GoToStaffRoom);
        Run(simulation, new SetPolicyCommand(PolicyKind.StaffAllowedToMove, 0));
        Assert.False(policies.StaffAllowedToMove);
        Run(simulation, new SetPolicyCommand(PolicyKind.GrantWageIncrease, 1));
        Assert.True(policies.GrantWageIncrease);
    }

    [Fact]
    public void TheStaffRoomPolicySetsWhenStaffWantABreak()
    {
        var simulation = NewSimulation();
        simulation.Schedule(0, new HireStaffCommand(StaffClass.Doctor, 0, new TilePosition(2, 2)));
        simulation.Step();
        var doctor = simulation.Entities.OfType<Entities.Humanoid>().Single();
        doctor.Fatigue = 0.3m;
        doctor.Happiness = 0.5m;

        Tiredness.OnEndDay(simulation, doctor);
        Assert.Equal(0.5m, doctor.Happiness);

        Run(simulation, new SetPolicyCommand(PolicyKind.GoToStaffRoom, 250));
        Tiredness.OnEndDay(simulation, doctor);
        Assert.Equal(0.48m, doctor.Happiness);
    }

    [Fact]
    public void TheCommandAndTheSavesKeepThePolicies()
    {
        var command = new SetPolicyCommand(PolicyKind.StopProcedure, 1500);
        using (var stream = new MemoryStream())
        {
            command.Write(new SimWriter(stream));
            stream.Position = 0;
            Assert.Equal(command, Command.Read(new SimReader(stream)));
        }

        var simulation = NewSimulation();
        Run(simulation, command);
        Run(simulation, new SetPolicyCommand(PolicyKind.SendHome, 300));
        using var save = new MemoryStream();
        simulation.Save(save);
        save.Position = 0;
        var restored = Simulation.Load(save);

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(1500, restored.Hospitals[0].Policies.StopProcedure);
        Assert.Equal(300, restored.Hospitals[0].Policies.SendHome);
    }

    private static void Run(Simulation simulation, Command command)
    {
        simulation.Schedule(0, command);
        simulation.Step();
    }

    private static Simulation NewSimulation(TirednessRules? tiredness = null)
    {
        var map = TestMaps.FromRows("........", "........", "........", "........");
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

        return new Simulation(map, seed: 1, rules: new LevelRules(10_000, [], [], tiredness: tiredness));
    }
}
