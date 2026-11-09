using System.Globalization;
using CorsixNet.Client.Maps;
using CorsixNet.Sim;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>A button of the room edit panel that stands for a key of the build tool.</summary>
internal enum BuildButton
{
    Confirm,
    PickUp,
    Back,
}

/// <summary>
/// The room edit panel while furnishing (CorsixTH's <c>dialogs/place_objects.lua</c> under <c>edit_room.lua</c>): the
/// objects the room still needs, the extra objects it allows with their prices to buy, and the confirm, pick up and
/// take down buttons (Enter, P and Esc).
/// </summary>
internal sealed class RoomEditWindow(BuildRoomTool tool, Action<BuildButton> press) : UiWindow
{
    public override string Title => tool.RoomType?.Name ?? "Room";

    public override bool IsDone(IWindowHost host) => !tool.IsFurnishing;

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var simulation = host.Simulation;
        if (tool.RoomType is not { } room)
        {
            yield break;
        }

        var missing = tool.Missing(simulation);
        yield return WindowCell.Line(missing.Count == 0 ? "Everything needed is in place." : "Still needed:", highlight: missing.Count == 0);
        foreach (var (objectType, count) in missing)
        {
            yield return WindowRow.Of("  " + (simulation.Content.FindObject(objectType)?.Name ?? objectType), WindowCell.Number(count.ToString(CultureInfo.InvariantCulture)));
        }

        if (room.AdditionalObjects is { Count: > 0 } extras)
        {
            yield return WindowRow.Gap;
            yield return WindowCell.Line("Buy more:", dim: true);
            foreach (var definition in extras.Select(simulation.Content.FindObject).OfType<ObjectDefinition>())
            {
                var price = simulation.Rules.ObjectCost(definition.Id);
                yield return WindowRow.Of(
                    "  " + definition.Name,
                    WindowCell.Number(MoneyText.Format(price)),
                    WindowCell.Button("Buy", () =>
                    {
                        tool.PlaceExtra(definition);
                        host.PlaySound("selectx.wav");
                    }, simulation.Hospitals[host.Player].Balance >= price));
            }
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(
            WindowCell.Button(string.Create(CultureInfo.InvariantCulture, $"Confirm {MoneyText.Format(simulation.Rules.RoomCost(room.Id))}"), () => press(BuildButton.Confirm), tool.CanConfirm(simulation)),
            WindowCell.Button(tool.IsPickingUp ? "Stop picking up" : "Pick up", () => press(BuildButton.PickUp), highlight: tool.IsPickingUp),
            WindowCell.Button("Take down", () => press(BuildButton.Back)));
    }
}

/// <summary>
/// The furnish corridor window (CorsixTH's <c>dialogs/furnish_corridor.lua</c>): the objects for the corridors with
/// their prices and how many the hospital has; Buy puts one on the cursor to place.
/// </summary>
internal sealed class FurnishCorridorWindow(Action<ObjectDefinition> buy) : UiWindow
{
    public override string Title => "Furnish corridor";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var simulation = host.Simulation;
        var balance = simulation.Hospitals[host.Player].Balance;
        yield return WindowRow.Of("Object", WindowCell.Number("Price"), WindowCell.Number("Have"), string.Empty);
        foreach (var definition in simulation.Content.Objects.Where(o => o.CorridorObject).OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
        {
            var price = simulation.Rules.ObjectCost(definition.Id);
            yield return WindowRow.Of(
                definition.Name,
                WindowCell.Number(MoneyText.Format(price)),
                WindowCell.Number(Owned(simulation, host.Player, definition.Id).ToString(CultureInfo.InvariantCulture)),
                WindowCell.Button("Buy", () =>
                {
                    host.Close(this);
                    buy(definition);
                }, balance >= price));
        }
    }

    private static int Owned(Simulation simulation, int player, string objectType) =>
        simulation.Entities.OfType<RoomObject>().Count(o => o.ObjectType == objectType && o.RoomId == 0
            && simulation.Map.GetParcelOwner(simulation.Map[o.Tile.X, o.Tile.Y].Parcel) == player + 1);
}

/// <summary>
/// The bottom panel (CorsixTH's <c>dialogs/bottom_panel.lua</c>): the balance and the date, and buttons for building,
/// furnishing the corridors, hiring, the faxes and each of the fullscreen dialogs.
/// </summary>
internal sealed class BottomPanel(Action<string> action) : UiWindow
{
    public override string Title => string.Empty;

    public override bool IsPinned => true;

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var simulation = host.Simulation;
        var hospital = simulation.Hospitals[host.Player];
        var faxes = FaxWindow.Pending(simulation, host.Player).Count();
        yield return WindowRow.Of(
            new WindowCell(MoneyText.Format(hospital.Balance), Highlight: true),
            WindowCell.Button("Build", () => action("rooms")),
            WindowCell.Button("Corridor", () => action("corridor")),
            WindowCell.Button("Hire", () => host.Open(new HireWindow())),
            WindowCell.Button(faxes > 0 ? string.Create(CultureInfo.InvariantCulture, $"Faxes ({faxes})") : "Faxes", () => host.Open(new FaxWindow()), highlight: faxes > 0),
            WindowCell.Button("Bank", () => host.Open(new BankManagerWindow())),
            WindowCell.Button("Staff", () => host.Open(new StaffManagementWindow())),
            WindowCell.Button("Town", () => host.Open(new TownMapWindow())),
            WindowCell.Button("Casebook", () => host.Open(new CasebookWindow())),
            WindowCell.Button("Research", () => host.Open(new ResearchWindow())),
            WindowCell.Button("Status", () => host.Open(new ProgressReportWindow())),
            WindowCell.Button("Charts", () => host.Open(new GraphWindow())),
            WindowCell.Button("Policy", () => host.Open(new PolicyWindow())));
    }
}
