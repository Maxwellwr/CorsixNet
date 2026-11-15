// The bidding screen of a land auction in a game of several hospitals (the original's network game, NETPLAY.TXT:
// "all players automatically go to the bidding screen ... make a bid by left clicking on the Tick button").

using System.Globalization;
using CorsixNet.Sim.Commands;
using Microsoft.Xna.Framework;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The land auction under way: the plot, the bid standing and whose it is, when the hammer falls, and the button that
/// bids the next step. It opens for every player when an auction starts and closes when the hammer falls.
/// </summary>
internal sealed class AuctionWindow : UiWindow
{
    public override string Title => UiText.Get("corsixnet.auction.caption", "Land auction");

    public override Vector2? DefaultPosition => new(0.5f, 0.3f);

    public override bool IsDone(IWindowHost host) => host.Simulation.Auction is null;

    public override bool IsSameAs(UiWindow other) => other is AuctionWindow;

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var simulation = host.Simulation;
        if (simulation.Auction is not { } auction)
        {
            yield break;
        }

        yield return WindowCell.Line(UiText.Format("corsixnet.auction.lot", "Plot %1%: bid $%2% by %3%", auction.Parcel, auction.Bid, ProgressReportScreen.HospitalName(host, auction.Leader)), highlight: true);
        var hours = Math.Max(0, auction.HammerHour - simulation.Hours);
        yield return WindowCell.Line(UiText.Format("corsixnet.auction.hammer", "The hammer falls in %1% hours", hours.ToString(CultureInfo.InvariantCulture)));
        var canBid = host.Player != auction.Leader && host.Player < simulation.Hospitals.Count
            && simulation.IsParcelPurchasable(auction.Parcel, host.Player) && auction.NextBid <= simulation.Hospitals[host.Player].Balance;
        yield return WindowRow.Of(WindowCell.Button(UiText.Format("corsixnet.auction.bid", "Bid $%1%", auction.NextBid), () => host.Submit(new BidForParcelCommand(auction.Parcel)), canBid));
    }
}
