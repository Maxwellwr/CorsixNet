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

/// <summary>The slack tongue room (CorsixTH's rooms/slack_tongue.lua, objects/machines/slicer.lua).</summary>
public class SlackTongueTests
{
    private static readonly TileRect Area = new(3, 13, 5, 5);

    [Fact]
    public void SlackTonguesAreCutAndTheirOwnersGoHomeCured()
    {
        var simulation = NewStaffedHospital(Rules());
        var room = BuildSlackTongue(simulation);
        var hospital = simulation.Hospitals[0];
        var treated = new HashSet<int>();
        var animations = new HashSet<int>();
        var men = new HashSet<int>();
        var women = new HashSet<int>();
        var cut = new HashSet<int>();

        for (var tick = 0; tick < 3 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is not PatientBehaviour patient)
                {
                    continue;
                }

                if (humanoid.Type == HumanoidType.SlackMalePatient)
                {
                    men.Add(humanoid.Id);
                }
                else if (humanoid.Type == HumanoidType.SlackFemalePatient && humanoid.Layers[0] >= 10)
                {
                    women.Add(humanoid.Id);
                }

                if (patient is { Phase: PatientPhase.OnTrolley, TrolleyUse: not null } && patient.TargetId == room.Id)
                {
                    treated.Add(humanoid.Id);
                    if (humanoid.ActionAnimation is { } animation)
                    {
                        animations.Add(animation.Id);
                    }
                }

                // After the slicer: men are standard men again, women's tongues have gone from layer 0.
                if (treated.Contains(humanoid.Id) && patient.Phase != PatientPhase.OnTrolley
                    && (humanoid.Type == HumanoidType.StandardMalePatient || (humanoid.Type == HumanoidType.SlackFemalePatient && humanoid.Layers[0] < 10)))
                {
                    cut.Add(humanoid.Id);
                }
            }
        }

        Assert.True(treated.Count >= 2, $"Only {treated.Count} patients were treated.");
        Assert.NotEmpty(men);
        Assert.NotEmpty(women);
        Assert.NotEmpty(cut);
        Assert.Contains(animations, id => id is 1390 or 1394);
        Assert.True(hospital.Cured + hospital.Deaths > 0, "Nobody was cured or died.");

        // The doctor keeps to the room, unless a worn slicer has blown it up with him.
        if (!room.IsCrashed)
        {
            var doctor = simulation.Entities.OfType<Humanoid>().Single(h => h.Behaviour is DoctorBehaviour { RoomId: var id } && id == room.Id);
            Assert.True(Area.Contains(doctor.Tile), $"The doctor left the slack tongue room for {doctor.Tile}.");
        }

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        StepFor(simulation, 5 * GameDate.HoursPerDay);
        StepFor(loaded, 5 * GameDate.HoursPerDay);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    /// <summary>Slack tongue the only disease, known and easy to diagnose, so that the GP sends patients straight on.</summary>
    private static LevelRules Rules()
    {
        var visuals = new int[DiseaseRules.VisualsCount];
        visuals[DiseaseCatalog.Find("slack_tongue")!.VisualsId!.Value] = 1;
        var diseases = new DiseaseRules(
            Enumerable.Repeat(100, DiseaseRules.ExpertiseCount + 1).ToArray(),
            Enumerable.Repeat(true, DiseaseRules.ExpertiseCount + 1).ToArray(),
            new int[DiseaseRules.ExpertiseCount + 1],
            visuals,
            new int[DiseaseRules.NonVisualsCount],
            new int[DiseaseRules.VisualsCount],
            new int[DiseaseRules.NonVisualsCount],
            0,
            0);
        return new LevelRules(
            1_000_000,
            [KeyValuePair.Create("gp", 2500), KeyValuePair.Create("slack_tongue", 2500)],
            [KeyValuePair.Create(ReceptionDesks.ObjectId, 600)],
            diseases: diseases);
    }

    private static Room BuildSlackTongue(Simulation simulation)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("slack_tongue", Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "slack_tongue");
        Place(simulation, "slicer");
        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, new(14, 16)));
        return room;
    }

    private static void Place(Simulation simulation, string type)
    {
        for (var y = Area.Bottom - 1; y >= Area.Y; y--)
        {
            for (var x = Area.Right - 1; x >= Area.X; x--)
            {
                foreach (var orientation in new[] { Direction.North, Direction.East })
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
