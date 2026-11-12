using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.People;
using static CorsixNet.Content.Tests.PatientFlowTests;

namespace CorsixNet.Content.Tests;

/// <summary>Patients queueing sit on benches (CorsixTH's action_queue_on_change_position and objects/bench.lua).</summary>
public class BenchTests
{
    [Fact]
    public void QueueingPatientsSitOnFreeBenchesNearTheDoorAndGetUpWhenCalledIn()
    {
        var simulation = NewStaffedHospital();
        var benches = PlaceBenches(simulation, 3);
        var gp = simulation.Entities.OfType<Room>().Single(r => r.RoomType == "gp");
        var sitters = new HashSet<int>();
        var calledFromBench = new HashSet<int>();
        var animations = new HashSet<int>();
        var saved = false;

        for (var tick = 0; tick < 2 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is not PatientBehaviour patient)
                {
                    continue;
                }

                if (patient.BenchId != 0)
                {
                    Assert.Contains(patient.BenchId, benches);
                    if (humanoid.ActionAnimation is { } animation)
                    {
                        sitters.Add(humanoid.Id);
                        animations.Add(animation.Id);
                    }
                }

                if (sitters.Contains(humanoid.Id) && patient.Phase == PatientPhase.EnteringRoom && patient.TargetId == gp.Id)
                {
                    calledFromBench.Add(humanoid.Id);
                }
            }

            // Nobody shares a bench.
            var taken = simulation.Entities.OfType<Humanoid>().Select(h => (h.Behaviour as PatientBehaviour)?.BenchId ?? 0).Where(id => id != 0).ToList();
            Assert.Equal(taken.Count, taken.Distinct().Count());

            if (!saved && simulation.Entities.OfType<Humanoid>().Any(h => h.Behaviour is PatientBehaviour { BenchId: not 0 } && h.ActionAnimation is not null))
            {
                saved = true;
                using var stream = new MemoryStream();
                simulation.Save(stream);
                stream.Position = 0;
                var loaded = Simulation.Load(stream, BaseGameContent.Create());
                Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
                var copy = Simulation.Load(new MemoryStream(SaveBytes(simulation)), BaseGameContent.Create());
                StepFor(loaded, 50);
                StepFor(copy, 50);
                Assert.Equal(copy.ComputeStateHash(), loaded.ComputeStateHash());
            }
        }

        Assert.NotEmpty(sitters);
        Assert.NotEmpty(calledFromBench);

        // bench.lua's standard patients: sitting down, sitting and getting up, facing north.
        Assert.Contains(animations, id => id is 96 or 232);
        Assert.Contains(animations, id => id is 146 or 224);
        Assert.Contains(animations, id => id is 216 or 240);
    }

    private static byte[] SaveBytes(Simulation simulation)
    {
        using var stream = new MemoryStream();
        simulation.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Benches in the corridor south of the GP's door, facing north, bought and placed.</summary>
    private static HashSet<int> PlaceBenches(Simulation simulation, int count)
    {
        var placed = new HashSet<int>();
        for (var y = 11; y < 16 && placed.Count < count; y++)
        {
            for (var x = 4; x < 10 && placed.Count < count; x++)
            {
                if (ObjectPlacer.Check(simulation, "bench", new TilePosition(x, y), Direction.North, 0) == ObjectPlacementProblem.None)
                {
                    Run(simulation, new PlaceObjectCommand("bench", new TilePosition(x, y), Direction.North));
                    placed.UnionWith(simulation.Entities.OfType<RoomObject>().Where(o => o.ObjectType == "bench").Select(o => o.Id));
                }
            }
        }

        Assert.Equal(count, placed.Count);
        return placed;
    }
}
