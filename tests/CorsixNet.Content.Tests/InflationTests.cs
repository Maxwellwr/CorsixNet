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

/// <summary>The inflation room (CorsixTH's rooms/inflation.lua, objects/machines/inflator.lua).</summary>
public class InflationTests
{
    private static readonly TileRect Area = new(3, 13, 5, 5);

    [Fact]
    public void BloatyHeadsArePoppedAndPumpedUpAgainAndGoHomeCured()
    {
        var simulation = NewStaffedHospital(Rules());
        var room = BuildInflation(simulation);
        var hospital = simulation.Hospitals[0];
        var treated = new HashSet<int>();
        var animations = new HashSet<int>();
        var bloatedHeads = new Dictionary<int, byte>();
        var shrunk = new HashSet<int>();

        for (var tick = 0; tick < 3 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is not PatientBehaviour patient)
                {
                    continue;
                }

                if (!bloatedHeads.ContainsKey(humanoid.Id))
                {
                    bloatedHeads[humanoid.Id] = humanoid.Layers[0];
                }
                else if (humanoid.Layers[0] == bloatedHeads[humanoid.Id] - 10)
                {
                    shrunk.Add(humanoid.Id);
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
        Assert.All(bloatedHeads.Values, layer => Assert.InRange(layer, 12, 16));
        Assert.NotEmpty(shrunk);
        Assert.Subset(treated, shrunk);
        Assert.Subset(new HashSet<int> { 464, 478, 482, 496, 576 }, animations);
        Assert.Contains(496, animations);
        Assert.True(hospital.Cured + hospital.Deaths > 0, "Nobody was cured or died.");
        Assert.Contains(hospital.Transactions, t => t is { Kind: TransactionKind.Treatment, Subject: "bloaty_head" });

        // The doctor keeps to the room.
        var doctor = simulation.Entities.OfType<Humanoid>().Single(h => h.Behaviour is DoctorBehaviour { RoomId: var id } && id == room.Id);
        Assert.True(Area.Contains(doctor.Tile), $"The doctor left the inflation room for {doctor.Tile}.");

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        StepFor(simulation, 5 * GameDate.HoursPerDay);
        StepFor(loaded, 5 * GameDate.HoursPerDay);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    /// <summary>Bloaty head the only disease, known and easy to diagnose, so that the GP sends patients straight on.</summary>
    internal static LevelRules Rules()
    {
        var visuals = new int[DiseaseRules.VisualsCount];
        visuals[DiseaseCatalog.Find("bloaty_head")!.VisualsId!.Value] = 1;
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
            [KeyValuePair.Create("gp", 2500), KeyValuePair.Create("inflation", 2500)],
            [KeyValuePair.Create(ReceptionDesks.ObjectId, 600)],
            diseases: diseases);
    }

    internal static Room BuildInflation(Simulation simulation)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("inflation", Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "inflation");
        Place(simulation, "inflator");
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
