using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;

namespace CorsixNet.Sim.Tests;

/// <summary>The patient window's buttons (CorsixTH's UIPatient:goHome and guessDisease).</summary>
public class PatientWindowTests
{
    private static readonly DiseaseDefinition Cold = DiseaseCatalog.Find("uncommon_cold")!;

    [Fact]
    public void APatientSentHomeCountsAsKickedOut()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, new TilePosition(2, 2));
        var hospital = simulation.Hospitals[0];

        Run(simulation, new SendPatientHomeCommand(humanoid.Id));

        Assert.Equal(PatientPhase.GoingHome, patient.Phase);
        Assert.Equal(1, hospital.NotCured);
        Assert.Equal(1, hospital.CaseOf(Cold, simulation.Rules.Diseases).TurnedAway);

        // Only once; and another player cannot.
        Run(simulation, new SendPatientHomeCommand(humanoid.Id));
        Assert.Equal(1, hospital.NotCured);
    }

    [Fact]
    public void NobodyIsSentHomeOrGuessedInARoom()
    {
        var simulation = NewSimulation();
        simulation.Map[5, 5].RoomId = 9;
        var (humanoid, patient) = AddPatient(simulation, new TilePosition(5, 5));
        patient.DiagnosisProgress = 300;
        simulation.Hospitals[0].CaseOf(Cold, simulation.Rules.Diseases).Discovered = true;

        Run(simulation, new SendPatientHomeCommand(humanoid.Id));
        Run(simulation, new GuessPatientCureCommand(humanoid.Id));

        Assert.NotEqual(PatientPhase.GoingHome, patient.Phase);
        Assert.False(patient.IsDiagnosed);
    }

    [Fact]
    public void AGuessNeedsSomeDiagnosisAndAKnownDisease()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, new TilePosition(2, 2));
        var entry = simulation.Hospitals[0].CaseOf(Cold, simulation.Rules.Diseases);

        Run(simulation, new GuessPatientCureCommand(humanoid.Id));
        Assert.False(patient.IsDiagnosed);

        patient.DiagnosisProgress = 300;
        Run(simulation, new GuessPatientCureCommand(humanoid.Id));
        Assert.False(patient.IsDiagnosed);

        // Known now: diagnosed, and with no pharmacy the patient waits and asks.
        entry.Discovered = true;
        Run(simulation, new GuessPatientCureCommand(humanoid.Id));
        Assert.True(patient.IsDiagnosed);
        Assert.Equal(PatientPhase.Waiting, patient.Phase);
        Assert.Equal(PatientQuestion.NoTreatmentRoom, patient.Question);
    }

    [Fact]
    public void APatientWhoWillNotPayForTheGuessedCureGoesHome()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        hospital.CaseOf(Cold, simulation.Rules.Diseases).Discovered = true;
        hospital.SetTreatmentPrice(Cold, simulation.Rules.Diseases, 120);
        var patients = Enumerable.Range(0, 20).Select(_ => AddPatient(simulation, new TilePosition(2, 2))).ToList();
        foreach (var (humanoid, patient) in patients)
        {
            patient.DiagnosisProgress = 300;
            Run(simulation, new GuessPatientCureCommand(humanoid.Id));
        }

        // At 120 % about half refuse (e^−0.8 agree): they are put off by the price.
        Assert.All(patients, entry => Assert.True(entry.Patient.IsDiagnosed));
        var refused = patients.Count(entry => entry.Patient.Phase == PatientPhase.GoingHome);
        Assert.InRange(refused, 1, 19);
        Assert.Equal(refused, hospital.NotCured);
    }

    private static (Humanoid Humanoid, PatientBehaviour Patient) AddPatient(Simulation simulation, TilePosition tile)
    {
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, tile, simulation.Random, PatientLook.Standard, 0));
        var patient = new PatientBehaviour { Health = 0.9m };
        patient.SetDisease(Cold, 0, simulation.Random);
        humanoid.Behaviour = patient;
        return (humanoid, patient);
    }

    private static void Run(Simulation simulation, Command command)
    {
        simulation.Schedule(0, command);
        simulation.Step();
    }

    private static Simulation NewSimulation()
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

        return new Simulation(map, seed: 1, rules: new LevelRules(10_000, [], []));
    }
}
