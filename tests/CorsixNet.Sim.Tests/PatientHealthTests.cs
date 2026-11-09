using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;

namespace CorsixNet.Sim.Tests;

/// <summary>Patients' health day by day (CorsixTH's Patient:tickDay, _dailyHealthChecks, setToDying, tick and die).</summary>
public class PatientHealthTests
{
    private static readonly DiseaseDefinition Cold = DiseaseCatalog.Find("uncommon_cold")!;

    [Fact]
    public void EachDayCostsHealthAndStandingInTheCorridorALittleHappiness()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, new TilePosition(2, 2), 0.9m);
        humanoid.Happiness = 0.5m;

        Feelings.OnEndDay(simulation, humanoid);

        Assert.Equal(0.896m, patient.Health);

        // Comfortable (0.005), standing about (−0.00002).
        Assert.Equal(0.50498m, humanoid.Happiness);
    }

    [Fact]
    public void AnExtinguisherInSightReassuresOnlyOutsideRooms()
    {
        var simulation = NewSimulation();
        var (outside, _) = AddPatient(simulation, new TilePosition(2, 2), 0.9m);
        var (inside, _) = AddPatient(simulation, new TilePosition(5, 5), 0.9m);
        simulation.Map[5, 5].RoomId = 99;
        simulation.Map[6, 5].RoomId = 99;
        simulation.AddEntity(id => new RoomObject(id, "extinguisher", new TilePosition(3, 3), Direction.South, 0));
        simulation.AddEntity(id => new RoomObject(id, "extinguisher", new TilePosition(6, 5), Direction.South, 0));
        outside.Happiness = 0.5m;
        inside.Happiness = 0.5m;

        Feelings.OnEndDay(simulation, outside);
        Feelings.OnEndDay(simulation, inside);

        Assert.Equal(0.50518m, outside.Happiness);
        Assert.Equal(0.505m, inside.Happiness);
    }

    [Fact]
    public void OnTheRoadOfDeteriorationSomeGetFedUpAndGoHome()
    {
        var simulation = NewSimulation();
        var patients = Enumerable.Range(0, 200).Select(_ => AddPatient(simulation, new TilePosition(2, 2), 0.2m)).ToList();
        foreach (var (humanoid, _) in patients)
        {
            humanoid.Happiness = 0.5m;
            Feelings.OnEndDay(simulation, humanoid);
        }

        var fedUp = patients.Count(entry => entry.Patient.Phase == PatientPhase.GoingHome);
        Assert.InRange(fedUp, 1, 30);
        Assert.Equal(fedUp, simulation.Hospitals[0].CaseOf(Cold, simulation.Rules.Diseases).TurnedAway);
        Assert.All(patients, entry => Assert.Equal(0.196m, entry.Patient.Health));

        // Less happy by 0.0002 than a comfortable day in the corridor.
        Assert.All(patients, entry => Assert.Equal(0.50478m, entry.Humanoid.Happiness));
    }

    [Fact]
    public void AtNoHealthAPatientDiesOnceOutOfTheRoom()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, new TilePosition(2, 2), 0.005m);

        Feelings.OnEndDay(simulation, humanoid);
        Assert.Equal(0m, patient.Health);
        Assert.False(patient.IsSetToDie);

        Feelings.OnEndDay(simulation, humanoid);
        Assert.True(patient.IsSetToDie);

        // In a room, the patient lives on for now.
        simulation.Map[2, 2].RoomId = 99;
        patient.Update(simulation, humanoid);
        Assert.False(humanoid.IsDespawning);

        simulation.Map[2, 2].RoomId = 0;
        patient.Update(simulation, humanoid);
        Assert.True(humanoid.IsDespawning);
        var hospital = simulation.Hospitals[0];
        Assert.Equal(1, hospital.Deaths);
        Assert.Equal(1, hospital.DeathsThisYear);
        Assert.Equal(1, hospital.CaseOf(Cold, simulation.Rules.Diseases).Fatalities);
    }

    [Fact]
    public void ACureRestoresHealthAndCancelsDying()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, new TilePosition(2, 2), 0m);
        Feelings.OnEndDay(simulation, humanoid);
        Assert.True(patient.IsSetToDie);

        patient.Cure();

        Assert.Equal(1m, patient.Health);
        Assert.False(patient.IsSetToDie);
    }

    [Fact]
    public void SavesKeepHealth()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, new TilePosition(2, 2), 0m);
        Feelings.OnEndDay(simulation, humanoid);
        AddPatient(simulation, new TilePosition(3, 3), 0.83m);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        var copies = restored.Entities.OfType<Humanoid>().Select(entry => (PatientBehaviour)entry.Behaviour!).ToList();
        Assert.True(copies[0].IsSetToDie);
        Assert.Equal(0.83m, copies[1].Health);
    }

    private static (Humanoid Humanoid, PatientBehaviour Patient) AddPatient(Simulation simulation, TilePosition tile, decimal health)
    {
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, tile, simulation.Random, PatientLook.Standard, 0));
        var patient = new PatientBehaviour { Health = health };
        patient.SetDisease(Cold, 0);
        humanoid.Behaviour = patient;
        return (humanoid, patient);
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
