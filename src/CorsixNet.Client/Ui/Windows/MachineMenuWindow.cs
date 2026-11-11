// The machines list, ported from CorsixTH CorsixTH/Lua/dialogs/resizables/machine_menu.lua (UIMachineMenu): every machine
// of the player's hospital with its danger and repair marks, its status and strength, sortable by each.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Sim;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>How the machines list is sorted (<c>sort_method</c>).</summary>
internal enum MachineSort
{
    /// <summary><c>sortByRatio</c>: smoking machines first, the fewest uses left among them first, then the lowest share left.</summary>
    Ratio,

    /// <summary><c>sortByName</c>.</summary>
    Name,

    /// <summary><c>sortByRemain</c>: the fewest uses left first.</summary>
    Remaining,

    /// <summary><c>sortByStrength</c>: the weakest first.</summary>
    Strength,
}

/// <summary>A machine as the list shows it (<c>machineForList</c>).</summary>
internal sealed record MachineListEntry(RoomObject Machine, string Name, bool Smoking, int AssignedHandymanId, int Strength, int Remaining, int Percentage);

/// <summary>
/// <c>UIMachineMenu</c> (F10): the player's machines in rooms that have not crashed. "!" marks a machine at risk of
/// exploding (a click offers to replace it), "X" one a handyman is on the way to repair (a click opens his window); the
/// name opens the machine's window and moves the view there.
/// </summary>
internal sealed class MachineMenuWindow : UiWindow
{
    public MachineSort Sort { get; private set; } = MachineSort.Ratio;

    public override string Title => "Machines";

    public override string? Hotkey => "F10";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var machines = List(host.Simulation, host.Player, Sort);
        yield return WindowRow.Of(
            new WindowCell("!", Dim: true) { Tooltip = "Danger indicator" },
            new WindowCell("X", Dim: true) { Tooltip = "Repair assignment indicator" },
            Header("Machine", MachineSort.Name, "Machine name"),
            Header("Status", MachineSort.Ratio, "Remaining strength to total strength of the machines"),
            Header("Remain", MachineSort.Remaining, "Remaining strength of the machines"),
            Header("Strength", MachineSort.Strength, "Total strength of the machines"));
        if (machines.Count == 0)
        {
            yield return WindowCell.Line("The hospital has no machines.", dim: true);
            yield break;
        }

        foreach (var entry in machines)
        {
            var machine = entry.Machine;
            var assigned = entry.AssignedHandymanId;
            yield return WindowRow.Of(
                WindowCell.Button(entry.Smoking ? "!" : " ", () => host.Open(new MachineWindow(machine.Id)), enabled: entry.Smoking, highlight: entry.Smoking),
                WindowCell.Button(assigned != 0 ? "X" : " ", () => host.Open(new StaffWindow(assigned)), enabled: assigned != 0),
                WindowCell.Button(entry.Name, () =>
                {
                    host.CenterOn(machine.Tile);
                    host.Open(new MachineWindow(machine.Id));
                }),
                WindowCell.Number(string.Create(CultureInfo.InvariantCulture, $"{entry.Percentage}%")),
                WindowCell.Number(entry.Remaining.ToString(CultureInfo.InvariantCulture), highlight: entry.Smoking),
                WindowCell.Number(entry.Strength.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private WindowCell Header(string text, MachineSort sort, string tooltip) =>
        WindowCell.Button(text, () => Sort = sort, highlight: Sort == sort) with { Tooltip = tooltip + ". Click to sort by this value." };

    /// <summary><c>UIMachineMenu:update</c>: the player's machines (not slave parts) in rooms that have not crashed, sorted.</summary>
    public static List<MachineListEntry> List(Simulation simulation, int player, MachineSort sort)
    {
        var assigned = new Dictionary<int, int>();
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Behaviour: HandymanBehaviour { TargetId: not 0 } handyman, IsFired: false } staff
                && handyman.Phase is HandymanPhase.WalkingToMachine or HandymanPhase.WaitingToRepair or HandymanPhase.GoingToRepair or HandymanPhase.Repairing)
            {
                assigned.TryAdd(handyman.TargetId, staff.Id);
            }
        }

        var list = new List<MachineListEntry>();
        foreach (var entity in simulation.Entities)
        {
            if (entity is not RoomObject { Machine: { } state, IsDestroyed: false } machine
                || simulation.FindEntity(machine.RoomId) is not Room { IsCrashed: false } room
                || RoomVisits.Owner(simulation, room) != player)
            {
                continue;
            }

            var remaining = state.RemainingUses;
            var percentage = state.Strength <= 0 ? 0 : (int)Math.Floor(remaining * 100.0 / state.Strength);
            var name = simulation.Content.FindObject(machine.ObjectType)?.Name ?? machine.ObjectType;
            list.Add(new MachineListEntry(machine, name, state.IsBreaking, assigned.GetValueOrDefault(machine.Id), state.Strength, remaining, percentage));
        }

        // Lua's table.sort is not stable; ties keep the order the machines were built in here.
        Comparison<MachineListEntry> compare = sort switch
        {
            MachineSort.Name => (a, b) => string.CompareOrdinal(a.Name, b.Name),
            MachineSort.Remaining => (a, b) => a.Remaining.CompareTo(b.Remaining),
            MachineSort.Strength => (a, b) => a.Strength.CompareTo(b.Strength),
            _ => CompareByRatio,
        };
        return [.. list.Select((entry, index) => (entry, index)).OrderBy(pair => pair.entry, Comparer<MachineListEntry>.Create(compare)).ThenBy(pair => pair.index).Select(pair => pair.entry)];
    }

    private static int CompareByRatio(MachineListEntry a, MachineListEntry b)
    {
        if (a.Smoking != b.Smoking)
        {
            return a.Smoking ? -1 : 1;
        }

        if (a.Smoking && a.Remaining != b.Remaining)
        {
            return a.Remaining.CompareTo(b.Remaining);
        }

        return a.Percentage.CompareTo(b.Percentage);
    }
}
