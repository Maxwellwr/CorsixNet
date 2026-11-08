using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Sim.Tests;

/// <summary>Buying parcels of land.</summary>
public class LandPurchaseTests
{
    [Fact]
    public void AParcelIsForSaleWhenItTouchesTheOutsideOrTheBuyersLand()
    {
        var simulation = NewSimulation(10_000);

        Assert.False(simulation.IsParcelPurchasable(0, 0)); // the outside
        Assert.False(simulation.IsParcelPurchasable(1, 0)); // owned already
        Assert.True(simulation.IsParcelPurchasable(2, 0)); // touches the outside
        Assert.True(simulation.IsParcelPurchasable(3, 0)); // touches parcel 1 only
        Assert.False(simulation.IsParcelPurchasable(4, 0)); // touches nothing by passable tiles
        Assert.False(simulation.IsParcelPurchasable(5, 0)); // no such parcel
    }

    [Fact]
    public void BuyingAParcelCostsItsTilesAndAddsToTheValue()
    {
        var simulation = NewSimulation(10_000);
        var hospital = simulation.Hospitals[0];
        var value = hospital.Value;
        Assert.Equal(4 * 30, simulation.ParcelPrice(2));

        simulation.Schedule(0, new PurchaseParcelCommand(2));
        simulation.Step();

        Assert.Equal(1, simulation.Map.GetParcelOwner(2));
        Assert.Equal(10_000 - 120, hospital.Balance);
        Assert.Equal(value + 120, hospital.Value);
        Assert.Equal(new Transaction(0, TransactionKind.BuyLand, "2", -120, 9880), hospital.Transactions[0]);
        Assert.False(simulation.IsParcelPurchasable(2, 0));
    }

    [Fact]
    public void AParcelTooDearOrNotForSaleIsNotBought()
    {
        var simulation = NewSimulation(100);

        simulation.Schedule(0, new PurchaseParcelCommand(2));
        simulation.Schedule(0, new PurchaseParcelCommand(4));
        simulation.Step();

        Assert.Equal(0, simulation.Map.GetParcelOwner(2));
        Assert.Equal(0, simulation.Map.GetParcelOwner(4));
        Assert.Empty(simulation.Hospitals[0].Transactions);
    }

    [Fact]
    public void SavesKeepWhichParcelsTouch()
    {
        var simulation = NewSimulation(10_000);

        // Blocking the tiles afterwards does not change which parcels touch: that goes by the map as it began.
        simulation.Map[2, 2].Set(TileFlags.Passable, false);
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.True(restored.IsParcelPurchasable(3, 0));
        Assert.False(restored.IsParcelPurchasable(4, 0));
    }

    /// <summary>
    /// Parcel 1 (the player's) in the middle, parcel 2 at the top touching the outside, parcel 3 enclosed by parcel 1,
    /// and parcel 4 cut off by walls (impassable tiles).
    /// </summary>
    private static Simulation NewSimulation(int startCash)
    {
        var map = TestMaps.FromRows([.. Rows.Select(row => new string([.. row.Select(c => c == '#' ? '#' : '.')]))]);
        map.ParcelCount = 5;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                ref var tile = ref map[x, y];
                tile.Parcel = Rows[y][x] switch
                {
                    '1' => 1,
                    '2' => 2,
                    '3' => 3,
                    '4' => 4,
                    _ => 0,
                };
            }
        }

        // Parcel 4's tiles are walled off.
        map[3, 4].Set(TileFlags.Passable, false);
        map[4, 4].Set(TileFlags.Passable, false);
        return new Simulation(map, seed: 1, rules: new LevelRules(startCash, [], [], landCostPerTile: 30));
    }

    private static readonly string[] Rows =
    [
        "......",
        ".2222.",
        ".1311.",
        ".1111.",
        ".1#44.",
        "...##.",
    ];
}
