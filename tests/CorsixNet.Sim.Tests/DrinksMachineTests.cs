using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.People;
using static CorsixNet.Sim.Definitions.FootprintFlags;

namespace CorsixNet.Sim.Tests;

/// <summary>Thirsty patients buying a can (CorsixTH's Patient:_constructGetSodaAction, Hospital:sellSodaToPatient).</summary>
public class DrinksMachineTests
{
    private const int MaleUse = 190;
    private const int FemaleUse = 250;

    private static readonly Dictionary<HumanoidType, UseAnimations> Use = new()
    {
        [HumanoidType.StandardMalePatient] = new(null, [new(MaleUse, 5)], null),
        [HumanoidType.StandardFemalePatient] = new(null, [new(FemaleUse, 4)], null),
    };

    private static readonly ObjectDefinition Machine = new(
        "drinks_machine",
        "Drinks Machine",
        7,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.South] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition)], AnimateFromUsePosition: true),
        },
        CorridorObject: true,
        UsageSouth: Use);

    [Fact]
    public void AThirstyPatientWalksToTheMachineBuysACanAndCarriesOn()
    {
        var simulation = NewSimulation();
        var machine = ObjectPlacer.Place(simulation, Machine, new TilePosition(6, 2), Direction.South);
        var (humanoid, patient) = AddPatient(simulation);
        var hospital = simulation.Hospitals[0];

        StartForDrink(simulation, humanoid, patient);
        Assert.Equal(machine.Id, patient.DrinksMachineId);
        var toiletNeed = patient.ToiletNeed;
        Assert.Equal(PatientPhase.SeekReception, patient.Phase);

        var animations = new HashSet<int>();
        for (var tick = 0; tick < 200 && patient.DrinksMachineId != 0; tick++)
        {
            simulation.Step();
            if (humanoid.ActionAnimation is { } animation)
            {
                animations.Add(animation.Id);
                Assert.Equal(new TilePosition(6, 3), humanoid.Tile);
                Assert.Equal(machine.Id, humanoid.HiddenObjectId);
            }
        }

        Assert.Equal(0, patient.DrinksMachineId);
        Assert.Contains(humanoid.Type == HumanoidType.StandardMalePatient ? MaleUse : FemaleUse, animations);
        Assert.Null(humanoid.ActionAnimation);

        // patient_drinks_machine_after_use: 0.7 to 1 less thirsty, 0.05 to 0.1 more in need of the toilet, the can paid for.
        Assert.InRange(patient.Thirst, 0m, 0.1m);
        Assert.False(patient.IsThirsty);
        Assert.InRange(patient.ToiletNeed - toiletNeed, 0.05m, 0.1m);
        Assert.Equal(1, hospital.SodasSold);
        Assert.Equal(new Transaction(simulation.Hours - 1, TransactionKind.Drinks, string.Empty, LevelRules.DefaultSodaPrice, 10_000 + LevelRules.DefaultSodaPrice), hospital.Transactions[0]);
        Assert.Contains(simulation.FloatingDollarSigns, sign => sign.Amount == LevelRules.DefaultSodaPrice && !sign.Insurance);
        Assert.Equal(PatientPhase.SeekReception, patient.Phase);
    }

    [Fact]
    public void APatientSentHomeAtTheMachineFinishesBuyingButDoesNotPay()
    {
        var simulation = NewSimulation();
        ObjectPlacer.Place(simulation, Machine, new TilePosition(6, 2), Direction.South);
        var (humanoid, patient) = AddPatient(simulation);
        StartForDrink(simulation, humanoid, patient);
        StepUntil(simulation, () => humanoid.ActionAnimation is not null);

        patient.SendHome(simulation, humanoid);

        Assert.Equal(PatientPhase.GoingHome, patient.Phase);
        Assert.NotNull(humanoid.ActionAnimation);
        StepUntil(simulation, () => patient.DrinksMachineId == 0);
        Assert.Equal(0, simulation.Hospitals[0].SodasSold);
        Assert.True(patient.Thirst < 0.8m);
        StepUntil(simulation, () => humanoid.IsMoving);
    }

    [Fact]
    public void AGameSavedWhileBuyingACanContinuesIdentically()
    {
        var simulation = NewSimulation();
        ObjectPlacer.Place(simulation, Machine, new TilePosition(6, 2), Direction.South);
        var (humanoid, patient) = AddPatient(simulation);
        StartForDrink(simulation, humanoid, patient);
        StepUntil(simulation, () => humanoid.ActionAnimation is not null);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());

        for (var i = 0; i < 30; i++)
        {
            simulation.Step();
            restored.Step();
        }

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(1, restored.Hospitals[0].SodasSold);
    }

    [Fact]
    public void ThePriceComesFromTheLevel()
    {
        var rules = new LevelRules(0, [], [], sodaPrice: 35);
        Assert.Equal(35, rules.SodaPrice);
        Assert.Equal(LevelRules.DefaultSodaPrice, new LevelRules(0, [], []).SodaPrice);
    }

    /// <summary>A thirsty patient looking for a desk whose daily check finds the machine (the chance of not going is retried).</summary>
    private static void StartForDrink(Simulation simulation, Humanoid humanoid, PatientBehaviour patient)
    {
        for (var day = 0; day < 50 && patient.DrinksMachineId == 0; day++)
        {
            patient.Thirst = 0.8m;
            patient.DrinkTimeout = 0;
            Feelings.OnEndDay(simulation, humanoid);
        }
    }

    private static void StepUntil(Simulation simulation, Func<bool> done)
    {
        for (var tick = 0; tick < 500 && !done(); tick++)
        {
            simulation.Step();
        }

        Assert.True(done());
    }

    private static (Humanoid Humanoid, PatientBehaviour Patient) AddPatient(Simulation simulation)
    {
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, new TilePosition(2, 5), simulation.Random, PatientLook.Standard, 0));
        var patient = new PatientBehaviour { Health = 0.9m };
        patient.SetDisease(DiseaseCatalog.Find("uncommon_cold")!, 0, simulation.Random);
        humanoid.Behaviour = patient;
        return (humanoid, patient);
    }

    private static Simulation NewSimulation()
    {
        var map = TestMaps.FromRows("..........", "..........", "..........", "..........", "..........", "..........", "..........", "..........");
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Parcel = 1;
                map[x, y].Set(TileFlags.Buildable | TileFlags.Hospital | TileFlags.BuildableNorth | TileFlags.BuildableEast | TileFlags.BuildableSouth | TileFlags.BuildableWest, true);
            }
        }

        map.UpdatePathfinding();
        return new Simulation(map, seed: 1, new GameContent([], [114], [Machine]), new LevelRules(10_000, [], []));
    }
}
