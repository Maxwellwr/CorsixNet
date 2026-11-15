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

    [Fact]
    public void WithSeveralHospitalsAParcelGoesToAuctionAndTheHighestBidderGetsItWhenTheHammerFalls()
    {
        var simulation = NewSimulation(10_000, players: 2);
        Assert.Equal(500, LandAuction.Increment(120));

        // Player 1 wishes to buy parcel 2: the auction starts at its price, nothing is bought yet.
        simulation.Schedule(0, new PurchaseParcelCommand(2));
        simulation.Step();
        var auction = Assert.IsType<LandAuction>(simulation.Auction);
        Assert.Equal((2, 120L, 0, 48L), (auction.Parcel, auction.Bid, auction.Leader, auction.HammerHour));
        Assert.Equal(0, simulation.Map.GetParcelOwner(2));

        // The leader cannot outbid themself; player 2 bids 500 more and the hammer waits two days again.
        simulation.Schedule(0, new BidForParcelCommand(2), tick: 10);
        simulation.Schedule(1, new BidForParcelCommand(2), tick: 10);
        RunTo(simulation, 11);
        Assert.Equal((620L, 1, 58L), (auction.Bid, auction.Leader, auction.HammerHour));

        // A second wish to buy waits for this auction to end.
        simulation.Schedule(0, new PurchaseParcelCommand(3), tick: 11);
        RunTo(simulation, 58);
        Assert.Same(auction, simulation.Auction);

        simulation.Step();
        Assert.Null(simulation.Auction);
        Assert.Equal(2, simulation.Map.GetParcelOwner(2));
        Assert.Equal(10_000 - 620, simulation.Hospitals[1].Balance);
        Assert.Equal(10_000, simulation.Hospitals[0].Balance);
        Assert.Equal(new AuctionResult(2, 1, 620), simulation.LastAuction);
    }

    [Fact]
    public void ABidTooDearForTheBidderIsNotMadeAndAnAuctionIsSaved()
    {
        var simulation = NewSimulation(600, players: 2);
        simulation.Schedule(0, new PurchaseParcelCommand(2));
        simulation.Schedule(1, new BidForParcelCommand(2));
        simulation.Step();
        Assert.Equal(0, simulation.Auction!.Leader);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal((2, 120L, 0), (restored.Auction!.Parcel, restored.Auction.Bid, restored.Auction.Leader));
    }

    private static void RunTo(Simulation simulation, long tick)
    {
        while (simulation.Tick < tick)
        {
            simulation.Step();
        }
    }

    /// <summary>
    /// Parcel 1 (the player's) in the middle, parcel 2 at the top touching the outside, parcel 3 enclosed by parcel 1,
    /// and parcel 4 cut off by walls (impassable tiles).
    /// </summary>
    private static Simulation NewSimulation(int startCash, int players = 1)
    {
        var map = TestMaps.FromRows([.. Rows.Select(row => new string([.. row.Select(c => c == '#' ? '#' : '.')]))]);
        map.SetPlayerCount(players);
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
