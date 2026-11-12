using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;
using static CorsixNet.Content.Tests.PatientFlowTests;

namespace CorsixNet.Content.Tests;

/// <summary>Patients knocking on a room's door before going in (CorsixTH's <c>KnockDoorAction</c>, walk.lua).</summary>
public class DoorKnockTests
{
    [Fact]
    public void APatientKnocksOnTheGpsDoorWhenNobodyQueuesAndNobodyIsInside()
    {
        var simulation = NewStaffedHospital();
        var gp = simulation.Entities.OfType<Room>().Single();
        var knocks = new HashSet<int>();
        var animations = new HashSet<int>();
        var knocking = new HashSet<int>();

        for (var tick = 0; tick < 2 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is not PatientBehaviour patient)
                {
                    continue;
                }

                if (patient.SpotAction != PatientSpotAction.Knock)
                {
                    knocking.Remove(humanoid.Id);
                    continue;
                }

                if (knocking.Add(humanoid.Id))
                {
                    // walk.lua knocks only when nobody queues and no patient is inside.
                    Assert.Equal(0, gp.DoorQueue!.Count);
                    Assert.Equal(0, gp.PatientId);
                    knocks.Add(humanoid.Id);
                }

                if (humanoid.ActionAnimation is { } animation)
                {
                    animations.Add(animation.Id);

                    // Nobody else goes through the door meanwhile (door:setUser).
                    Assert.DoesNotContain(simulation.Entities.OfType<Humanoid>(), other => other.IsCrossingDoor && other.HiddenObjectId == gp.DoorId);
                }
            }
        }

        Assert.NotEmpty(knocks);

        // The GP's door is on the room's south wall: patients go in northwards and knock with knock_north.
        var expected = BaseGameContent.DoorAnimations.Values.Select(d => d.KnockNorth?.Id).OfType<int>().ToHashSet();
        Assert.NotEmpty(animations);
        Assert.Subset(expected, animations);
        Assert.True(gp.DoorQueue!.VisitorCount >= knocks.Count);
    }

    [Fact]
    public void ASavedGameWithAPatientKnockingContinuesIdentically()
    {
        var simulation = NewStaffedHospital();
        for (var tick = 0; tick < 2 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            if (simulation.Entities.OfType<Humanoid>().Any(h => h.Behaviour is PatientBehaviour { SpotAction: PatientSpotAction.Knock } && h.ActionAnimation is not null))
            {
                break;
            }
        }

        Assert.Contains(simulation.Entities.OfType<Humanoid>(), h => h.Behaviour is PatientBehaviour { SpotAction: PatientSpotAction.Knock });
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, BaseGameContent.Create());

        StepFor(simulation, 200);
        StepFor(restored, 200);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
    }
}
