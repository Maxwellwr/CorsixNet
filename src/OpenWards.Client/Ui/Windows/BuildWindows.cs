using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenWards.Client.Maps;
using OpenWards.Sim;
using OpenWards.Sim.Definitions;
using OpenWards.Sim.Entities;

namespace OpenWards.Client.Ui.Windows;

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
    public override string Title => tool.RoomType is { } room ? UiText.Room(room) : UiText.Get("openwards.build.room", "Room");

    public override bool IsDone(IWindowHost host) => !tool.IsFurnishing;

    private readonly RoomEditScreen _screen = new(tool, press);
    private int _lines;

    /// <summary>The original's look (<see cref="RoomEditScreen"/>) where its graphics are there; else the table below.</summary>
    public override bool IsFullscreen(IWindowHost host)
    {
        if (!RoomEditScreen.IsAvailable(host))
        {
            return false;
        }

        _lines = _screen.Lines(host).Count;
        return true;
    }

    public override Point FullscreenSize => new(RoomEditScreen.Width, RoomEditScreen.Height(_lines));

    /// <summary>UIPlaceObjects:setDefaultPosition(0.9, 0.1).</summary>
    public override Vector2? FullscreenPosition => new(0.9f, 0.1f);

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse) => _screen.Draw(batch, host, origin, mouse);

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous) => _screen.Mouse(host, this, local, mouse, previous);

    public override string? FullscreenTooltip(IWindowHost host, Point local) => RoomEditScreen.Tooltip(host, local);

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var simulation = host.Simulation;
        if (tool.RoomType is not { } room)
        {
            yield break;
        }

        var missing = tool.Missing(simulation);
        yield return WindowCell.Line(missing.Count == 0 ? UiText.Get("openwards.build.all_in_place", "Everything needed is in place.") : UiText.Get("openwards.build.still_needed", "Still needed:"), highlight: missing.Count == 0);
        foreach (var (objectType, count) in missing)
        {
            yield return WindowRow.Of("  " + (UiText.ObjectName(simulation.Content, objectType)), WindowCell.Number(count.ToString(CultureInfo.InvariantCulture)));
        }

        if (room.AdditionalObjects is { Count: > 0 } extras)
        {
            yield return WindowRow.Gap;
            yield return WindowCell.Line(UiText.Get("openwards.build.buy_more", "Buy more:"), dim: true);
            // UIEditRoom:purchaseItems lists only what the level has and research has discovered.
            var research = simulation.Hospitals[host.Player].Research;
            foreach (var definition in extras.Select(simulation.Content.FindObject).OfType<ObjectDefinition>().Where(o => research.CanBuy(simulation.Rules, o.Id)))
            {
                // CorsixTH's place objects list: how many are bought and still to place, one more with each Buy.
                var price = research.ObjectCost(simulation.Rules, definition.Id);
                var toPlace = tool.ExtrasToPlace(definition.Id);
                yield return WindowRow.Of(
                    "  " + UiText.Object(definition),
                    WindowCell.Number(toPlace > 0 ? toPlace.ToString(CultureInfo.InvariantCulture) : string.Empty),
                    WindowCell.Number(MoneyText.Format(price)),
                    WindowCell.Button(UiText.Get("openwards.build.buy", "Buy"), () =>
                    {
                        tool.PlaceExtra(definition);
                    }, simulation.Hospitals[host.Player].Balance >= price * (toPlace + 1)));
            }
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(
            WindowCell.Button(tool.IsEditing ? UiText.Get("tooltip.place_objects_window.confirm", "Confirm") : UiText.Format("openwards.build.confirm_cost", "Confirm %1%", MoneyText.Format(simulation.Rules.RoomCost(room.Id))), () => press(BuildButton.Confirm), tool.CanConfirm(simulation), highlight: Tutorial.Lit("confirm")),
            WindowCell.Button(tool.IsPickingUp ? UiText.Get("openwards.build.stop_picking_up", "Stop picking up") : UiText.Get("tooltip.staff_window.pick_up", "Pick up"), () => press(BuildButton.PickUp), highlight: tool.IsPickingUp),
            WindowCell.Button(UiText.Get("openwards.build.take_down", "Take down"), () => press(BuildButton.Back)));
    }
}

/// <summary>
/// The furnish corridor window (CorsixTH's <c>dialogs/furnish_corridor.lua</c>): the objects for the corridors with
/// their prices and how many the hospital has; Buy puts one on the cursor to place.
/// </summary>
internal sealed class FurnishCorridorWindow(Action<ObjectDefinition> buy) : UiWindow
{
    public override string Title => UiText.Get("openwards.build.furnish_corridor", "Furnish corridor");

    private readonly FurnishCorridorScreen _screen = new();

    /// <summary>Puts several objects on the cursor one after the other (the original's look's confirm); one at a time without it.</summary>
    public Action<IReadOnlyList<ObjectDefinition>>? BuyMany { get; init; }

    /// <summary>The original's look (<see cref="FurnishCorridorScreen"/>) where its graphics are there; else the table below.</summary>
    public override bool IsFullscreen(IWindowHost host) => BuyMany is not null && FurnishCorridorScreen.IsAvailable(host);

    public override Point FullscreenSize => FurnishCorridorScreen.Size;

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse) => _screen.Draw(batch, host, origin, mouse);

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous) => _screen.Mouse(host, this, local, mouse, previous, BuyMany!);

    /// <summary>global_confirm: Enter does what the confirm button does.</summary>
    public override bool TakesKey(string action) => action == "global_confirm";

    public override void FullscreenKey(IWindowHost host, string action) => _screen.Buy(host, this, BuyMany!);

    public override string? FullscreenTooltip(IWindowHost host, Point local) => _screen.Tooltip(host, local);
    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var simulation = host.Simulation;
        var balance = simulation.Hospitals[host.Player].Balance;
        yield return WindowRow.Of(UiText.Get("openwards.build.object", "Object"), WindowCell.Number(UiText.Get("openwards.build.price", "Price")), WindowCell.Number(UiText.Get("openwards.build.have", "Have")), string.Empty);
        foreach (var definition in simulation.Content.Objects.Where(o => o.CorridorObject).OrderBy(o => UiText.Object(o), StringComparer.CurrentCultureIgnoreCase))
        {
            var price = simulation.Rules.ObjectCost(definition.Id);
            yield return WindowRow.Of(
                UiText.Object(definition),
                WindowCell.Number(MoneyText.Format(price)),
                WindowCell.Number(Owned(simulation, host.Player, definition.Id).ToString(CultureInfo.InvariantCulture)),
                WindowCell.Button(UiText.Get("openwards.build.buy", "Buy"), () =>
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
        string Tip(string name, string text, string? action = null, string? key = null)
        {
            var template = UiText.Get(name, text + " (%s)");
            return (action is null ? key : host.HotkeyText(action) ?? key) is { } shown
                ? UiText.Fill(template, shown)
                : System.Text.RegularExpressions.Regex.Replace(template, @"\s*\(%s\)", string.Empty);
        }
        yield return WindowRow.Of(
            new WindowCell(MoneyText.Format(hospital.Balance), Highlight: true) { Tooltip = UiText.Get("tooltip.toolbar.balance", "Balance") },
            WindowCell.Button(UiText.Get("openwards.toolbar.build", "Build"), () => action("rooms"), highlight: Tutorial.Lit("build")) with { Tooltip = Tip("tooltip.toolbar.rooms", "Build rooms", key: "B") },
            WindowCell.Button(UiText.Get("openwards.toolbar.corridor", "Corridor"), () => action("corridor"), highlight: Tutorial.Lit("corridor")) with { Tooltip = Tip("tooltip.toolbar.objects", "Furnish corridor") },
            WindowCell.Button(UiText.Get("openwards.toolbar.edit", "Edit room"), () => action("edit")) with { Tooltip = Tip("tooltip.toolbar.edit", "Edit rooms/items") },
            WindowCell.Button(UiText.Get("openwards.toolbar.hire", "Hire"), () => host.Open(new HireWindow()), highlight: Tutorial.Lit("hire")) with { Tooltip = Tip("tooltip.toolbar.hire", "Hire staff", "ingame_panel_hireStaff") },
            WindowCell.Button(faxes > 0 ? UiText.Format("openwards.toolbar.faxes_count", "Faxes (%1%)", faxes) : UiText.Get("openwards.toolbar.faxes", "Faxes"), () => host.Open(new FaxWindow()), highlight: faxes > 0) with { Tooltip = Tip("openwards.toolbar.faxes_tooltip", "Faxes waiting for an answer", "ingame_openFirstMessage") },
            WindowCell.Button(UiText.Get("openwards.menu.bank", "Bank"), () => host.Open(new BankManagerWindow())) with { Tooltip = Tip("openwards.toolbar.bank_tooltip", "Bank Manager", "ingame_panel_bankManager") },
            WindowCell.Button(UiText.Get("openwards.menu.staff", "Staff"), () => host.Open(new StaffManagementWindow())) with { Tooltip = Tip("tooltip.toolbar.staff_list", "Staff management", "ingame_panel_staffManage") },
            WindowCell.Button(UiText.Get("openwards.toolbar.town", "Town"), () => host.Open(new TownMapWindow())) with { Tooltip = Tip("tooltip.toolbar.town_map", "Town map", "ingame_panel_townMap") },
            WindowCell.Button(UiText.Get("openwards.menu.casebook", "Casebook"), () => host.Open(new CasebookWindow())) with { Tooltip = Tip("tooltip.toolbar.casebook", "Drug casebook", "ingame_panel_casebook") },
            WindowCell.Button(UiText.Get("openwards.menu.research", "Research"), () => host.Open(new ResearchWindow())) with { Tooltip = Tip("tooltip.toolbar.research", "Research", "ingame_panel_research") },
            WindowCell.Button(UiText.Get("openwards.menu.status", "Status"), () => host.Open(new ProgressReportWindow())) with { Tooltip = Tip("tooltip.toolbar.status", "Status", "ingame_panel_status") },
            WindowCell.Button(UiText.Get("openwards.toolbar.charts", "Charts"), () => host.Open(new GraphWindow())) with { Tooltip = Tip("tooltip.toolbar.charts", "Charts", "ingame_panel_charts") },
            WindowCell.Button(UiText.Get("openwards.menu.policy", "Policy"), () => host.Open(new PolicyWindow())) with { Tooltip = Tip("tooltip.toolbar.policy", "Policy", "ingame_panel_policy") },
            WindowCell.Button(UiText.Get("openwards.toolbar.machines", "Machines"), () => host.Open(new MachineMenuWindow())) with { Tooltip = Tip("tooltip.toolbar.machine_menu", "Machine menu", "ingame_panel_machineMenu") },
            WindowCell.Button(UiText.Get("openwards.toolbar.messages", "Messages"), () => host.Open(new AdviserHistoryWindow())) with { Tooltip = Tip("openwards.toolbar.messages_tooltip", "The adviser's messages", "ingame_panel_adviserHistory") });

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
