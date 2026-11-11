using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;
using static CorsixNet.Content.Tests.PatientFlowTests;

namespace CorsixNet.Content.Tests;

/// <summary>
/// The faxes a waiting patient sends (CorsixTH's makeNoDiagnosisRoomFax, makeNoTreatmentRoomFax and UIFax:choice), in a
/// hospital where broken wind is the only disease and the GP gets three quarters of the way to a diagnosis.
/// </summary>
public class PatientFaxTests
{
    [Fact]
    public void APatientShortOfADiagnosisAsksAndAGuessedCureSendsThemOnToTreatment()
    {
        var simulation = NewStaffedHospital(Rules());
        BuildPharmacy(simulation);
        var (humanoid, patient) = WaitForQuestion(simulation);
        Assert.Equal(PatientQuestion.NoDiagnosisRoom, patient.Question);
        Assert.False(patient.IsDiagnosed);

        Run(simulation, new AnswerPatientCommand(humanoid.Id, PatientAnswer.GuessCure));

        Assert.Equal(PatientQuestion.None, patient.Question);
        Assert.True(patient.IsDiagnosed);
        Assert.Equal(RoomSeek.Treatment, patient.Seeking);
        Assert.NotEqual(PatientPhase.Waiting, patient.Phase);
    }

    [Fact]
    public void WithoutTheTreatmentRoomTheGuessedPatientAsksAgainAndCanBeSentHome()
    {
        var simulation = NewStaffedHospital(Rules());
        var (humanoid, patient) = WaitForQuestion(simulation);
        Run(simulation, new AnswerPatientCommand(humanoid.Id, PatientAnswer.GuessCure));
        Assert.Equal(PatientQuestion.NoTreatmentRoom, patient.Question);

        // Guessing only answers a diagnosis question.
        Run(simulation, new AnswerPatientCommand(humanoid.Id, PatientAnswer.GuessCure));
        Assert.Equal(PatientQuestion.NoTreatmentRoom, patient.Question);

        var reputation = simulation.Hospitals[0].Reputation;
        Run(simulation, new AnswerPatientCommand(humanoid.Id, PatientAnswer.SendHome));

        Assert.Equal(PatientPhase.GoingHome, patient.Phase);
        Assert.Equal(PatientQuestion.None, patient.Question);
        Assert.True(simulation.Hospitals[0].Reputation <= reputation);
    }

    [Fact]
    public void APatientToldToWaitStaysOnWithoutAsking()
    {
        var simulation = NewStaffedHospital(Rules());
        var (humanoid, patient) = WaitForQuestion(simulation);

        Run(simulation, new AnswerPatientCommand(humanoid.Id, PatientAnswer.Wait));

        Assert.Equal(PatientPhase.Waiting, patient.Phase);
        Assert.Equal(PatientQuestion.None, patient.Question);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());

        // Another 60 days from now, not from when they first asked.
        StepFor(simulation, (PatientCare.WaitingDays * GameDate.HoursPerDay) - 10);
        Assert.Equal(PatientPhase.Waiting, patient.Phase);
    }

    [Fact]
    public void AWaitingPatientLooksAtTheWatchYawnsAndTapsAFoot()
    {
        var simulation = NewStaffedHospital(Rules());
        var (humanoid, patient) = WaitForQuestion(simulation);
        Run(simulation, new AnswerPatientCommand(humanoid.Id, PatientAnswer.Wait));

        // Patient:_dailyWaitChecks: with 30, 20 and 10 days of waiting left.
        var seen = new List<PatientSpotAction>();
        var animations = new HashSet<int>();
        for (var tick = 0; tick < PatientCare.WaitingDays * GameDate.HoursPerDay && patient.Phase == PatientPhase.Waiting; tick++)
        {
            simulation.Step();
            if (patient.SpotAction is PatientSpotAction.CheckWatch or PatientSpotAction.Yawn or PatientSpotAction.TapFoot
                && (seen.Count == 0 || seen[^1] != patient.SpotAction))
            {
                seen.Add(patient.SpotAction);
            }

            if (patient.SpotAction is PatientSpotAction.CheckWatch or PatientSpotAction.Yawn or PatientSpotAction.TapFoot && humanoid.ActionAnimation is { } animation)
            {
                animations.Add(animation.Id);
            }
        }

        Assert.Equal([PatientSpotAction.CheckWatch, PatientSpotAction.Yawn, PatientSpotAction.TapFoot], seen);
        var expected = BaseGameContent.PatientAnimations[humanoid.Type];
        Assert.Equal(new[] { expected.CheckWatch!.Value.Id, expected.Yawn!.Value.Id, expected.TapFoot!.Value.Id }.ToHashSet(), animations);
    }

    private static (Humanoid Humanoid, PatientBehaviour Patient) WaitForQuestion(Simulation simulation)
    {
        Run(simulation, new CheatCommand(CheatKind.CreatePatient));
        var humanoid = simulation.Entities.OfType<Humanoid>().Single(h => h.Behaviour is PatientBehaviour);
        var patient = (PatientBehaviour)humanoid.Behaviour!;
        for (var tick = 0; tick < 20 * GameDate.HoursPerDay && patient.Question == PatientQuestion.None; tick++)
        {
            simulation.Step();
        }

        Assert.NotEqual(PatientQuestion.None, patient.Question);
        return (humanoid, patient);
    }

    private static LevelRules Rules()
    {
        var nonVisuals = new int[DiseaseRules.NonVisualsCount];
        nonVisuals[DiseaseCatalog.Find("broken_wind")!.NonVisualsId!.Value] = 1;
        var diseases = new DiseaseRules(
            Enumerable.Repeat(100, DiseaseRules.ExpertiseCount + 1).ToArray(),
            Enumerable.Repeat(true, DiseaseRules.ExpertiseCount + 1).ToArray(),
            Enumerable.Repeat(0, DiseaseRules.ExpertiseCount + 1).ToArray(),
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
