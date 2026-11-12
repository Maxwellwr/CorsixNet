using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;
using static CorsixNet.Content.Tests.PatientFlowTests;

namespace CorsixNet.Content.Tests;

/// <summary>The psychiatric room (CorsixTH's rooms/psych.lua, objects/couch.lua, comfortable_chair.lua, bookcase.lua).</summary>
public class PsychTests
{
    private static readonly TileRect Area = new(3, 13, 6, 6);

    [Fact]
    public void PatientsLieOnTheCouchWhileThePsychiatristSitsAndAreTreated()
    {
        var simulation = NewStaffedHospital(Rules());
        var room = BuildPsych(simulation, out var psychiatristId);
        var hospital = simulation.Hospitals[0];
        var onCouch = new HashSet<int>();
        var animations = new HashSet<int>();
        var phases = new HashSet<DoctorPhase>();

        for (var tick = 0; tick < 3 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Id == psychiatristId && humanoid.Behaviour is DoctorBehaviour doctor)
                {
                    phases.Add(doctor.Phase);
                    if (humanoid.ActionAnimation is { } own)
                    {
                        animations.Add(own.Id);
                    }
                }

                if (humanoid.Behaviour is PatientBehaviour { Phase: PatientPhase.UsingObject, Use.IsUsing: true } patient && patient.TargetId == room.Id)
                {
                    onCouch.Add(humanoid.Id);
                    if (humanoid.ActionAnimation is { } animation)
                    {
                        animations.Add(animation.Id);
                    }
                }
            }
        }

        Assert.True(onCouch.Count >= 2, $"Only {onCouch.Count} patients lay on the couch.");
        Assert.Contains(animations, id => id is 2528 or 3180);
        Assert.Contains(animations, id => id is 2536 or 3330);
        Assert.Contains(2544, animations);
        Assert.Contains(2414, animations);
        Assert.Contains(DoctorPhase.PsychMeandering, phases);
        Assert.True(hospital.Cured + hospital.Deaths > 0, "Nobody was cured or died.");
        Assert.Contains(hospital.Transactions, t => t is { Kind: TransactionKind.Treatment, Subject: "infectious_laughter" });

        // Only the psychiatrist works there; the GP stays in the GP's office.
        Assert.All(
            simulation.Entities.OfType<Humanoid>().Where(h => h.Behaviour is DoctorBehaviour { RoomId: var id } && id == room.Id),
            h => Assert.True(h.Profile!.IsPsychiatrist));
        var psychiatrist = (Humanoid)simulation.FindEntity(psychiatristId)!;
        Assert.True(Area.Contains(psychiatrist.Tile), $"The psychiatrist left the room for {psychiatrist.Tile}.");

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        StepFor(simulation, 5 * GameDate.HoursPerDay);
        StepFor(loaded, 5 * GameDate.HoursPerDay);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    [Fact]
    public void KingComplexPatientsChangeAtTheScreenAfterTheCouch()
    {
        var simulation = NewStaffedHospital(Rules(king: true));
        var room = BuildPsych(simulation, out _);
        var hospital = simulation.Hospitals[0];
        var kings = new HashSet<int>();
        var changed = new HashSet<int>();
        var animations = new HashSet<int>();

        for (var tick = 0; tick < 3 * 31 * GameDate.HoursPerDay && changed.Count < 2; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is not PatientBehaviour patient)
                {
                    continue;
                }

                if (humanoid.Type == HumanoidType.ElvisPatient)
                {
                    kings.Add(humanoid.Id);
                    if (patient.TargetId == room.Id && humanoid.ActionAnimation is { } animation)
                    {
                        animations.Add(animation.Id);
                    }
                }
                else if (kings.Contains(humanoid.Id) && !changed.Contains(humanoid.Id))
                {
                    // use_screen.lua: a standard man with head 2 and trousers 2, changed at the psychiatrist's screen.
                    Assert.Equal(HumanoidType.StandardMalePatient, humanoid.Type);
                    Assert.Equal(room.Id, patient.TargetId);
                    Assert.Equal(2, humanoid.Layers[0]);
                    Assert.Equal(2, humanoid.Layers[2]);
                    changed.Add(humanoid.Id);
                    if (humanoid.ActionAnimation is { } change)
                    {
                        animations.Add(change.Id);
                    }
                }
            }
        }

        Assert.True(changed.Count >= 2, $"Only {changed.Count} of {kings.Count} kings changed.");
        Assert.Contains(1014, animations);
        Assert.Contains(942, animations);
        Assert.Contains(946, animations);
        StepFor(simulation, 2 * GameDate.HoursPerDay);
        Assert.Contains(hospital.Transactions, t => t is { Kind: TransactionKind.Treatment, Subject: "king_complex" });

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        Assert.Equal(simulation.ComputeStateHash(), Simulation.Load(stream, BaseGameContent.Create()).ComputeStateHash());
    }

    [Fact]
    public void ADoctorWhoIsNoPsychiatristIsNotCalled()
    {
        var simulation = NewStaffedHospital(Rules());
        Run(simulation, new BuildRoomCommand(new RoomPlan("psych", Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "psych");
        foreach (var type in new[] { "couch", "comfortable_chair", "screen" })
        {
            Place(simulation, type);
        }

        Run(simulation, new ConfirmRoomCommand(room.Id));
        var other = simulation.AvailableStaff(StaffClass.Doctor).Select((profile, index) => (profile, index)).First(c => !c.profile.IsPsychiatrist).index;
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, other, new(14, 16)));
        StepFor(simulation, 10 * GameDate.HoursPerDay);

        Assert.Equal(0, room.StaffId);
    }

    /// <summary>
    /// Infectious laughter (or the king complex) the only disease, known and easy to diagnose, so that the GP sends
    /// patients straight on.
    /// </summary>
    private static LevelRules Rules(bool king = false)
    {
        var nonVisuals = new int[DiseaseRules.NonVisualsCount];
        var visuals = new int[DiseaseRules.VisualsCount];
        if (king)
        {
            visuals[DiseaseCatalog.Find("king_complex")!.VisualsId!.Value] = 1;
        }
        else
        {
            nonVisuals[DiseaseCatalog.Find("infectious_laughter")!.NonVisualsId!.Value] = 1;
        }

        var diseases = new DiseaseRules(
            Enumerable.Repeat(100, DiseaseRules.ExpertiseCount + 1).ToArray(),
            Enumerable.Repeat(true, DiseaseRules.ExpertiseCount + 1).ToArray(),
            new int[DiseaseRules.ExpertiseCount + 1],
            visuals,
            nonVisuals,
            new int[DiseaseRules.VisualsCount],
            new int[DiseaseRules.NonVisualsCount],
            0,
            0);
        return new LevelRules(
            1_000_000,
            [KeyValuePair.Create("gp", 2500), KeyValuePair.Create("psych", 2500)],
            [KeyValuePair.Create(ReceptionDesks.ObjectId, 600)],
            diseases: diseases);
    }

    private static Room BuildPsych(Simulation simulation, out int psychiatristId)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("psych", Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "psych");
        foreach (var type in new[] { "couch", "comfortable_chair", "screen" })
        {
            Place(simulation, type);
        }

        Run(simulation, new PlaceObjectCommand("bookcase", FreeSpot(simulation, "bookcase").Tile, FreeSpot(simulation, "bookcase").Orientation));
        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        // The super doctor cheat makes every doctor for hire a psychiatrist.
        Run(simulation, new CheatCommand(CheatKind.ToggleSuperDoctor));
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, new(14, 16)));
        psychiatristId = simulation.Entities.OfType<Humanoid>().Where(h => h.Profile is { IsPsychiatrist: true }).Max(h => h.Id);
        return room;
    }

    private static void Place(Simulation simulation, string type)
    {
        var (tile, orientation) = FreeSpot(simulation, type);
        Run(simulation, new PlaceObjectCommand(type, tile, orientation));
    }

    private static (TilePosition Tile, Direction Orientation) FreeSpot(Simulation simulation, string type)
    {
        for (var y = Area.Bottom - 1; y >= Area.Y; y--)
        {
            for (var x = Area.Right - 1; x >= Area.X; x--)
            {
                foreach (var orientation in new[] { Direction.North, Direction.East })
                {
                    if (ObjectPlacer.Check(simulation, type, new(x, y), orientation, 0) == ObjectPlacementProblem.None)
                    {
                        return (new(x, y), orientation);
                    }
                }
            }
        }

        Assert.Fail($"Found no place for {type}.");
        return default;
    }
}
