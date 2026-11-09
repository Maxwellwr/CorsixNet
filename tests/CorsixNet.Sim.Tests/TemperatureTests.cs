using CorsixNet.Sim.Maps;

namespace CorsixNet.Sim.Tests;

/// <summary>Tile temperatures (CorsixTH's level_map::update_temperatures).</summary>
public class TemperatureTests
{
    [Fact]
    public void TheOutsideAndRadiatorTemperaturesAreCorsixThs()
    {
        Assert.Equal(5373, TileTemperatures.OutsideTemperature(1)); // 4.1 °C
        Assert.Equal(22216, TileTemperatures.OutsideTemperature(7)); // 16.95 °C
        Assert.Equal(16383, TileTemperatures.RadiatorTemperature(0));
        Assert.Equal(26214, TileTemperatures.RadiatorTemperature(5));
        Assert.Equal(36044, TileTemperatures.RadiatorTemperature(10));
    }

    [Fact]
    public void OutsideTilesDriftOnePercentTowardsTheAirEachHour()
    {
        var simulation = new Simulation(TestMaps.FromRows("...", "..."), seed: 1);
        Assert.Equal(TileTemperatures.Start, simulation.Temperatures[1, 1]);

        simulation.Step();

        // (8192 × 99 + 5373) / 100: the neighbours are as warm, so only January's air counts.
        Assert.All(Tiles(simulation.Map), tile => Assert.Equal(8163, simulation.Temperatures[tile.X, tile.Y]));
    }

    [Fact]
    public void ARadiatorWarmsItsTileAndTheHeatSpreads()
    {
        var map = Inside(TestMaps.FromRows(".....", "....."));
        var temperatures = new TileTemperatures(map.Width, map.Height);
        var radiators = new ushort[map.Width * map.Height];
        radiators[0] = TileTemperatures.RadiatorTemperature(5);

        temperatures.Update(map, TileTemperatures.OutsideTemperature(1), radiators);

        // The radiator's tile goes half-way to 26214 after a quarter towards its neighbours (all 8192).
        Assert.Equal((8192 + 26214) / 2, temperatures[0, 0]);
        // Others lose 0.1 %.
        Assert.Equal(8183, temperatures[4, 1]);

        for (var i = 0; i < 200; i++)
        {
            temperatures.Update(map, TileTemperatures.OutsideTemperature(1), radiators);
        }

        Assert.True(temperatures[1, 0] > temperatures[4, 1]);
        Assert.True(temperatures[0, 0] > temperatures[1, 0]);
    }

    [Fact]
    public void AWallBetweenAHallAndARoomLetsLittleHeatThrough()
    {
        var map = Inside(TestMaps.FromRows("...", "..."));
        TestMaps.AddWestWall(map, 1, 0);
        map[1, 0].Set(TileFlags.Room, true);
        var temperatures = new TileTemperatures(map.Width, map.Height);
        var radiators = new ushort[map.Width * map.Height];
        radiators[0] = TileTemperatures.RadiatorTemperature(10);

        // Warm the radiator's tile a few hours, then compare its neighbours through the wall and through the open edge.
        for (var i = 0; i < 3; i++)
        {
            temperatures.Update(map, TileTemperatures.OutsideTemperature(1), radiators);
        }

        Assert.True(temperatures[0, 1] > temperatures[1, 0]);

        // Without the room flag the wall would weigh as much as an open edge (CorsixTH compares only the flags).
        var open = Inside(TestMaps.FromRows("...", "..."));
        TestMaps.AddWestWall(open, 1, 0);
        var same = new TileTemperatures(open.Width, open.Height);
        for (var i = 0; i < 3; i++)
        {
            same.Update(open, TileTemperatures.OutsideTemperature(1), radiators);
        }

        Assert.True(same[1, 0] > temperatures[1, 0]);
    }

    [Fact]
    public void SavesKeepTheTemperatures()
    {
        var simulation = new Simulation(Inside(TestMaps.FromRows("....", "....")), seed: 1);
        for (var i = 0; i < 30; i++)
        {
            simulation.Step();
        }

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(simulation.Temperatures[2, 1], restored.Temperatures[2, 1]);
        Assert.NotEqual(TileTemperatures.Start, restored.Temperatures[2, 1]);
    }

    private static LevelMap Inside(LevelMap map)
    {
        foreach (var tile in Tiles(map))
        {
            map[tile.X, tile.Y].Set(TileFlags.Hospital, true);
        }

        return map;
    }

    private static IEnumerable<TilePosition> Tiles(LevelMap map)
    {
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                yield return new TilePosition(x, y);
            }
        }
    }
}
