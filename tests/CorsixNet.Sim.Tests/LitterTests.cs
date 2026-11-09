using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Sim.Tests;

/// <summary>Litter on the floor and which kind a tile keeps (CorsixTH's objects/litter.lua and World:newLitter).</summary>
public class LitterTests
{
    [Fact]
    public void AHigherRankedLitterReplacesALowerOneButNotTheOtherWayRound()
    {
        var simulation = new Simulation(new LevelMap(6, 6, playerCount: 1), seed: 1);
        var tile = new TilePosition(2, 2);

        var can = Litter.Drop(simulation, 0, LitterType.SodaCan, tile, isMirrored: false);
        Assert.NotNull(can);

        // Rubbish of the same rank does not displace it.
        Assert.Null(Litter.Drop(simulation, 0, LitterType.Paper, tile, isMirrored: true));

        var puke = Litter.Drop(simulation, 0, LitterType.Puke, tile, isMirrored: false);
        Assert.NotNull(puke);
        Assert.True(can!.IsRemoved);
        Assert.Null(Litter.Drop(simulation, 0, LitterType.Pee, tile, isMirrored: false));
        Assert.Equal([puke], Litter.At(simulation, tile));

        // Soot cannot be swept; building removes only what can.
        var soot = Litter.Drop(simulation, 0, LitterType.SootFloor, new TilePosition(3, 3), isMirrored: false)!;
        Litter.RemoveAllAt(simulation, tile);
        Litter.RemoveAllAt(simulation, soot.Tile);
        Assert.True(puke!.IsRemoved);
        Assert.False(soot.IsRemoved);
        Assert.True(puke.IsVomitInducing);
        Assert.False(soot.IsCleanable);

        // Removed litter leaves at the end of the tick; the rest is saved.
        simulation.Step();
        Assert.Equal([soot.Id], simulation.Entities.OfType<Litter>().Select(l => l.Id));
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(LitterType.SootFloor, Assert.Single(restored.Entities.OfType<Litter>()).Type);
    }
}
