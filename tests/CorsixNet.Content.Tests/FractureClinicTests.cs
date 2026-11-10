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

/// <summary>Men in casts and the fracture clinic (CorsixTH's diseases/fractured_bones.lua, rooms/fracture_clinic.lua, objects/machines/cast_remover.lua).</summary>
public class FractureClinicTests
{
    private static readonly TileRect Area = new(3, 13, 5, 5);

    [Fact]
    public void TheNurseTakesTheCastsOffWithTheCastRemover()
    {
        var simulation = NewStaffedHospital(Rules());
        var room = BuildFractureClinic(simulation);
        var hospital = simulation.Hospitals[0];
        var treated = new HashSet<int>();
        var animations = new HashSet<int>();
        var inCasts = new HashSet<int>();
        var castsOff = new HashSet<int>();

        for (var tick = 0; tick < 3 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is not PatientBehaviour patient)
                {
                    continue;
                }

                Assert.Equal(HumanoidType.AlternateMalePatient, humanoid.Type);
                if (humanoid.Layers[2] + humanoid.Layers[3] + humanoid.Layers[4] > 0)
                {
                    inCasts.Add(humanoid.Id);
                }
                else if (inCasts.Contains(humanoid.Id))
                {
                    castsOff.Add(humanoid.Id);
                }

                if (patient is { Phase: PatientPhase.OnTrolley, TrolleyUse: not null } && patient.TargetId == room.Id)
                {
                    treated.Add(humanoid.Id);
                    if (humanoid.ActionAnimation is { } animation)
                    {
                        animations.Add(animation.Id);
                    }
                }
            }
        }

        Assert.True(treated.Count >= 2, $"Only {treated.Count} patients were treated.");
        Assert.NotEmpty(castsOff);
        Assert.Contains(2688, animations);
        Assert.Contains(3662, animations);
        Assert.True(hospital.Cured + hospital.Deaths > 0, "Nobody was cured or died.");

        // The nurse keeps to the room.
        var nurse = simulation.Entities.OfType<Humanoid>().Single(h => h.Behaviour is NurseBehaviour { RoomId: var id } && id == room.Id);
        Assert.True(Area.Contains(nurse.Tile), $"The nurse left the fracture clinic for {nurse.Tile}.");

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        StepFor(simulation, 5 * GameDate.HoursPerDay);
        StepFor(loaded, 5 * GameDate.HoursPerDay);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    /// <summary>Fractured bones the only disease, known and easy to diagnose, so that the GP sends patients straight on.</summary>
    private static LevelRules Rules()
    {
        var visuals = new int[DiseaseRules.VisualsCount];
        visuals[DiseaseCatalog.Find("fractured_bones")!.VisualsId!.Value] = 1;
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
            [KeyValuePair.Create("gp", 2500), KeyValuePair.Create("fracture_clinic", 2500)],
            [KeyValuePair.Create(ReceptionDesks.ObjectId, 600)],
            diseases: diseases);
    }

    private static Room BuildFractureClinic(Simulation simulation)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("fracture_clinic", Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "fracture_clinic");
        Place(simulation, "cast_remover");
        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        Run(simulation, new HireStaffCommand(StaffClass.Nurse, 0, new(14, 16)));
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
