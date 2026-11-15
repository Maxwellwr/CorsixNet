// Land auctions in a game of several hospitals, after the original's network game (NETPLAY.TXT, "Buying Plots": a player
// wishing to buy a plot starts the bidding, every player may bid, and when the hammer falls the plot goes to the
// highest bidder). CorsixTH has no network game (its town_map.lua leaves auctions as a TODO), so the steps and the
// time the hammer waits are our own.

using System.Globalization;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Economy;

/// <summary>
/// The auction of a parcel: the bid standing, who made it, and the hour the hammer falls unless someone bids higher.
/// </summary>
public sealed class LandAuction
{
    /// <summary>How long after the last bid the hammer falls: two days of the game.</summary>
    public const int HoursToHammer = 48;

    /// <summary>The smallest step a bid goes up by.</summary>
    public const int MinimumIncrement = 500;

    internal LandAuction(int parcel, long bid, int leader, long hammerHour, long startPrice)
    {
        Parcel = parcel;
        Bid = bid;
        Leader = leader;
        HammerHour = hammerHour;
        StartPrice = startPrice;
    }

    /// <summary>The parcel up for auction.</summary>
    public int Parcel { get; }

    /// <summary>The bid standing.</summary>
    public long Bid { get; private set; }

    /// <summary>The player (0-based) whose bid stands.</summary>
    public int Leader { get; private set; }

    /// <summary>The hour the hammer falls, unless someone bids before.</summary>
    public long HammerHour { get; private set; }

    /// <summary>The parcel's price when the auction began, the first bid.</summary>
    public long StartPrice { get; }

    /// <summary>What a new bid must be: the bid standing raised by a tenth of the starting price, at least 500, in hundreds.</summary>
    public long NextBid => Bid + Increment(StartPrice);

    /// <summary>A tenth of the price rounded up to a hundred, at least <see cref="MinimumIncrement"/>.</summary>
    public static long Increment(long price) => Math.Max(MinimumIncrement, (price / 10 + 99) / 100 * 100);

    /// <summary>
    /// A player wishing to buy a parcel nobody owns: alone in the game they buy it at once (<see cref="Simulation.PurchaseParcel"/>);
    /// with other hospitals in the game it goes to auction, their wish the first bid at the parcel's price.
    /// </summary>
    internal static void Start(Simulation simulation, int parcel, int player)
    {
        var hospital = simulation.Hospitals[player];
        var price = simulation.ParcelPrice(parcel);
        if (simulation.Auction is not null || price > hospital.Balance)
        {
            return;
        }

        simulation.Auction = new LandAuction(parcel, price, player, simulation.Hours + HoursToHammer, price);
    }

    /// <summary>
    /// A bid: another player who could buy the parcel and has the money for the next bid takes the lead, and the hammer
    /// waits <see cref="HoursToHammer"/> again.
    /// </summary>
    internal static void PlaceBid(Simulation simulation, int parcel, int player)
    {
        if (simulation.Auction is not { } auction || auction.Parcel != parcel || auction.Leader == player
            || player < 0 || player >= simulation.Hospitals.Count || !simulation.IsParcelPurchasable(parcel, player))
        {
            return;
        }

        var bid = auction.NextBid;
        if (bid > simulation.Hospitals[player].Balance)
        {
            return;
        }

        auction.Bid = bid;
        auction.Leader = player;
        auction.HammerHour = simulation.Hours + HoursToHammer;
    }

    /// <summary>
    /// Each hour: when the hammer falls the parcel goes to the highest bidder for their bid, if it is still for sale and
    /// they can still pay; otherwise nobody gets it.
    /// </summary>
    internal static void Tick(Simulation simulation)
    {
        if (simulation.Auction is not { } auction || simulation.Hours < auction.HammerHour)
        {
            return;
        }

        simulation.Auction = null;
        var winner = simulation.Hospitals[auction.Leader];
        if (simulation.Map.GetParcelOwner(auction.Parcel) == 0 && auction.Bid <= winner.Balance)
        {
            simulation.SetParcelOwner(auction.Parcel, auction.Leader + 1);
            winner.Spend(simulation.Hours, checked((int)auction.Bid), TransactionKind.BuyLand, auction.Parcel.ToString(CultureInfo.InvariantCulture));
        }

        simulation.LastAuction = new AuctionResult(auction.Parcel, auction.Leader, auction.Bid);
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(Parcel);
        writer.WriteInt64(Bid);
        writer.WriteInt32(Leader);
        writer.WriteInt64(HammerHour);
        writer.WriteInt64(StartPrice);
    }

    internal static LandAuction Read(SimReader reader)
    {
        var parcel = reader.ReadInt32();
        var bid = reader.ReadInt64();
        var leader = reader.ReadInt32();
        var hammer = reader.ReadInt64();
        var start = reader.ReadInt64();
        return new LandAuction(parcel, bid, leader, hammer, start);
    }
}

/// <summary>How the last auction ended: the parcel, the highest bidder and their bid.</summary>
public sealed record AuctionResult(int Parcel, int Winner, long Bid)
{
    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(Parcel);
        writer.WriteInt32(Winner);
        writer.WriteInt64(Bid);
    }

    internal static AuctionResult Read(SimReader reader) => new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt64());
}
