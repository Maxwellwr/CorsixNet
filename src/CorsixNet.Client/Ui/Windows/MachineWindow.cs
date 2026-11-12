// The machine window, ported from CorsixTH CorsixTH/Lua/dialogs/machine_dialog.lua (UIMachine): the machine's name,
// its total uses, its strength left as a bar, and the call handyman and replace buttons.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Client.Maps;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Client.Ui.Windows;

/// <summary><c>UIMachine</c>: what a machine has been through and what can be done about it.</summary>
internal sealed class MachineWindow(int machineId) : UiWindow
{
    /// <summary>The status bar's width in CorsixTH, in pixels: one character here stands for four.</summary>
    private const int BarLength = 10;

    public int MachineId { get; } = machineId;

    public override string Title => "Machine";

    public override bool IsSameAs(UiWindow other) => other is MachineWindow;

    public override bool IsDone(IWindowHost host) => Machine(host) is null;

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        if (Machine(host) is not { Machine: { } state } machine)
        {
            yield break;
        }

        var simulation = host.Simulation;
        var name = simulation.Content.FindObject(machine.ObjectType)?.Name ?? machine.ObjectType;

        // The name is in brackets while the room waits for the repair the player called for.
        yield return WindowCell.Line(state.IsRepairCalled ? $"({name})" : name, highlight: true);
        yield return WindowRow.Of(host.Texts?.String(32, 115) ?? "Times used", WindowCell.Number(state.TotalUsage.ToString(CultureInfo.InvariantCulture)));

        // UIMachine:draw: the bar is (1 - times_used / strength) of its length.
        var filled = state.Strength <= 0 ? 0 : (int)Math.Round(Math.Clamp(1 - ((double)state.TimesUsed / state.Strength), 0, 1) * BarLength, MidpointRounding.AwayFromZero);
        yield return WindowRow.Of(
            host.Texts?.String(32, 116) ?? "Status",
            WindowCell.Number(new string('I', filled) + new string('.', BarLength - filled)),
            WindowCell.Number(string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, state.RemainingUses)} of {state.Strength}"), dim: true));
        if (state.IsBreaking)
        {
            yield return WindowCell.Line("Smoking: it needs a handyman soon.", dim: true);
        }

        yield return WindowRow.Gap;
        var cost = host.Player >= 0 && host.Player < simulation.Hospitals.Count ? simulation.Hospitals[host.Player].Research.ObjectCost(simulation.Rules, machine.ObjectType) : 0;
        yield return WindowRow.Of(
            WindowCell.Button("Call handyman", () => host.Submit(new CallHandymanCommand(MachineId)), state.TimesUsed != 0 && !state.IsRepairCalled).WithTooltip(OriginalTooltips.Get(host, 117, "Call a handyman to repair the machine")),
            WindowCell.Button(string.Create(CultureInfo.InvariantCulture, $"Replace ({MoneyText.Format(cost)})"), () => Replace(host, name, cost)).WithTooltip(OriginalTooltips.Get(host, 118, "Replace the machine")));
    }

    /// <summary>
    /// <c>UIMachine:replaceMachine</c>: without the money the adviser says so, otherwise the player is asked first.
    /// </summary>
    private void Replace(IWindowHost host, string name, int cost)
    {
        var simulation = host.Simulation;
        if (host.Player < 0 || host.Player >= simulation.Hospitals.Count || simulation.Hospitals[host.Player].Balance < cost)
        {
            host.PlaySound("wrong2.wav");
            host.Notice(string.Create(CultureInfo.InvariantCulture, $"You need at least {MoneyText.Format(cost)} in the bank to afford a new {name}!"));
            return;
        }

        // S[35][3]: "%s (machine name) %d (price)".
        var question = host.Texts?.String(35, 3) is { } text
            ? text.Replace("%s", name, StringComparison.Ordinal).Replace("%d", cost.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            : string.Create(CultureInfo.InvariantCulture, $"Replace the {name} for {MoneyText.Format(cost)}?");
        var id = MachineId;
        host.Open(new ConfirmWindow(question, () => host.Submit(new ReplaceMachineCommand(id))));
    }

    private RoomObject? Machine(IWindowHost host) =>
        host.Simulation.FindEntity(MachineId) is RoomObject { Machine: not null, IsDestroyed: false } machine
        && host.Simulation.FindEntity(machine.RoomId) is Room { IsCrashed: false }
            ? machine
            : null;

    /// <summary>The machine with a part on the tile, if any (a click there opens its window).</summary>
    public static RoomObject? At(Sim.Simulation simulation, TilePosition tile)
    {
        foreach (var placed in simulation.Entities.OfType<RoomObject>())
        {
            if (placed.Machine is null || placed.IsDestroyed
                || simulation.Content.FindObject(placed.ObjectType) is not { } definition
                || !definition.Orientations.TryGetValue(placed.Orientation, out var layout))
            {
                continue;
            }

            if (layout.Footprint.Any(part => !part.IsSide && placed.Tile.X + part.X == tile.X && placed.Tile.Y + part.Y == tile.Y))
            {
                return placed;
            }
        }

        return null;
    }
}
