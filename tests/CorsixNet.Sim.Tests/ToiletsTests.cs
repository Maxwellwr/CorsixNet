using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Rooms;
using static CorsixNet.Sim.Definitions.FootprintFlags;

namespace CorsixNet.Sim.Tests;

/// <summary>The toilets (CorsixTH's rooms/toilets.lua, SeekToiletsAction, Patient:handleToiletNeed).</summary>
public class ToiletsTests
{
    private const int LooBegin = 1712;
    private const int LooInUse = 1728;
    private const int SinkInUse = 1780;

    private static readonly Dictionary<HumanoidType, UseAnimations> LooUse = new()
    {
        [HumanoidType.StandardMalePatient] = new(new(LooBegin, 3), [new(LooInUse, 4)], new(1740, 3), Begin2: new(1716, 2), Finish2: new(1744, 2)),
        [HumanoidType.StandardFemalePatient] = new(new(LooBegin, 3), [new(LooInUse, 4)], new(1740, 3), Begin2: new(1716, 2), Finish2: new(1744, 2)),
    };

    private static readonly Dictionary<HumanoidType, UseAnimations> SinkUse = new()
    {
        [HumanoidType.StandardMalePatient] = new(new(1776, 3), [new(SinkInUse, 2)], new(1784, 3)),
        [HumanoidType.StandardFemalePatient] = new(new(1776, 3), [new(SinkInUse, 2)], new(1784, 3)),
    };

    private static readonly ObjectDefinition Loo = new(
        "loo",
        "Toilet",
        51,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition)], AnimateFromUsePosition: true),
        },
        UsageNorth: LooUse,
        UsageSouth: LooUse);

    private static readonly ObjectDefinition Sink = new(
        "sink",
        "Toilet Sink",
        32,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition)], AnimateFromUsePosition: true),
        },
        UsageNorth: SinkUse,
        UsageSouth: SinkUse);

    private static readonly RoomDefinition Toilets = new(
        "toilets", "Toilets", 4, 21, new WallSet(88, 89, 96, 97), new Dictionary<string, int> { ["loo"] = 1, ["sink"] = 1 }, ["loo", "sink"], LevelConfigId: 29);

    private static readonly TileRect Area = new(2, 2, 4, 4);

    [Fact]
    public void APatientInNeedUsesATheToiletWashesTheirHandsAndCarriesOn()
    {
        var simulation = NewSimulation();
        BuildToilets(simulation, loos: 1);
        var (humanoid, patient) = AddPatient(simulation, new TilePosition(9, 9));

        SendToToilets(simulation, humanoid, patient);
        Assert.Equal(ToiletVisit.Yes, patient.GoingToToilet);
        Assert.True(patient.IsNeedingToilet);

        var phases = new HashSet<PatientPhase>();
        var animations = new HashSet<int>();
        for (var tick = 0; tick < 1000 && !(phases.Contains(PatientPhase.UsingSink) && patient.Phase is PatientPhase.SeekReception or PatientPhase.Waiting); tick++)
        {
            simulation.Step();
            phases.Add(patient.Phase);
            if (humanoid.ActionAnimation is { } animation)
            {
                animations.Add(animation.Id);
            }
        }

        Assert.Superset(new HashSet<PatientPhase> { PatientPhase.WalkingToRoom, PatientPhase.EnteringRoom, PatientPhase.WalkingToLoo, PatientPhase.UsingLoo, PatientPhase.WalkingToSink, PatientPhase.UsingSink, PatientPhase.LeavingRoom }, phases);
        Assert.Superset(new HashSet<int> { LooBegin, 1716, LooInUse, 1744, SinkInUse }, animations);
        Assert.Equal(ToiletVisit.No, patient.GoingToToilet);
        Assert.False(patient.IsNeedingToilet);
        Assert.True(patient.ToiletNeed <= 0.05m, $"Toilet need {patient.ToiletNeed}.");
        Assert.Equal(0, patient.ToiletObjectId);

        // Back to looking for the reception desk, out of the room.
        Assert.Equal(0, simulation.Map[humanoid.Tile.X, humanoid.Tile.Y].RoomId);
    }

    [Fact]
    public void WithOneToiletTheNextPatientWaitsOutside()
    {
        var simulation = NewSimulation();
        var room = BuildToilets(simulation, loos: 1);
        var (first, firstPatient) = AddPatient(simulation, new TilePosition(9, 9));
        var (second, secondPatient) = AddPatient(simulation, new TilePosition(10, 9));
        SendToToilets(simulation, first, firstPatient);
        SendToToilets(simulation, second, secondPatient);

        var maxInside = 0;
        for (var tick = 0; tick < 3000 && room.DoorQueue?.VisitorCount is not >= 2; tick++)
        {
            simulation.Step();
            maxInside = Math.Max(maxInside, RoomVisits.ToiletsPatientCount(simulation, room, 1));
        }

        for (var tick = 0; tick < 1000 && firstPatient.TargetId == room.Id | secondPatient.TargetId == room.Id; tick++)
        {
            simulation.Step();
            maxInside = Math.Max(maxInside, RoomVisits.ToiletsPatientCount(simulation, room, 1));
        }

        // A private toilet: one at a time, the other queues at the door.
        Assert.Equal(2, room.DoorQueue!.VisitorCount);
        Assert.Equal(1, maxInside);
        Assert.NotEqual(room.Id, firstPatient.TargetId);
        Assert.NotEqual(room.Id, secondPatient.TargetId);
    }

    [Fact]
    public void WithoutToiletsAPatientRemembersAndTriesAgainOnceTheyAreBuilt()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, new TilePosition(9, 9));
        for (var day = 0; day < 50 && patient.GoingToToilet != ToiletVisit.NoToilets; day++)
        {
            Prepare(humanoid, patient);
            Feelings.OnEndDay(simulation, humanoid);
            FinishSpotAction(simulation, patient);
        }

        Assert.Equal(ToiletVisit.NoToilets, patient.GoingToToilet);
        Assert.True(patient.IsNeedingToilet);

        BuildToilets(simulation, loos: 1);
        Assert.Equal(ToiletVisit.No, patient.GoingToToilet);
    }

    [Fact]
    public void AGameSavedInTheToiletsContinuesIdentically()
    {
        var simulation = NewSimulation();
        BuildToilets(simulation, loos: 1);
        var (humanoid, patient) = AddPatient(simulation, new TilePosition(9, 9));
        SendToToilets(simulation, humanoid, patient);
        for (var tick = 0; tick < 1000 && patient.Phase != PatientPhase.UsingLoo; tick++)
        {
            simulation.Step();
        }

        Assert.Equal(PatientPhase.UsingLoo, patient.Phase);
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, NewContent());
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        for (var tick = 0; tick < 100; tick++)
        {
            simulation.Step();
            restored.Step();
        }

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
    }

    /// <summary>A patient in need whose daily check sends them to the toilets (four days in ten they do it on the floor).</summary>
    private static void SendToToilets(Simulation simulation, Humanoid humanoid, PatientBehaviour patient)
    {
        for (var day = 0; day < 50 && patient.GoingToToilet != ToiletVisit.Yes; day++)
        {
            Prepare(humanoid, patient);
            Feelings.OnEndDay(simulation, humanoid);
            FinishSpotAction(simulation, patient);
        }

        Assert.Equal(ToiletVisit.Yes, patient.GoingToToilet);
    }

    private static void Prepare(Humanoid humanoid, PatientBehaviour patient)
    {
        humanoid.Happiness = 1m;
        patient.Health = 0.95m;
        patient.ToiletNeed = 0.9m;
    }

    private static void FinishSpotAction(Simulation simulation, PatientBehaviour patient)
    {
        for (var tick = 0; tick < 50 && patient.SpotAction != PatientSpotAction.None; tick++)
        {
            simulation.Step();
        }
    }

    private static Room BuildToilets(Simulation simulation, int loos)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("toilets", Area, new DoorPlacement(new(3, Area.Bottom), EdgeOrientation.North))));
        var room = simulation.Entities.OfType<Room>().Single();
        Run(simulation, new PlaceObjectCommand("loo", new(2, 2), Direction.North));
        if (loos > 1)
        {
            Run(simulation, new PlaceObjectCommand("loo", new(3, 2), Direction.North));
        }

        Run(simulation, new PlaceObjectCommand("sink", new(5, 2), Direction.North));
        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        return room;
    }

    private static void Run(Simulation simulation, Command command)
    {
        simulation.Schedule(0, command);
        simulation.Step();
    }

    private static (Humanoid Humanoid, PatientBehaviour Patient) AddPatient(Simulation simulation, TilePosition tile)
    {
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, tile, simulation.Random, PatientLook.Standard, 0));
        var patient = new PatientBehaviour { Health = 0.95m };
        patient.SetDisease(DiseaseCatalog.Find("uncommon_cold")!, 0, simulation.Random);
        humanoid.Behaviour = patient;
        return (humanoid, patient);
    }

    private static GameContent NewContent() => new([Toilets], [114], [Loo, Sink], patientAnimations: new Dictionary<HumanoidType, PatientAnimations>
    {
        [HumanoidType.StandardMalePatient] = new(new(2056, 6), new(2244, 4)),
        [HumanoidType.StandardFemalePatient] = new(new(3184, 5), new(4744, 4)),
    });

    private static Simulation NewSimulation()
    {
        var map = TestMaps.FromRows(Enumerable.Repeat("..............", 14).ToArray());
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
        map.UpdateShadows();
        var simulation = new Simulation(map, seed: 5, NewContent(), new LevelRules(100_000, [], []));
        simulation.Schedule(0, new OpenHospitalCommand());
        return simulation;
    }
}
