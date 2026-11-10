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

/// <summary>Slack patients: slack tongues and bald heads (CorsixTH's diseases/slack_tongue.lua, baldness.lua).</summary>
public class SlackPatientTests
{
    private static readonly TileRect Area = new(3, 13, 6, 6);

    [Fact]
    public void BaldMenAreSlackPatientsWhoSitAtTheGpAndAreXRayed()
    {
        var simulation = NewStaffedHospital(Rules("baldness"));
        var room = Build(simulation, "x_ray", ("x_ray", true), ("radiation_shield", false));
        var types = new HashSet<HumanoidType>();
        var heads = new HashSet<byte>();
        var animations = new HashSet<int>();

        Watch(simulation, 3 * 31 * GameDate.HoursPerDay, humanoid =>
        {
            if (humanoid.Behaviour is PatientBehaviour)
            {
                types.Add(humanoid.Type);
                heads.Add(humanoid.Layers[0]);
                if (humanoid.ActionAnimation is { } animation)
                {
                    animations.Add(animation.Id);
                }
            }
        });

        Assert.Equal([HumanoidType.SlackMalePatient], types);
        Assert.Equal([(byte)12], heads);
        Assert.Contains(animations, id => id is 4580 or 4588 or 1540 or 1544 or 4606);
        Assert.Contains(5178, animations);
        Assert.Contains(simulation.Hospitals[0].Transactions, t => t is { Kind: TransactionKind.Treatment, Subject: "diag_x_ray" });
        AssertSaveLoad(simulation);
        _ = room;
    }

    [Fact]
    public void SlackTonguesAreMenAndWomenWhoGoIntoTheScannerDressed()
    {
        var simulation = NewStaffedHospital(Rules("slack_tongue"));
        var room = Build(simulation, "scanner", ("scanner", false), ("console", false), ("screen", true));
        var types = new HashSet<HumanoidType>();
        var scanned = new HashSet<HumanoidType>();

        Watch(simulation, 3 * 31 * GameDate.HoursPerDay, humanoid =>
        {
            if (humanoid.Behaviour is PatientBehaviour patient)
            {
                types.Add(humanoid.Type);
                if (patient is { Phase: PatientPhase.OnTrolley, Use: not null } && patient.TargetId == room.Id)
                {
                    scanned.Add(humanoid.Type);
                }
            }
        });

        Assert.Subset(new HashSet<HumanoidType> { HumanoidType.SlackMalePatient, HumanoidType.SlackFemalePatient }, types);
        Assert.NotEmpty(scanned);
        Assert.Subset(new HashSet<HumanoidType> { HumanoidType.SlackMalePatient, HumanoidType.SlackFemalePatient }, scanned);
        Assert.Contains(simulation.Hospitals[0].Transactions, t => t is { Kind: TransactionKind.Treatment, Subject: "diag_scanner" });
        AssertSaveLoad(simulation);
    }

    private static void Watch(Simulation simulation, int ticks, Action<Humanoid> look)
    {
        for (var tick = 0; tick < ticks; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                look(humanoid);
            }
        }
    }

    private static void AssertSaveLoad(Simulation simulation)
    {
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    /// <summary>One visual disease only, known but hard for the GP to diagnose, available from the start.</summary>
    private static LevelRules Rules(string disease)
    {
        var visuals = new int[DiseaseRules.VisualsCount];
        visuals[DiseaseCatalog.Find(disease)!.VisualsId!.Value] = 1;
        var diseases = new DiseaseRules(
            Enumerable.Repeat(100, DiseaseRules.ExpertiseCount + 1).ToArray(),
            Enumerable.Repeat(true, DiseaseRules.ExpertiseCount + 1).ToArray(),
            Enumerable.Repeat(900, DiseaseRules.ExpertiseCount + 1).ToArray(),
            visuals,
            new int[DiseaseRules.NonVisualsCount],
            new int[DiseaseRules.VisualsCount],
            new int[DiseaseRules.NonVisualsCount],
            0,
            0);
        return new LevelRules(
            1_000_000,
            [KeyValuePair.Create("gp", 2500), KeyValuePair.Create("x_ray", 2500), KeyValuePair.Create("scanner", 2500)],
            [KeyValuePair.Create(ReceptionDesks.ObjectId, 600)],
            diseases: diseases);
    }

    private static Room Build(Simulation simulation, string roomType, params (string Type, bool FromBack)[] objects)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan(roomType, Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == roomType);
        foreach (var (type, fromBack) in objects)
        {
            Place(simulation, type, fromBack);
        }

        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, new(14, 16)));
        return room;
    }

    private static void Place(Simulation simulation, string type, bool fromBack)
    {
        for (var i = 0; i < Area.Height; i++)
        {
            var y = fromBack ? Area.Y + i : Area.Bottom - 1 - i;
            for (var j = 0; j < Area.Width; j++)
            {
                var x = fromBack ? Area.X + j : Area.Right - 1 - j;
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
