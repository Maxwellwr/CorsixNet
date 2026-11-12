using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Disasters;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;
using static CorsixNet.Content.Tests.PatientFlowTests;

namespace CorsixNet.Content.Tests;

/// <summary>Emergencies (CorsixTH's createEmergency, the helicopter and resolveEmergency), with a pharmacy to cure them.</summary>
public class EmergencyTests
{
    private static readonly TilePosition Heliport = new(1, 12);

    [Fact]
    public void NoEmergencyWithoutAHeliport()
    {
        var simulation = NewStaffedHospital(Rules());

        Run(simulation, new CheatCommand(CheatKind.Emergency));

        Assert.Null(simulation.Hospitals[0].Emergencies.Current);
        Assert.False(simulation.Hospitals[0].Cheated);
    }

    [Fact]
    public void AnEmergencyNotAnsweredIsRefusedAfterSixteenDays()
    {
        var simulation = NewEmergencyHospital();
        var emergencies = simulation.Hospitals[0].Emergencies;

        Run(simulation, new CheatCommand(CheatKind.Emergency));
        var offer = Assert.IsType<Emergency>(emergencies.Current);
        Assert.False(offer.IsAccepted);
        Assert.Equal("broken_wind", offer.DiseaseId);
        Assert.InRange(offer.Victims, 2, 14);

        StepFor(simulation, (HospitalEmergencies.OfferDays * GameDate.HoursPerDay) + 1);

        Assert.Null(emergencies.Current);
        Assert.Null(emergencies.HelicopterPhase);
    }

    [Fact]
    public void TheHelicopterBringsDiagnosedPatientsWhoAreCuredOrLostBeforeTheWatchRunsOut()
    {
        var simulation = NewEmergencyHospital();
        var hospital = simulation.Hospitals[0];
        Run(simulation, new CheatCommand(CheatKind.Emergency));
        var emergency = hospital.Emergencies.Current!;
        Run(simulation, new AnswerEmergencyCommand(Accept: true));
        Assert.True(emergency.IsAccepted);
        Assert.Equal(HospitalEmergencies.HelicopterStart + 1, hospital.Emergencies.HelicopterPhase);

        // On the way for 120 hours, 60 coming down, then a patient.
        StepFor(simulation, 120 + 85);
        var first = Assert.Single(simulation.Entities.OfType<Humanoid>(), humanoid => humanoid.Behaviour is PatientBehaviour { EmergencyIndex: 1 });
        Assert.Equal(Heliport with { Y = Heliport.Y - 1 }, first.Tile);
        Assert.True(((PatientBehaviour)first.Behaviour!).IsDiagnosed);

        var reputation = hospital.Reputation;
        var limit = (HospitalEmergencies.WatchSegments + 1) * (HospitalEmergencies.WatchTickRate + 1);
        for (var tick = 0; tick < limit && hospital.Emergencies.Current is not null; tick++)
        {
            simulation.Step();
        }

        Assert.Null(hospital.Emergencies.Current);
        Assert.Equal(emergency.Victims, emergency.PatientIds.Count);
        var success = emergency.Cured * 100 >= emergency.Percentage * emergency.Victims;
        if (success)
        {
            Assert.Contains(hospital.Transactions, t => t.Kind == TransactionKind.EmergencyBonus && t.Amount == emergency.Bonus * emergency.Cured);
        }

        Assert.True(emergency.Cured > 0, "Nobody was cured at the pharmacy.");
        Assert.NotEqual(reputation, hospital.Reputation);

        // The result's fax (makeEmergencyEndFax), until it expires.
        var report = Assert.IsType<EmergencyReport>(hospital.Emergencies.Report);
        Assert.Equal((emergency.Cured, emergency.Victims, emergency.Bonus * emergency.Victims, success ? emergency.Bonus * emergency.Cured : 0), (report.Rescued, report.Total, report.MaxBonus, report.Earned));
        StepFor(simulation, (int)(report.Expires - simulation.Tick) + 1);
        Assert.Null(hospital.Emergencies.Report);
    }

    [Fact]
    public void AnEmergencySurvivesASave()
    {
        var simulation = NewEmergencyHospital();
        Run(simulation, new CheatCommand(CheatKind.Emergency));
        Run(simulation, new AnswerEmergencyCommand(Accept: true));
        StepFor(simulation, 300);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, BaseGameContent.Create());

        for (var tick = 0; tick < 10 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            restored.Step();
        }

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
    }

    /// <summary>A staffed hospital with a pharmacy and a heliport west of it, where broken wind is the only disease, known.</summary>
    private static Simulation NewEmergencyHospital()
    {
        var simulation = NewStaffedHospital(Rules());
        BuildPharmacy(simulation);
        simulation.Map.SetHeliportTile(0, Heliport);
        return simulation;
    }

    private static LevelRules Rules()
    {
        var nonVisuals = new int[DiseaseRules.NonVisualsCount];
        nonVisuals[DiseaseCatalog.Find("broken_wind")!.NonVisualsId!.Value] = 1;
        var diseases = new DiseaseRules(
            Enumerable.Repeat(100, DiseaseRules.ExpertiseCount + 1).ToArray(),
            Enumerable.Repeat(true, DiseaseRules.ExpertiseCount + 1).ToArray(),
            Enumerable.Repeat(500, DiseaseRules.ExpertiseCount + 1).ToArray(),
            new int[DiseaseRules.VisualsCount],
            nonVisuals,
            new int[DiseaseRules.VisualsCount],
            new int[DiseaseRules.NonVisualsCount],
            0,
            0);
        return new LevelRules(
            1_000_000,
            [KeyValuePair.Create("gp", 2500), KeyValuePair.Create("pharmacy", 2500)],
            [KeyValuePair.Create(ReceptionDesks.ObjectId, 600)],
            diseases: diseases);
    }
}
