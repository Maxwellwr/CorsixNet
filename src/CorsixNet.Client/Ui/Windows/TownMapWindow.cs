using System.Globalization;
using CorsixNet.Client.Maps;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The town map (CorsixTH's <c>dialogs/fullscreen/town_map.lua</c>, F4): the hospital's people and objects counted,
/// every parcel with its owner and price and a button to buy the ones for sale, and the heating with its bill.
/// </summary>
internal sealed class TownMapWindow : UiWindow
{
    public override string Title => UiText.Get("corsixnet.menu.town_map", "Town map");

    public override string? Hotkey => "F4";

    private readonly TownMapScreen _screen = new();

    /// <summary>The original's look (<see cref="TownMapScreen"/>) where its graphics are there; else the table below.</summary>
    public override bool IsFullscreen(IWindowHost host) => TownMapScreen.IsAvailable(host);

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse) => _screen.Draw(batch, host, origin, mouse);

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous) => _screen.Mouse(host, this, local, mouse, previous);

    public override string? FullscreenTooltip(IWindowHost host, Point local) => TownMapScreen.Tooltip(host, local);
    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var simulation = host.Simulation;
        var hospital = simulation.Hospitals[host.Player];
        var humanoids = simulation.Entities.OfType<Humanoid>().ToList();
        var objects = simulation.Entities.OfType<RoomObject>().Where(o => simulation.Map[o.Tile.X, o.Tile.Y].Parcel is var parcel && simulation.Map.GetParcelOwner(parcel) == host.Player + 1).ToList();
        yield return WindowRow.Of(UiText.Get("corsixnet.town_map.patients", "Patients"), WindowCell.Number(humanoids.Count(h => h.Behaviour is PatientBehaviour && h.HospitalPlayer == host.Player && !h.IsDespawning).ToString(CultureInfo.InvariantCulture)));
        yield return WindowRow.Of(UiText.Get("corsixnet.menu.staff", "Staff"), WindowCell.Number(humanoids.Count(h => h.Profile is not null && h.Employer == host.Player && !h.IsFired).ToString(CultureInfo.InvariantCulture)));
        yield return WindowRow.Of(UiText.Get("corsixnet.staff.plants", "Plants"), WindowCell.Number(objects.Count(o => o.ObjectType == "plant").ToString(CultureInfo.InvariantCulture)));
        yield return WindowRow.Of(UiText.Get("corsixnet.town_map.radiators", "Radiators"), WindowCell.Number(objects.Count(o => o.ObjectType == "radiator").ToString(CultureInfo.InvariantCulture)));
        yield return WindowRow.Of(UiText.Get("corsixnet.town_map.fire_extinguishers", "Fire extinguishers"), WindowCell.Number(objects.Count(o => o.ObjectType == "extinguisher").ToString(CultureInfo.InvariantCulture)));
        yield return WindowRow.Gap;

        yield return WindowCell.Line(UiText.Get("corsixnet.town_map.heating", "Heating"), highlight: true);
        yield return WindowRow.Of(
            hospital.IsHeatingBroken ? UiText.Get("corsixnet.town_map.boiler_broken", "The boiler is broken") : UiText.Format("corsixnet.town_map.radiators_at", "Radiators at %1% %%", hospital.RadiatorHeat * 10),
            WindowCell.Button(UiText.Get("corsixnet.town_map.colder", "Colder"), () => Heat(host, hospital.RadiatorHeat - 1), !hospital.IsHeatingBroken && hospital.RadiatorHeat > Hospital.MinRadiatorHeat).WithTooltip(OriginalTooltips.Get(host, 198, "Turn the heating down")),
            WindowCell.Button(UiText.Get("corsixnet.town_map.warmer", "Warmer"), () => Heat(host, hospital.RadiatorHeat + 1), !hospital.IsHeatingBroken && hospital.RadiatorHeat < Hospital.MaxRadiatorHeat).WithTooltip(OriginalTooltips.Get(host, 197, "Turn the heating up")));
        yield return WindowRow.Of(UiText.Get("corsixnet.town_map.heating_bill", "Heating bill so far this month"), WindowCell.Number(MoneyText.Format((long)hospital.AccruedHeating)));
        yield return WindowRow.Gap;

        yield return WindowCell.Line(UiText.Get("corsixnet.town_map.land", "Land"), highlight: true);
        yield return WindowRow.Of(UiText.Get("corsixnet.town_map.parcel", "Parcel"), UiText.Get("corsixnet.town_map.owner", "Owner"), WindowCell.Number(UiText.Get("corsixnet.build.price", "Price")), string.Empty);
        for (var parcel = 1; parcel < simulation.Map.ParcelCount; parcel++)
        {
            var owner = simulation.Map.GetParcelOwner(parcel);
            var price = simulation.ParcelPrice(parcel);
            var forSale = simulation.IsParcelPurchasable(parcel, host.Player);
            var number = parcel;
            yield return WindowRow.Of(
                UiText.Format("corsixnet.staff.parcel", "Parcel %1%", parcel),
                owner == host.Player + 1 ? UiText.Get("corsixnet.town_map.you", "You") : owner > 0 ? UiText.Format("corsixnet.town_map.player", "Player %1%", owner) : forSale ? UiText.Get("town_map.for_sale", "For sale") : UiText.Get("corsixnet.town_map.not_for_sale_yet", "Not for sale yet"),
                WindowCell.Number(owner == 0 ? MoneyText.Format(price) : string.Empty),
                forSale
                    ? WindowCell.Button(UiText.Get("corsixnet.build.buy", "Buy"), () =>
                    {
                        if (price > hospital.Balance)
                        {
                            host.Notice(UiText.Format("corsixnet.town_map.cannot_afford", "Not enough money for parcel %1%: it costs %2%", number, MoneyText.Format(price)));
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
    }
}
