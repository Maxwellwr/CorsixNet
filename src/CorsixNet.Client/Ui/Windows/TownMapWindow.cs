using System.Globalization;
using CorsixNet.Client.Maps;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The town map (CorsixTH's <c>dialogs/fullscreen/town_map.lua</c>, F4): the hospital's people and objects counted,
/// every parcel with its owner and price and a button to buy the ones for sale, and the heating with its bill.
/// </summary>
internal sealed class TownMapWindow : UiWindow
{
    public override string Title => "Town map";

    public override string? Hotkey => "F4";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var simulation = host.Simulation;
        var hospital = simulation.Hospitals[host.Player];
        var humanoids = simulation.Entities.OfType<Humanoid>().ToList();
        var objects = simulation.Entities.OfType<RoomObject>().Where(o => simulation.Map[o.Tile.X, o.Tile.Y].Parcel is var parcel && simulation.Map.GetParcelOwner(parcel) == host.Player + 1).ToList();
        yield return WindowRow.Of("Patients", WindowCell.Number(humanoids.Count(h => h.Behaviour is PatientBehaviour && h.HospitalPlayer == host.Player && !h.IsDespawning).ToString(CultureInfo.InvariantCulture)));
        yield return WindowRow.Of("Staff", WindowCell.Number(humanoids.Count(h => h.Profile is not null && h.Employer == host.Player && !h.IsFired).ToString(CultureInfo.InvariantCulture)));
        yield return WindowRow.Of("Plants", WindowCell.Number(objects.Count(o => o.ObjectType == "plant").ToString(CultureInfo.InvariantCulture)));
        yield return WindowRow.Of("Radiators", WindowCell.Number(objects.Count(o => o.ObjectType == "radiator").ToString(CultureInfo.InvariantCulture)));
        yield return WindowRow.Of("Fire extinguishers", WindowCell.Number(objects.Count(o => o.ObjectType == "extinguisher").ToString(CultureInfo.InvariantCulture)));
        yield return WindowRow.Gap;

        yield return WindowCell.Line("Heating", highlight: true);
        yield return WindowRow.Of(
            hospital.IsHeatingBroken ? "The boiler is broken" : string.Create(CultureInfo.InvariantCulture, $"Radiators at {hospital.RadiatorHeat * 10} %"),
            WindowCell.Button("Colder", () => Heat(host, hospital.RadiatorHeat - 1), !hospital.IsHeatingBroken && hospital.RadiatorHeat > Hospital.MinRadiatorHeat).WithTooltip(OriginalTooltips.Get(host, 198, "Turn the heating down")),
            WindowCell.Button("Warmer", () => Heat(host, hospital.RadiatorHeat + 1), !hospital.IsHeatingBroken && hospital.RadiatorHeat < Hospital.MaxRadiatorHeat).WithTooltip(OriginalTooltips.Get(host, 197, "Turn the heating up")));
        yield return WindowRow.Of("Heating bill so far this month", WindowCell.Number(MoneyText.Format((long)hospital.AccruedHeating)));
        yield return WindowRow.Gap;

        yield return WindowCell.Line("Land", highlight: true);
        yield return WindowRow.Of("Parcel", "Owner", WindowCell.Number("Price"), string.Empty);
        for (var parcel = 1; parcel < simulation.Map.ParcelCount; parcel++)
        {
            var owner = simulation.Map.GetParcelOwner(parcel);
            var price = simulation.ParcelPrice(parcel);
            var forSale = simulation.IsParcelPurchasable(parcel, host.Player);
            var number = parcel;
            yield return WindowRow.Of(
                string.Create(CultureInfo.InvariantCulture, $"Parcel {parcel}"),
                owner == host.Player + 1 ? "You" : owner > 0 ? string.Create(CultureInfo.InvariantCulture, $"Player {owner}") : forSale ? "For sale" : "Not for sale yet",
                WindowCell.Number(owner == 0 ? MoneyText.Format(price) : string.Empty),
                forSale
                    ? WindowCell.Button("Buy", () =>
                    {
                        if (price > hospital.Balance)
                        {
                            host.Notice($"Not enough money for parcel {number}: it costs {MoneyText.Format(price)}");
                            host.PlaySound("Wrong2.wav");
                            return;
                        }

                        host.Submit(new PurchaseParcelCommand(number));
                        host.PlaySound("cashreg.wav");
                    })
                    : string.Empty);
        }
    }

    private static void Heat(IWindowHost host, int tenths)
    {
        host.Submit(new SetRadiatorHeatCommand(tenths));
        host.PlaySound("selectx.wav");
    }
}
