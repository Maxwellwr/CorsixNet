using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;

namespace CorsixNet.Sim.Tests;

/// <summary>When VIPs come (CorsixTH's World:_generateNextVipDate, math.t_random) and where they queue (Queue:push).</summary>
public class VipTests
{
    [Fact]
    public void VipsComeWithinThirtyDaysOfTheLevelsAverage()
    {
        var random = new SimRandom(5);
        var days = Enumerable.Range(0, 2000).Select(_ => VipVisits.DaysUntilNext(random, 150)).ToList();

        Assert.All(days, day => Assert.InRange(day, 120, 180));
        Assert.InRange(days.Average(), 145, 155);

        // Most come near the average: the distribution is a triangle.
        Assert.True(days.Count(day => Math.Abs(day - 150) <= 10) > days.Count(day => Math.Abs(day - 150) > 20));

        // At least a day away, however small the setting.
        Assert.All(Enumerable.Range(0, 200).Select(_ => VipVisits.DaysUntilNext(random, 0)), day => Assert.InRange(day, 1, 31));
    }

    [Fact]
    public void TheTriangularDistributionStaysWithinItsBounds()
    {
        var random = new SimRandom(9);
        for (var i = 0; i < 1000; i++)
        {
            Assert.InRange(VipVisits.TriangularRandom(random, 0.75m, 1m, 1.25m), 0.75m, 1.25m);
        }
    }

    [Fact]
    public void AVipGoesAheadOfPatientsInAQueue()
    {
        var map = new LevelMap(10, 10, playerCount: 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Set(TileFlags.Passable, true);
            }
        }

        var simulation = new Simulation(map, seed: 1, rules: new LevelRules(10_000, [], []));
        var queue = new HumanoidQueue(new TilePosition(5, 5), new TilePosition(5, 4), 20);
        var first = Patient(simulation);
        var second = Patient(simulation);
        queue.Push(simulation, first);
        queue.Push(simulation, second);

        var vip = simulation.AddEntity(id => Humanoid.CreateVip(id, new TilePosition(1, 1), 0));
        vip.Behaviour = new VipBehaviour(1, 10);
        queue.Push(simulation, vip);
        var third = Patient(simulation);
        queue.Push(simulation, third);

        Assert.Equal([vip.Id, first.Id, second.Id, third.Id], queue.Members);
    }

    [Fact]
    public void AVipCountsEachPieceOfLitterWithinEightStepsOnce()
    {
        // Vip:tickDay's findObjectNear(self, "litter", 8): a wall at x = 8 keeps the litter beyond it out of sight.
        var map = TestMaps.FromRows(
            "..................",
            "........#.........",
            "........#.........");
        var simulation = new Simulation(map, seed: 1, rules: new LevelRules(10_000, [], []));
        var vip = simulation.AddEntity(id => Humanoid.CreateVip(id, new TilePosition(1, 1), 0));
        var behaviour = new VipBehaviour(1, 10);
        vip.Behaviour = behaviour;

        Litter.Drop(simulation, 0, LitterType.SodaCan, new TilePosition(3, 1), isMirrored: false);
        Litter.Drop(simulation, 0, LitterType.Puke, new TilePosition(1, 2), isMirrored: false);
        Litter.Drop(simulation, 0, LitterType.Paper, new TilePosition(16, 1), isMirrored: false);
        Litter.Drop(simulation, 0, LitterType.Banana, new TilePosition(9, 2), isMirrored: false);

        behaviour.OnEndDay(simulation, vip);
        behaviour.OnEndDay(simulation, vip);
        Assert.Equal(1, behaviour.RubbishFound);
        Assert.Equal(1, behaviour.VomitFound);

        // New litter is counted on the next day; what was seen and saved is not counted again after loading.
        Litter.Drop(simulation, 0, LitterType.Bottle, new TilePosition(2, 0), isMirrored: false);
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());

        var restoredVip = restored.Entities.OfType<Humanoid>().Single(h => h.Behaviour is VipBehaviour);
        var restoredBehaviour = (VipBehaviour)restoredVip.Behaviour!;
        restoredBehaviour.OnEndDay(restored, restoredVip);
        Assert.Equal(2, restoredBehaviour.RubbishFound);
        Assert.Equal(1, restoredBehaviour.VomitFound);
    }

    private static Humanoid Patient(Simulation simulation)
    {
        var patient = simulation.AddEntity(id => Humanoid.CreateRandomPatient(id, new TilePosition(1, 1), simulation.Random, 0));
        patient.Behaviour = new PatientBehaviour();
        return patient;
    }
}
