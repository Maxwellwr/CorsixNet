using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Disasters;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;
using static CorsixNet.Content.Tests.PatientFlowTests;

namespace CorsixNet.Content.Tests;

/// <summary>Epidemics (CorsixTH's epidemic.lua), in a staffed hospital with a pharmacy where broken wind is the only disease.</summary>
public class EpidemicTests
{
    [Fact]
    public void TheCheatBringsAContagiousPatientWhoseEpidemicStaysHiddenUntilDiagnosed()
    {
        var simulation = NewEpidemicHospital();
        var epidemics = simulation.Hospitals[0].Epidemics;

        Run(simulation, new CheatCommand(CheatKind.Epidemic));

        Assert.True(simulation.Hospitals[0].Cheated);
        Assert.Null(epidemics.Current);
        var epidemic = Assert.Single(epidemics.Future);
        Assert.Equal("broken_wind", epidemic.DiseaseId);
        var patient = (PatientBehaviour)((Humanoid)simulation.FindEntity(Assert.Single(epidemic.InfectedIds))!).Behaviour!;
        Assert.True(patient.IsInfected);
        Assert.False(epidemic.IsReadyToReveal);
    }

    [Fact]
    public void TurningEpidemicsOffCancelsThemAndStopsTheCheat()
    {
        var simulation = NewEpidemicHospital();
        var epidemics = simulation.Hospitals[0].Epidemics;
        Run(simulation, new CheatCommand(CheatKind.Epidemic));
        var infected = (PatientBehaviour)((Humanoid)simulation.FindEntity(epidemics.Future[0].InfectedIds[0])!).Behaviour!;

        Run(simulation, new CheatCommand(CheatKind.ToggleEpidemics));

        Assert.True(epidemics.IsDisabled);
        Assert.Empty(epidemics.Future);
        Assert.True(infected.IsVaccinated);
        Run(simulation, new CheatCommand(CheatKind.Epidemic));
        Assert.Empty(epidemics.Future);
    }

    [Fact]
    public void AContagiousPatientInfectsThePatientsQueuingNextToThem()
    {
        var simulation = NewEpidemicHospital(spreadFactor: 200);
        Run(simulation, new CheatCommand(CheatKind.Epidemic));
        for (var i = 0; i < 4; i++)
        {
            Run(simulation, new CheatCommand(CheatKind.CreatePatient));
        }

        var epidemic = simulation.Hospitals[0].Epidemics.Future.Count > 0 ? simulation.Hospitals[0].Epidemics.Future[0] : null;
        for (var tick = 0; tick < 10 * GameDate.HoursPerDay && epidemic is { InfectedIds.Count: < 2 }; tick++)
        {
            simulation.Step();
            epidemic = simulation.Hospitals[0].Epidemics.Current ?? (simulation.Hospitals[0].Epidemics.Future.Count > 0 ? simulation.Hospitals[0].Epidemics.Future[0] : null);
        }

        Assert.NotNull(epidemic);
        Assert.True(epidemic.InfectedIds.Count >= 2, "Nobody caught it.");
        Assert.True(epidemic.AttemptedInfections >= epidemic.TotalInfections);
        foreach (var id in epidemic.InfectedIds)
        {
            var patient = (PatientBehaviour)((Humanoid)simulation.FindEntity(id)!).Behaviour!;
            Assert.True(patient.IsInfected || patient.IsCured);
            Assert.Equal("broken_wind", patient.DiseaseId);
        }
    }

    [Fact]
    public void ADeclaredEpidemicCostsAFineAndReputation()
    {
        var simulation = NewEpidemicHospital();
        var hospital = simulation.Hospitals[0];
        var epidemic = RevealEpidemic(simulation);
        Assert.True(epidemic.IsRevealed);
        Assert.True(hospital.Epidemics.IsFaxPending);
        Assert.Equal(2000, epidemic.DeclareFine);
        var reputation = hospital.Reputation;

        Run(simulation, new AnswerEpidemicCommand(CoverUp: false));

        Assert.Null(hospital.Epidemics.Current);
        Assert.Equal(EpidemicOutcome.Declared, hospital.Epidemics.LastOutcome);
        Assert.Contains(hospital.Transactions, t => t.Kind == TransactionKind.EpidemicFine && t.Amount == -2000);
        Assert.Equal(reputation - 20, hospital.Reputation);
        foreach (var id in epidemic.InfectedIds)
        {
            if (simulation.FindEntity(id) is Humanoid { Behaviour: PatientBehaviour patient })
            {
                Assert.True(patient.IsVaccinated);
            }
        }
    }

    [Fact]
    public void AnUnansweredFaxCoversTheEpidemicUp()
    {
        var simulation = NewEpidemicHospital();
        var epidemic = RevealEpidemic(simulation);

        StepFor(simulation, Epidemics.FaxHours);

        Assert.True(epidemic.IsCoverUp);
        Assert.False(simulation.Hospitals[0].Epidemics.IsFaxPending);
    }

    [Fact]
    public void AfterACoverUpTheInspectorReportsAtReceptionAndSettlesTheEpidemic()
    {
        var simulation = NewEpidemicHospital();
        var hospital = simulation.Hospitals[0];
        var epidemic = RevealEpidemic(simulation);
        Run(simulation, new AnswerEpidemicCommand(CoverUp: true));
        Assert.True(epidemic.IsCoverUp);
        Assert.True(epidemic.IsWatchRunning);

        var sawInspector = false;
        var limit = (Epidemics.WatchSegments + 2) * (Epidemics.WatchTickRate + 1) + (20 * GameDate.HoursPerDay);
        for (var tick = 0; tick < limit && hospital.Epidemics.Current is not null; tick++)
        {
            simulation.Step();
            sawInspector |= simulation.Entities.OfType<Humanoid>().Any(humanoid => humanoid is { Type: HumanoidType.Inspector, Behaviour: InspectorBehaviour });
        }

        Assert.Null(hospital.Epidemics.Current);
        Assert.True(sawInspector, "The inspector never came.");
        Assert.NotEqual(EpidemicOutcome.None, epidemic.Outcome);
        if (epidemic.Outcome == EpidemicOutcome.CoveredUp)
        {
            Assert.InRange(epidemic.Compensation, 1000, 15000);
            Assert.Contains(hospital.Transactions, t => t.Kind == TransactionKind.EpidemicCompensation && t.Amount == epidemic.Compensation);
        }
        else
        {
            Assert.Contains(hospital.Transactions, t => t.Kind == TransactionKind.EpidemicCoverUpFine && t.Amount == -epidemic.CoverUpFine);
        }
    }

    [Fact]
    public void ANurseNearbyVaccinatesAPickedPatientForTheFee()
    {
        // Without a pharmacy the diagnosed patient waits for one, standing still.
        var simulation = NewStaffedHospital(Rules(25));
        Run(simulation, new SetPolicyCommand(PolicyKind.GuessCure, 500));
        var hospital = simulation.Hospitals[0];
        var epidemic = RevealEpidemic(simulation);
        var infected = (Humanoid)simulation.FindEntity(epidemic.InfectedIds[0])!;
        var patient = (PatientBehaviour)infected.Behaviour!;

        // Vaccination needs a cover-up.
        Run(simulation, new VaccinateCommand(infected.Id));
        Assert.False(patient.IsMarkedForVaccination);
        Run(simulation, new AnswerEpidemicCommand(CoverUp: true));
        for (var tick = 0; tick < 5 * GameDate.HoursPerDay && !(patient.Phase == PatientPhase.Waiting && !infected.IsMoving); tick++)
        {
            simulation.Step();
        }

        Assert.Equal(PatientPhase.Waiting, patient.Phase);
        HireNurseNear(simulation, infected.Tile);
        Run(simulation, new VaccinateCommand(infected.Id));
        Assert.True(patient.IsMarkedForVaccination);

        for (var tick = 0; tick < 200 && !patient.IsVaccinated; tick++)
        {
            simulation.Step();
        }

        Assert.True(patient.IsVaccinated, "The nurse did not vaccinate the patient.");
        Assert.False(patient.IsMarkedForVaccination);
        Assert.Contains(hospital.Transactions, t => t.Kind == TransactionKind.Vaccination && t.Amount == -50);

        // Vaccinated patients still count until they are cured.
        Assert.True(patient.IsInfected);
        Assert.Equal(1, Epidemics.CountInfectedPatients(simulation, epidemic));
        var nurse = simulation.Entities.OfType<Humanoid>().Single(humanoid => humanoid.Behaviour is NurseBehaviour);
        Assert.Equal(NursePhase.Idle, ((NurseBehaviour)nurse.Behaviour!).Phase);
    }

    [Fact]
    public void AnEpidemicSurvivesASave()
    {
        var simulation = NewEpidemicHospital(spreadFactor: 200);
        RevealEpidemic(simulation);
        Run(simulation, new AnswerEpidemicCommand(CoverUp: true));
        StepFor(simulation, 100);

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

    private static void HireNurseNear(Simulation simulation, Sim.Maps.TilePosition tile)
    {
        for (var distance = 2; distance <= 4; distance++)
        {
            foreach (var (dx, dy) in new[] { (distance, 0), (-distance, 0), (0, distance), (0, -distance) })
            {
                var target = new Sim.Maps.TilePosition(tile.X + dx, tile.Y + dy);
                Run(simulation, new HireStaffCommand(Sim.Staff.StaffClass.Nurse, 0, target));
                if (simulation.Entities.OfType<Humanoid>().Any(humanoid => humanoid.Behaviour is NurseBehaviour))
                {
                    return;
                }
            }
        }

        Assert.Fail("Found no place for the nurse.");
    }

    /// <summary>Brings a contagious patient by the cheat and steps until they are diagnosed and the fax asks.</summary>
    private static Epidemic RevealEpidemic(Simulation simulation)
    {
        var epidemics = simulation.Hospitals[0].Epidemics;
        Run(simulation, new CheatCommand(CheatKind.Epidemic));
        for (var tick = 0; tick < 30 * GameDate.HoursPerDay && !epidemics.IsFaxPending; tick++)
        {
            simulation.Step();
        }

        return Assert.IsType<Epidemic>(epidemics.Current);
    }

    private static Simulation NewEpidemicHospital(int spreadFactor = 25)
    {
        var simulation = NewStaffedHospital(Rules(spreadFactor));
        BuildPharmacy(simulation);

        // Without diagnosis rooms, patients the GP diagnosed halfway have their disease guessed.
        Run(simulation, new SetPolicyCommand(PolicyKind.GuessCure, 500));
        return simulation;
    }

    private static LevelRules Rules(int spreadFactor)
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
            diseases: diseases,
            epidemics: new EpidemicRules(new int[DiseaseRules.ExpertiseCount + 1], spreadFactor: spreadFactor));
    }
}
