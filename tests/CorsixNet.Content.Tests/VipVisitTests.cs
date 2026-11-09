using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;
using static CorsixNet.Content.Tests.PatientFlowTests;

namespace CorsixNet.Content.Tests;

/// <summary>VIP visits (CorsixTH's vip.lua and the VIP faxes), in a hospital with a GP's office and reception.</summary>
public class VipVisitTests
{
    [Fact]
    public void TheFaxInvitesTheVipByItselfAfterTwentyDays()
    {
        var simulation = NewStaffedHospital();
        var vips = simulation.Hospitals[0].Vips;

        Run(simulation, new CheatCommand(CheatKind.Vip));
        Assert.True(vips.IsInvitationPending);
        Assert.InRange(vips.InvitedName, 1, VipVisits.NameCount);
        Assert.True(simulation.Hospitals[0].Cheated);

        StepFor(simulation, (VipVisits.InvitationDays * GameDate.HoursPerDay) - 2);
        Assert.True(vips.IsInvitationPending);
        Assert.DoesNotContain(simulation.Entities, entity => entity is Humanoid { Behaviour: VipBehaviour });

        StepFor(simulation, 2);
        Assert.False(vips.IsInvitationPending);
        Assert.Equal(1, vips.Count);
        var vip = Assert.Single(simulation.Entities.OfType<Humanoid>(), humanoid => humanoid.Behaviour is VipBehaviour);
        Assert.Equal(HumanoidType.Vip, vip.Type);
        Assert.True(vip.IsSlow);
    }

    [Fact]
    public void ARefusedVipComesAnotherDay()
    {
        var simulation = NewStaffedHospital();
        var vips = simulation.Hospitals[0].Vips;
        Run(simulation, new CheatCommand(CheatKind.Vip));

        Run(simulation, new AnswerVipCommand(Accept: false));

        Assert.False(vips.IsInvitationPending);
        Assert.Equal(1, vips.Declined);
        Assert.Equal(0, vips.Count);
        Assert.InRange(vips.NextVipDay - (simulation.Tick / GameDate.HoursPerDay), 120, 180);
        Assert.DoesNotContain(simulation.Entities, entity => entity is Humanoid { Behaviour: VipBehaviour });

        // Answering again does nothing: nobody asks.
        Run(simulation, new AnswerVipCommand(Accept: true));
        Assert.Equal(0, vips.Count);
    }

    [Fact]
    public void AnInvitedVipReportsAtReceptionLooksInAtTheRoomsAndLeavesARating()
    {
        var simulation = NewStaffedHospital();
        var hospital = simulation.Hospitals[0];
        Run(simulation, new CheatCommand(CheatKind.Vip));
        var dayBefore = simulation.Tick / GameDate.HoursPerDay;
        Run(simulation, new AnswerVipCommand(Accept: true));
        var vip = simulation.Entities.OfType<Humanoid>().Single(humanoid => humanoid.Behaviour is VipBehaviour);
        var behaviour = (VipBehaviour)vip.Behaviour!;
        Assert.InRange(behaviour.Rating, 7, 12);

        var phases = new HashSet<VipPhase>();
        var wasInQueue = false;
        for (var tick = 0; tick < 30 * GameDate.HoursPerDay && simulation.FindEntity(vip.Id) is not null; tick++)
        {
            simulation.Step();
            phases.Add(behaviour.Phase);
            if (Desk(simulation).Desk!.Queue.Contains(vip.Id))
            {
                wasInQueue = true;
                Assert.Equal(vip.Id, Desk(simulation).Desk!.Queue.Front);
            }
        }

        Assert.Null(simulation.FindEntity(vip.Id));
        Assert.True(wasInQueue);
        Assert.Contains(VipPhase.Waiting, phases);
        Assert.Contains(VipPhase.LookingAtRoom, phases);
        Assert.Contains(VipPhase.GoingHome, phases);
        Assert.Equal(1, behaviour.VisitedRooms);
        Assert.InRange(behaviour.Rating, 1, 15);
        Assert.Equal(1, hospital.Vips.ThisYear);
        Assert.Equal(behaviour.Rating <= VipBehaviour.PleasedRating ? 1 : 0, hospital.Vips.PleasedThisYear);
        Assert.True(hospital.Vips.NextVipDay > dayBefore);
    }

    [Fact]
    public void AVipVisitSurvivesASave()
    {
        var simulation = NewStaffedHospital();
        Run(simulation, new CheatCommand(CheatKind.Vip));
        Run(simulation, new AnswerVipCommand(Accept: true));
        StepFor(simulation, 3 * GameDate.HoursPerDay);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, BaseGameContent.Create());

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        for (var tick = 0; tick < 20 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            restored.Step();
        }

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
    }
}
