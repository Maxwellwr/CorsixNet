using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;
using static CorsixNet.Content.Tests.PatientFlowTests;

namespace CorsixNet.Content.Tests;

/// <summary>Machines wearing out, exploding and being repaired (CorsixTH's entities/machine.lua, room.lua's crashRoom).</summary>
public class MachineWearTests
{
    [Fact]
    public void AMachineStartsAsStrongAsItsResearch()
    {
        var simulation = NewStaffedHospital(InflationTests.Rules());
        InflationTests.BuildInflation(simulation);

        var inflator = Inflator(simulation);
        Assert.NotNull(inflator.Machine);
        Assert.Equal(10, inflator.Machine.Strength);
        Assert.Equal(0, inflator.Machine.TimesUsed);
        Assert.Null(simulation.Entities.OfType<RoomObject>().First(o => o.ObjectType == "desk").Machine);
    }

    [Fact]
    public void AWreckedRoomCanOnlyBeClearedAwayForAFee()
    {
        var simulation = NewStaffedHospital(InflationTests.Rules());
        var room = InflationTests.BuildInflation(simulation);
        var hospital = simulation.Hospitals[0];
        for (var tick = 0; tick < 12 * 31 * GameDate.HoursPerDay && !room.IsCrashed; tick++)
        {
            simulation.Step();
        }

        Assert.True(room.IsCrashed);
        simulation.Step();

        // Taking it down as an unfinished room does nothing.
        Run(simulation, new CancelRoomCommand(room.Id));
        Assert.Same(room, simulation.FindEntity(room.Id));

        // Twice the room's cost less 40 % of the inflator as scrap.
        var definition = simulation.Content.FindRoom("inflation")!;
        var expected = (RoomPurchases.BuildCost(simulation, hospital, definition) * 2) - (int)Math.Floor(hospital.Research.ObjectCost(simulation.Rules, "inflator") * 0.4m);
        Assert.Equal(expected, RemoveDestroyedRoomCommand.RemovalCost(simulation, room, 0));
        var balance = hospital.Balance;
        Run(simulation, new RemoveDestroyedRoomCommand(room.Id));

        Assert.Null(simulation.FindEntity(room.Id));
        Assert.Contains(hospital.Transactions, t => t.Kind == TransactionKind.RemoveRoom && t.Amount == -expected);
        Assert.True(hospital.Balance <= balance - expected);
        Assert.Equal(0, simulation.Map[room.Area.X + 1, room.Area.Y + 1].RoomId);
    }

    [Fact]
    public void WithTheInvulnerableMachinesCheatOnlyTheTotalUsageCounts()
    {
        var simulation = NewStaffedHospital(InflationTests.Rules());
        var room = InflationTests.BuildInflation(simulation);
        var inflator = Inflator(simulation);
        Run(simulation, new CheatCommand(CheatKind.ToggleInvulnerableMachines));
        Assert.True(simulation.Hospitals[0].ActiveCheats.HasFlag(ToggleCheats.InvulnerableMachines));
        Assert.True(simulation.Hospitals[0].Cheated);

        for (var hour = 0; hour < 6 * 31 * GameDate.HoursPerDay && inflator.Machine!.TotalUsage <= inflator.Machine.Strength; hour++)
        {
            simulation.Step();
        }

        Assert.False(room.IsCrashed);
        Assert.True(inflator.Machine!.TotalUsage > inflator.Machine.Strength, $"Only {inflator.Machine.TotalUsage} uses.");
        Assert.Equal(0, inflator.Machine.TimesUsed);

        // Off again, the machine wears as before.
        Run(simulation, new CheatCommand(CheatKind.ToggleInvulnerableMachines));
        for (var hour = 0; hour < 3 * 31 * GameDate.HoursPerDay && inflator.Machine.TimesUsed == 0; hour++)
        {
            simulation.Step();
        }

        Assert.True(inflator.Machine.TimesUsed > 0);
    }

    [Fact]
    public void TheRepairAllMachinesCheatMakesThemAsGoodAsNew()
    {
        var simulation = NewStaffedHospital(InflationTests.Rules());
        InflationTests.BuildInflation(simulation);
        var inflator = Inflator(simulation);
        for (var tick = 0; tick < 6 * 31 * GameDate.HoursPerDay && inflator.Machine!.TimesUsed < 3; tick++)
        {
            simulation.Step();
        }

        Assert.True(inflator.Machine!.TimesUsed >= 3);
        var strength = inflator.Machine.Strength;
        Run(simulation, new CheatCommand(CheatKind.RepairAllMachines));

        Assert.Equal(0, inflator.Machine.TimesUsed);
        Assert.Equal(strength, inflator.Machine.Strength);
    }

    [Fact]
    public void AWornOutMachineWithoutAFireExtinguisherExplodesAndWrecksTheRoom()
    {
        var simulation = NewStaffedHospital(InflationTests.Rules());
        var room = InflationTests.BuildInflation(simulation);
        var hospital = simulation.Hospitals[0];
        var inflator = Inflator(simulation);
        var valueBefore = 0L;
        var deathsBefore = 0;
        var smoked = false;

        for (var tick = 0; tick < 12 * 31 * GameDate.HoursPerDay && !room.IsCrashed; tick++)
        {
            valueBefore = hospital.Value;
            deathsBefore = hospital.Deaths;
            simulation.Step();
            smoked |= inflator.Machine!.IsBreaking;
        }

        Assert.True(room.IsCrashed, $"The inflator was used {inflator.Machine!.TotalUsage} times and did not explode.");
        Assert.True(smoked);
        Assert.False(room.IsActive);
        Assert.Equal(inflator.Machine!.Strength, inflator.Machine.TimesUsed);

        // Room:crashRoom: the patient and the doctor are dead, soot covers the floor, the hospital loses the room's value.
        simulation.Step();
        Assert.DoesNotContain(simulation.Entities.OfType<Humanoid>(), h => room.Area.Contains(h.Tile));
        Assert.Equal(deathsBefore + 1, hospital.Deaths);
        Assert.Equal(room.Area.Width * room.Area.Height, simulation.Entities.OfType<Litter>().Count(l => l.Type == LitterType.SootFloor && room.Area.Contains(l.Tile)));

        // The soot crashRoom puts on the walls ranks no higher than the floor's on the same tiles, so it makes way
        // (Litter.resolveTileRank).
        Assert.DoesNotContain(simulation.Entities.OfType<Litter>(), l => l.Type is LitterType.SootWall or LitterType.SootWindow);
        Assert.True(hospital.Value < valueBefore);
        Assert.Equal(1, hospital.Explosions);
        Assert.Contains(simulation.Entities.OfType<RoomObject>(), o => o.Id == inflator.Id);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        Assert.True(loaded.Entities.OfType<Room>().Single(r => r.Id == room.Id).IsCrashed);
        Assert.Equal(1, loaded.Hospitals[0].Explosions);

        // The wreck cannot be furnished or opened again, only taken down.
        Assert.False(RoomBuilder.CanConfirm(simulation, room, 0));

        // Nobody goes into the wreck again.
        StepFor(simulation, 10 * GameDate.HoursPerDay);
        Assert.DoesNotContain(simulation.Entities.OfType<Humanoid>(), h => room.Area.Contains(h.Tile));
    }

    [Fact]
    public void AHandymanRepairsAWornMachineBeforeItExplodes()
    {
        var simulation = NewStaffedHospital(InflationTests.Rules());
        var room = InflationTests.BuildInflation(simulation);
        Run(simulation, new HireStaffCommand(StaffClass.Handyman, 0, new(14, 15)));
        var inflator = Inflator(simulation);
        var repairs = 0;
        var wasRepairing = false;
        var repairAnimations = new HashSet<int>();

        for (var tick = 0; tick < 6 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            var repairing = false;
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is HandymanBehaviour { Phase: HandymanPhase.Repairing } handyman && handyman.TargetId == inflator.Id)
                {
                    repairing = true;
                    if (humanoid.ActionAnimation is { } animation)
                    {
                        repairAnimations.Add(animation.Id);
                    }
                }
            }

            if (wasRepairing && !repairing)
            {
                repairs++;
                Assert.Equal(0, inflator.Machine!.TimesUsed);
            }

            wasRepairing = repairing;
        }

        Assert.False(room.IsCrashed);
        Assert.True(repairs > 0, $"The inflator was used {inflator.Machine!.TotalUsage} times and never repaired.");
        Assert.True(inflator.Machine!.TotalUsage > inflator.Machine.Strength);
        Assert.Equal([3480], repairAnimations);
    }

    [Fact]
    public void ReplacingAMachineBuysANewOneAtTodaysPrice()
    {
        var simulation = NewStaffedHospital(InflationTests.Rules());
        InflationTests.BuildInflation(simulation);
        var hospital = simulation.Hospitals[0];
        var inflator = Inflator(simulation);
        for (var tick = 0; tick < 3 * 31 * GameDate.HoursPerDay && inflator.Machine!.TimesUsed < 2; tick++)
        {
            simulation.Step();
        }

        Assert.True(inflator.Machine!.TimesUsed >= 2);
        var balance = hospital.Balance;
        Run(simulation, new ReplaceMachineCommand(inflator.Id));

        var cost = hospital.Research.ObjectCost(simulation.Rules, "inflator");
        if (cost > 0)
        {
            Assert.Equal(TransactionKind.MachineReplacement, hospital.Transactions[^1].Kind);
            Assert.Equal(-cost, hospital.Transactions[^1].Amount);
        }

        Assert.Equal(0, inflator.Machine.TimesUsed);
        Assert.Equal(0, inflator.Machine.TotalUsage);
        Assert.Equal(10, inflator.Machine.Strength);
        Assert.Equal(balance - cost, hospital.Balance);

        // Another player cannot replace it.
        var count = hospital.Transactions.Count;
        simulation.Schedule(1, new ReplaceMachineCommand(inflator.Id));
        simulation.Step();
        Assert.DoesNotContain(hospital.Transactions.Skip(count), t => t.Kind == TransactionKind.MachineReplacement);
    }

    [Fact]
    public void CallingAHandymanRepairsTheMachineAtOnceAndKeepsPatientsOut()
    {
        var simulation = NewStaffedHospital(InflationTests.Rules());
        var room = InflationTests.BuildInflation(simulation);
        var inflator = Inflator(simulation);
        for (var tick = 0; tick < 3 * 31 * GameDate.HoursPerDay && inflator.Machine!.TimesUsed < 1; tick++)
        {
            simulation.Step();
        }

        Assert.Equal(1, inflator.Machine!.TimesUsed);
        Run(simulation, new CallHandymanCommand(inflator.Id));
        Assert.True(inflator.Machine.IsRepairCalled);

        using (var stream = new MemoryStream())
        {
            simulation.Save(stream);
            stream.Position = 0;
            var loaded = Simulation.Load(stream, BaseGameContent.Create());
            Assert.True(loaded.Entities.OfType<RoomObject>().Single(o => o.ObjectType == "inflator").Machine!.IsRepairCalled);
        }

        // Nobody new is let in while the call stands; a handyman hired now comes and repairs it.
        Run(simulation, new HireStaffCommand(StaffClass.Handyman, 0, new(14, 15)));
        var patientsLetIn = new HashSet<int>();
        var before = room.PatientId;
        for (var tick = 0; tick < 31 * GameDate.HoursPerDay && inflator.Machine.IsRepairCalled; tick++)
        {
            simulation.Step();
            if (room.PatientId != 0 && room.PatientId != before)
            {
                patientsLetIn.Add(room.PatientId);
            }
        }

        Assert.False(inflator.Machine.IsRepairCalled);
        Assert.Equal(0, inflator.Machine.TimesUsed);
        Assert.Empty(patientsLetIn);

        foreach (var command in new Command[] { new CallHandymanCommand(5), new ReplaceMachineCommand(6) })
        {
            using var wire = new MemoryStream();
            command.Write(new CorsixNet.Sim.Serialization.SimWriter(wire));
            wire.Position = 0;
            Assert.Equal(command, Command.Read(new CorsixNet.Sim.Serialization.SimReader(wire)));
        }
    }

    [Fact]
    public void EachRoundOfAnEarthquakeWearsTheMachinesWithoutCountingAsAUse()
    {
        var simulation = NewStaffedHospital(InflationTests.Rules());
        var room = InflationTests.BuildInflation(simulation);
        var inflator = Inflator(simulation);
        Run(simulation, new CheatCommand(CheatKind.Earthquake));
        var size = simulation.Earthquakes.Size;
        var rounds = 0;
        var started = false;

        for (var tick = 0; tick < 2 * 31 * GameDate.HoursPerDay && !room.IsCrashed; tick++)
        {
            var timesUsed = inflator.Machine!.TimesUsed;
            var totalUsage = inflator.Machine.TotalUsage;
            var remaining = simulation.Earthquakes.RemainingDamage;
            simulation.Step();
            started |= simulation.Earthquakes.IsActive;
            if (simulation.Earthquakes.RemainingDamage < remaining)
            {
                Assert.Equal(totalUsage, inflator.Machine.TotalUsage);
                Assert.True(inflator.Machine.TimesUsed == timesUsed + 1 || room.IsCrashed);
                rounds++;
            }

            if (started && !simulation.Earthquakes.IsActive)
            {
                break;
            }
        }

        Assert.True(rounds == size || room.IsCrashed, $"{rounds} rounds of {size}.");
        Assert.True(rounds > 0);
    }

    private static RoomObject Inflator(Simulation simulation) => simulation.Entities.OfType<RoomObject>().Single(o => o.ObjectType == "inflator");
}
