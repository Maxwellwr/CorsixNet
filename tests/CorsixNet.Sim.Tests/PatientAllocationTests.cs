using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;

namespace CorsixNet.Sim.Tests;

/// <summary>Which hospital a new patient goes to.</summary>
public class PatientAllocationTests
{
    [Fact]
    public void HospitalsTakeTurnsWhetherOrNotThePatientComes()
    {
        var simulation = NewSimulation(players: 3);

        var chosen = new List<int>();
        for (var i = 0; i < 7; i++)
        {
            var player = PatientArrivals.ChooseHospital(simulation);
            chosen.Add(player);

            // Without a reception desk nobody comes, but the turn is used up.
            Assert.Null(simulation.Arrivals.SpawnPatient(simulation, player));
        }

        Assert.Equal([0, 1, 2, 0, 1, 2, 0], chosen);
        Assert.Equal([3L, 2L, 2L], simulation.Hospitals.Select(h => h.SpawnAttempts));

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(1, PatientArrivals.ChooseHospital(restored));
    }

    private static Simulation NewSimulation(int players)
    {
        var map = new LevelMap(6, 6, players);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Floor = 1;
                map[x, y].Set(TileFlags.Passable, true);
            }
        }

        // A hospital tile, so that the map has spawn points.
        map[3, 3].Set(TileFlags.Hospital, true);
        map.UpdatePathfinding();
        return new Simulation(map, seed: 2);
    }
}
