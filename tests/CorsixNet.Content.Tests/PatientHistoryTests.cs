using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;
using static CorsixNet.Content.Tests.PatientFlowTests;

namespace CorsixNet.Content.Tests;

/// <summary>What the patient window shows of a patient's past (CorsixTH's health_history and treatment_history).</summary>
public class PatientHistoryTests
{
    [Fact]
    public void ThePatientWindowsHistoriesFollowTheVisit()
    {
        var simulation = NewStaffedHospital();
        BuildPharmacy(simulation);
        var histories = new Dictionary<int, (IReadOnlyList<TreatmentHistoryEntry> Treatment, IReadOnlyList<decimal> Health)>();
        for (var tick = 0; tick < 3 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is PatientBehaviour patient)
                {
                    histories[humanoid.Id] = ([.. patient.TreatmentHistory], [.. patient.HealthHistory]);
                }
            }
        }

        // The GP comes first; a diagnosis follows a room; a cure follows the pharmacy and ends the history.
        var seen = histories.Values.Where(h => h.Treatment.Count > 0).ToList();
        Assert.NotEmpty(seen);
        Assert.All(seen, h => Assert.Equal(new TreatmentHistoryEntry(TreatmentHistoryKind.Room, "gp"), h.Treatment[0]));
        Assert.All(seen, h =>
        {
            for (var i = 0; i < h.Treatment.Count; i++)
            {
                if (h.Treatment[i].Kind == TreatmentHistoryKind.Diagnosis)
                {
                    Assert.Equal(TreatmentHistoryKind.Room, h.Treatment[i - 1].Kind);
                }

                if (h.Treatment[i].Kind == TreatmentHistoryKind.Cured)
                {
                    Assert.Equal(new TreatmentHistoryEntry(TreatmentHistoryKind.Room, "pharmacy"), h.Treatment[i - 1]);
                    Assert.Equal(h.Treatment.Count - 1, i);
                }
            }
        });
        Assert.Contains(seen, h => h.Treatment.Any(e => e.Kind == TreatmentHistoryKind.Cured));

        // A day's health each day in the hospital, at most twenty, falling 0.004 a day until a cure makes it 1.
        Assert.All(histories.Values, h => Assert.InRange(h.Health.Count, 0, PatientBehaviour.HealthHistorySize));
        Assert.Contains(histories.Values, h => h.Health.Count == PatientBehaviour.HealthHistorySize);
        Assert.All(histories.Values.Where(h => h.Health.Count > 1 && !h.Treatment.Any(e => e.Kind == TreatmentHistoryKind.Cured)), h =>
        {
            for (var i = 1; i < h.Health.Count; i++)
            {
                Assert.True(h.Health[i] <= h.Health[i - 1]);
            }
        });
    }

    [Fact]
    public void TheHistoriesAreSaved()
    {
        var simulation = NewStaffedHospital();
        BuildPharmacy(simulation);
        StepFor(simulation, 40 * GameDate.HoursPerDay);
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var copy = Simulation.Load(stream, BaseGameContent.Create());

        var before = simulation.Entities.OfType<Humanoid>().Where(h => h.Behaviour is PatientBehaviour).ToList();
        Assert.Contains(before, h => ((PatientBehaviour)h.Behaviour!).TreatmentHistory.Count > 0);
        foreach (var humanoid in before)
        {
            var original = (PatientBehaviour)humanoid.Behaviour!;
            var loaded = (PatientBehaviour)((Humanoid)copy.FindEntity(humanoid.Id)!).Behaviour!;
            Assert.Equal(original.TreatmentHistory, loaded.TreatmentHistory);
            Assert.Equal(original.HealthHistory, loaded.HealthHistory);
            Assert.Equal(original.IsSad, loaded.IsSad);
        }
    }
}
