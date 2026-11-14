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
            // UIEditRoom:purchaseItems lists only what the level has and research has discovered.
            var research = simulation.Hospitals[host.Player].Research;
            foreach (var definition in extras.Select(simulation.Content.FindObject).OfType<ObjectDefinition>().Where(o => research.CanBuy(simulation.Rules, o.Id)))
            {
                // CorsixTH's place objects list: how many are bought and still to place, one more with each Buy.
                var price = research.ObjectCost(simulation.Rules, definition.Id);
                var toPlace = tool.ExtrasToPlace(definition.Id);
                yield return WindowRow.Of(
                    "  " + definition.Name,
                    WindowCell.Number(toPlace > 0 ? toPlace.ToString(CultureInfo.InvariantCulture) : string.Empty),
                    WindowCell.Number(MoneyText.Format(price)),
                    WindowCell.Button("Buy", () =>
                    {
                        tool.PlaceExtra(definition);
                    }, simulation.Hospitals[host.Player].Balance >= price * (toPlace + 1)));
            }
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(
            WindowCell.Button(tool.IsEditing ? "Confirm" : string.Create(CultureInfo.InvariantCulture, $"Confirm {MoneyText.Format(simulation.Rules.RoomCost(room.Id))}"), () => press(BuildButton.Confirm), tool.CanConfirm(simulation), highlight: Tutorial.Lit("confirm")),
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
                }, balance >= price, highlight: Tutorial.Lit("buy:" + definition.Id)));
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
        // CorsixTH's tooltip.toolbar texts, with the keys from the hotkeys file.
        string Tip(string text, string? action = null, string? key = null) =>
            (action is null ? key : host.HotkeyText(action) ?? key) is { } shown ? $"{text} ({shown})" : text;
        yield return WindowRow.Of(
            new WindowCell(MoneyText.Format(hospital.Balance), Highlight: true) { Tooltip = "Balance" },
            WindowCell.Button("Build", () => action("rooms"), highlight: Tutorial.Lit("build")) with { Tooltip = Tip("Build rooms", key: "B") },
            WindowCell.Button("Corridor", () => action("corridor"), highlight: Tutorial.Lit("corridor")) with { Tooltip = Tip("Furnish corridor") },
            WindowCell.Button("Edit room", () => action("edit")) with { Tooltip = Tip("Edit rooms/items") },
            WindowCell.Button("Hire", () => host.Open(new HireWindow()), highlight: Tutorial.Lit("hire")) with { Tooltip = Tip("Hire staff", "ingame_panel_hireStaff") },
            WindowCell.Button(faxes > 0 ? string.Create(CultureInfo.InvariantCulture, $"Faxes ({faxes})") : "Faxes", () => host.Open(new FaxWindow()), highlight: faxes > 0) with { Tooltip = Tip("Faxes waiting for an answer", "ingame_openFirstMessage") },
            WindowCell.Button("Bank", () => host.Open(new BankManagerWindow())) with { Tooltip = Tip("Bank Manager", "ingame_panel_bankManager") },
            WindowCell.Button("Staff", () => host.Open(new StaffManagementWindow())) with { Tooltip = Tip("Staff management", "ingame_panel_staffManage") },
            WindowCell.Button("Town", () => host.Open(new TownMapWindow())) with { Tooltip = Tip("Town map", "ingame_panel_townMap") },
            WindowCell.Button("Casebook", () => host.Open(new CasebookWindow())) with { Tooltip = Tip("Drug casebook", "ingame_panel_casebook") },
            WindowCell.Button("Research", () => host.Open(new ResearchWindow())) with { Tooltip = Tip("Research", "ingame_panel_research") },
            WindowCell.Button("Status", () => host.Open(new ProgressReportWindow())) with { Tooltip = Tip("Status", "ingame_panel_status") },
            WindowCell.Button("Charts", () => host.Open(new GraphWindow())) with { Tooltip = Tip("Charts", "ingame_panel_charts") },
            WindowCell.Button("Policy", () => host.Open(new PolicyWindow())) with { Tooltip = Tip("Policy", "ingame_panel_policy") },
            WindowCell.Button("Machines", () => host.Open(new MachineMenuWindow())) with { Tooltip = Tip("Machine menu", "ingame_panel_machineMenu") },
            WindowCell.Button("Messages", () => host.Open(new AdviserHistoryWindow())) with { Tooltip = Tip("The adviser's messages", "ingame_panel_adviserHistory") });

    }

    /// <summary>Characters on one line of the adviser's speech before it wraps.</summary>
    public const int WrapLength = 110;

    /// <summary>Breaks text into lines of at most <paramref name="length"/> characters at spaces (longer words stay whole).</summary>
    public static IReadOnlyList<string> Wrap(string text, int length)
    {
        var lines = new List<string>();
        var line = new System.Text.StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > length)
            {
                lines.Add(line.ToString());
                line.Clear();
            }

            line.Append(line.Length > 0 ? " " : string.Empty).Append(word);
        }

        lines.Add(line.ToString());
        return lines;
    }
}
