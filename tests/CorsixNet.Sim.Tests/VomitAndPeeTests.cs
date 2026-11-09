using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;

namespace CorsixNet.Sim.Tests;

/// <summary>Patients being sick and doing it on the floor (CorsixTH's Patient:vomit, Patient:pee, handleToiletNeed).</summary>
public class VomitAndPeeTests
{
    private static readonly Dictionary<HumanoidType, PatientAnimations> Animations = new()
    {
        [HumanoidType.StandardMalePatient] = new(new(2056, 6), new(2244, 4)),
        [HumanoidType.StandardFemalePatient] = new(new(3184, 5), new(4744, 4)),
    };

    [Fact]
    public void AnUnwellPatientIsSickOnTheSpotOnceAndLeavesPuke()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, health: 0.7m);
        var tile = humanoid.Tile;

        EndDaysUntil(simulation, humanoid, () => patient.SpotAction == PatientSpotAction.Vomit);
        Assert.True(patient.HasVomited);
        Assert.True(patient.IsSidetracked);

        var animation = Animations[humanoid.Type].Vomit!.Value;
        simulation.Step();
        Assert.Equal(animation, humanoid.ActionAnimation);
        for (var tick = 0; tick < animation.Length; tick++)
        {
            simulation.Step();
        }

        Assert.Equal(PatientSpotAction.None, patient.SpotAction);
        Assert.Null(humanoid.ActionAnimation);
        var puke = Assert.Single(simulation.Entities.OfType<Litter>());
        Assert.Equal(LitterType.Puke, puke.Type);
        Assert.Equal(tile, puke.Tile);

        // has_vomitted: not again until the patient waits.
        for (var day = 0; day < 20; day++)
        {
            Feelings.OnEndDay(simulation, humanoid);
            Assert.Equal(PatientSpotAction.None, patient.SpotAction);
        }
    }

    [Fact]
    public void AHealthyHappyPatientIsNotSick()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, health: 0.95m);
        for (var day = 0; day < 30; day++)
        {
            humanoid.Happiness = 1m;
            patient.Health = 0.95m;
            Feelings.OnEndDay(simulation, humanoid);
        }

        Assert.False(patient.HasVomited);
    }

    [Fact]
    public void APatientInNeedOfTheToiletDoesItOnTheFloorOrWantsTheToilets()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, health: 0.95m);
        var sawWanting = false;
        for (var day = 0; day < 50 && patient.SpotAction != PatientSpotAction.Pee; day++)
        {
            humanoid.Happiness = 1m;
            patient.Health = 0.95m;
            patient.ToiletNeed = 0.9m;
            patient.GoingToToilet = ToiletVisit.No;
            Feelings.OnEndDay(simulation, humanoid);
            sawWanting |= patient is { IsNeedingToilet: true, GoingToToilet: ToiletVisit.NoToilets };
        }

        Assert.True(sawWanting);
        Assert.Equal(PatientSpotAction.Pee, patient.SpotAction);
        Assert.False(patient.IsNeedingToilet);
        Assert.InRange(patient.ToiletNeed, 0.9m - 0.65m, 0.9m - 0.5m + 0.008m);

        for (var tick = 0; tick < 10; tick++)
        {
            simulation.Step();
        }

        Assert.Equal(LitterType.Pee, Assert.Single(simulation.Entities.OfType<Litter>()).Type);
    }

    [Fact]
    public void APatientOffTheirHospitalsFloorDoesNeither()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, health: 0.5m);
        humanoid.PlaceAt(new TilePosition(9, 9));
        simulation.Map[9, 9].Set(TileFlags.Hospital, false);

        Assert.False(patient.IsAtFullyEmptyTile(simulation, humanoid));
        patient.Vomit(simulation, humanoid);
        patient.Pee(simulation, humanoid);
        Assert.Equal(PatientSpotAction.None, patient.SpotAction);
    }

    [Fact]
    public void AGameSavedWhileAPatientIsSickContinuesIdentically()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, health: 0.7m);
        EndDaysUntil(simulation, humanoid, () => patient.SpotAction == PatientSpotAction.Vomit);
        simulation.Step();
        simulation.Step();

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, NewContent());
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        for (var tick = 0; tick < 20; tick++)
        {
            simulation.Step();
            restored.Step();
        }

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Single(restored.Entities.OfType<Litter>());
    }

    private static void EndDaysUntil(Simulation simulation, Humanoid humanoid, Func<bool> done)
    {
        for (var day = 0; day < 50 && !done(); day++)
        {
            Feelings.OnEndDay(simulation, humanoid);
        }

        Assert.True(done());
    }

    private static (Humanoid Humanoid, PatientBehaviour Patient) AddPatient(Simulation simulation, decimal health)
    {
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, new TilePosition(4, 4), simulation.Random, PatientLook.Standard, 0));
        var patient = new PatientBehaviour { Health = health };
        patient.SetDisease(DiseaseCatalog.Find("uncommon_cold")!, 0, simulation.Random);
        humanoid.Behaviour = patient;
        return (humanoid, patient);
    }

    private static GameContent NewContent() => new([], [114], patientAnimations: Animations);

    private static Simulation NewSimulation()
    {
        var map = TestMaps.FromRows("..........", "..........", "..........", "..........", "..........", "..........", "..........", "..........", "..........", "..........");
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

        map.UpdatePathfinding();
        return new Simulation(map, seed: 1, NewContent(), new LevelRules(10_000, [], []));
    }
}
