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

/// <summary>The operating theatre (CorsixTH's rooms/operating_theatre.lua and its objects).</summary>
public class OperatingTheatreTests
{
    private static readonly TileRect WardArea = new(2, 13, 6, 6);
    private static readonly TileRect TheatreArea = new(14, 5, 6, 6);

    [Fact]
    public void TwoSurgeonsInScrubsOperateOnGownedPatientsAfterTheWard()
    {
        var simulation = NewStaffedHospital(Rules());
        BuildWard(simulation);
        var theatre = BuildTheatre(simulation);
        var hospital = simulation.Hospitals[0];
        var operated = new HashSet<int>();
        var gowned = new HashSet<int>();
        var animations = new HashSet<int>();
        var surgeonsAtOnce = 0;

        for (var tick = 0; tick < 5 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            var surgeons = 0;
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Type == HumanoidType.Surgeon)
                {
                    surgeons++;
                    if (humanoid.ActionAnimation is { } own)
                    {
                        animations.Add(own.Id);
                    }
                }

                if (humanoid.Type is HumanoidType.GownedMalePatient or HumanoidType.GownedFemalePatient)
                {
                    gowned.Add(humanoid.Id);
                }

                if (humanoid.Behaviour is PatientBehaviour { Phase: PatientPhase.OnTrolley, TrolleyUse: not null } patient && patient.TargetId == theatre.Id)
                {
                    operated.Add(humanoid.Id);
                }
            }

            surgeonsAtOnce = Math.Max(surgeonsAtOnce, surgeons);
        }

        Assert.Equal(2, surgeonsAtOnce);
        Assert.NotEmpty(gowned);
        Assert.True(operated.Count >= 1, "Nobody was operated on.");
        Assert.Contains(animations, id => id is 2780 or 2782 or 2784);
        Assert.Contains(2362, animations);
        Assert.Contains(animations, id => id is 2318 or 2322 or 2932);
        Assert.Contains(animations, id => id is 4890 or 2326);
        // The operations cure them (the payments come from patients or their insurance).
        Assert.True(hospital.Cured >= 1, "Nobody was cured.");
        Assert.DoesNotContain(simulation.Entities.OfType<Humanoid>(), h => h.Behaviour is PatientBehaviour { Phase: PatientPhase.GoingHome }
            && h.Type is HumanoidType.GownedMalePatient or HumanoidType.GownedFemalePatient);

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
    public void SurgeonsLeavingChangeBackIntoDoctorsAtTheScreen()
    {
        var simulation = NewStaffedHospital(Rules());
        var theatre = BuildTheatre(simulation);
        for (var tick = 0; tick < 31 * GameDate.HoursPerDay && InScrubs(simulation) < 2; tick++)
        {
            simulation.Step();
        }

        Assert.Equal(2, InScrubs(simulation));
        var surgeons = simulation.Entities.OfType<Humanoid>().Where(h => h.Type == HumanoidType.Surgeon).ToList();

        // Editing the room sends its staff out (commandEnteringStaff's last actions: to the screen, and change back).
        Run(simulation, new EditRoomCommand(theatre.Id));
        var animations = new HashSet<int>();
        var screen = simulation.Entities.OfType<RoomObject>().Single(o => o.ObjectType == "surgeon_screen");
        for (var tick = 0; tick < 10 * GameDate.HoursPerDay && surgeons.Any(s => s.Type == HumanoidType.Surgeon || s.ActionAnimation is not null); tick++)
        {
            simulation.Step();
            foreach (var surgeon in surgeons)
            {
                if (surgeon.ActionAnimation is { } animation && surgeon.HiddenObjectId == screen.Id)
                {
                    if (animations.Count == 0)
                    {
                        using var stream = new MemoryStream();
                        simulation.Save(stream);
                        stream.Position = 0;
                        var loaded = Simulation.Load(stream, BaseGameContent.Create());
                        StepFor(loaded, 3);
                        var copy = new MemoryStream();
                        simulation.Save(copy);
                        copy.Position = 0;
                        var again = Simulation.Load(copy, BaseGameContent.Create());
                        StepFor(again, 3);
                        Assert.Equal(again.ComputeStateHash(), loaded.ComputeStateHash());
                    }

                    animations.Add(animation.Id);
                    Assert.True(theatre.Area.Contains(surgeon.Tile));
                }
            }
        }

        Assert.All(surgeons, s => Assert.Equal(HumanoidType.Doctor, s.Type));

        // use_screen.lua: the first back hangs scrubs on a screen with no greens (2792), the second on one with a green (2790).
        Assert.Equal(new HashSet<int> { 2792, 2790 }, animations);
    }

    private static int InScrubs(Simulation simulation) =>
        simulation.Entities.OfType<Humanoid>().Count(h => h is { Type: HumanoidType.Surgeon, Behaviour: DoctorBehaviour { Phase: DoctorPhase.OtReady } });

    /// <summary>Kidney beans the only disease, known and easy to diagnose: the ward, then the operating theatre.</summary>
    private static LevelRules Rules()
    {
        var nonVisuals = new int[DiseaseRules.NonVisualsCount];
        nonVisuals[DiseaseCatalog.Find("kidney_beans")!.NonVisualsId!.Value] = 1;
        var diseases = new DiseaseRules(
            Enumerable.Repeat(100, DiseaseRules.ExpertiseCount + 1).ToArray(),
            Enumerable.Repeat(true, DiseaseRules.ExpertiseCount + 1).ToArray(),
            new int[DiseaseRules.ExpertiseCount + 1],
            new int[DiseaseRules.VisualsCount],
            nonVisuals,
            new int[DiseaseRules.VisualsCount],
            new int[DiseaseRules.NonVisualsCount],
            0,
            0);
        return new LevelRules(
            1_000_000,
            [KeyValuePair.Create("gp", 2500), KeyValuePair.Create("ward", 2500), KeyValuePair.Create("operating_theatre", 2500)],
            [KeyValuePair.Create(ReceptionDesks.ObjectId, 600)],
            diseases: diseases);
    }

    private static void BuildWard(Simulation simulation)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("ward", WardArea, new DoorPlacement(new(5, WardArea.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "ward");
        Place(simulation, "bed", WardArea);
        Place(simulation, "desk", WardArea);
        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        Run(simulation, new HireStaffCommand(StaffClass.Nurse, 0, new(14, 16)));
    }

    private static Room BuildTheatre(Simulation simulation)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("operating_theatre", TheatreArea, new DoorPlacement(new(17, TheatreArea.Bottom), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "operating_theatre");
        foreach (var type in new[] { "operating_table", "surgeon_screen", "op_sink1", "x_ray_viewer" })
        {
            Place(simulation, type, TheatreArea);
        }

        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);

        // The super doctor cheat makes every doctor for hire a surgeon.
        Run(simulation, new CheatCommand(CheatKind.ToggleSuperDoctor));
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, new(15, 16)));
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, new(16, 16)));
        return room;
    }

    private static void Place(Simulation simulation, string type, TileRect area)
    {
        for (var y = area.Y; y < area.Bottom; y++)
        {
            for (var x = area.X; x < area.Right; x++)
            {
                foreach (var orientation in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
                {
                    if (ObjectPlacer.Check(simulation, type, new(x, y), orientation, 0) == ObjectPlacementProblem.None)
                    {
                        Run(simulation, new PlaceObjectCommand(type, new(x, y), orientation));
                        return;
                    }
                }
            }
        }

        Assert.Fail($"Found no place for {type}.");
    }
}
