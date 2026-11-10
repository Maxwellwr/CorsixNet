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

/// <summary>Hairy patients and the electrolysis room (CorsixTH's diseases/hairyitis.lua, rooms/electrolysis.lua, objects/machines/electrolyser.lua).</summary>
public class ElectrolysisTests
{
    private static readonly TileRect Area = new(3, 13, 6, 6);

    [Fact]
    public void HairyPatientsAreElectrocutedBareWhileTheDoctorWorksTheConsole()
    {
        var simulation = NewStaffedHospital(Rules());
        var room = BuildElectrolysis(simulation);
        var hospital = simulation.Hospitals[0];
        var showered = new HashSet<int>();
        var animations = new HashSet<int>();
        var restored = new HashSet<int>();

        for (var tick = 0; tick < 3 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Type == HumanoidType.ChewbaccaPatient)
                {
                    // Hairy until the electrolyser, which leaves a standard man or woman.
                    restored.Add(-humanoid.Id);
                }
                else if (restored.Contains(-humanoid.Id) && humanoid.Type is HumanoidType.StandardMalePatient or HumanoidType.StandardFemalePatient)
                {
                    restored.Add(humanoid.Id);
                }

                if (humanoid.Behaviour is DoctorBehaviour { Phase: DoctorPhase.UsingShield } && humanoid.ActionAnimation is { } console)
                {
                    animations.Add(console.Id);
                }

                if (humanoid.Behaviour is PatientBehaviour { Phase: PatientPhase.OnTrolley, Use: not null } patient && patient.TargetId == room.Id)
                {
                    showered.Add(humanoid.Id);
                    if (humanoid.ActionAnimation is { } animation)
                    {
                        animations.Add(animation.Id);
                    }
                }
            }
        }

        Assert.True(showered.Count >= 2, $"Only {showered.Count} patients showered.");
        Assert.Contains(1266, animations);
        Assert.Contains(1274, animations);
        Assert.Contains(animations, id => id is 1286 or 2940);
        Assert.Contains(animations, id => id is 798 or 806 or 810 or 814);
        Assert.True(hospital.Cured + hospital.Deaths > 0, "Nobody was cured or died.");
        Assert.Contains(restored, id => id > 0);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        StepFor(simulation, 5 * GameDate.HoursPerDay);
        StepFor(loaded, 5 * GameDate.HoursPerDay);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    /// <summary>Hairyitis the only disease, known and easy to diagnose, so that the GP sends patients straight on.</summary>
    private static LevelRules Rules()
    {
        var visuals = new int[DiseaseRules.VisualsCount];
        visuals[DiseaseCatalog.Find("hairyitis")!.VisualsId!.Value] = 1;
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
            [KeyValuePair.Create("gp", 2500), KeyValuePair.Create("electrolysis", 2500)],
            [KeyValuePair.Create(ReceptionDesks.ObjectId, 600)],
            diseases: diseases);
    }

    private static Room BuildElectrolysis(Simulation simulation)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("electrolysis", Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "electrolysis");
        Place(simulation, "electrolyser");
        Place(simulation, "console");
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
