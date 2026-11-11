// Taking a finished room back to its blueprint, ported from CorsixTH CorsixTH/Lua/dialogs/edit_room.lua
// (UIEditRoom:returnToDoorPhase, removeRoom, cancel, abortRoom, _enterObjectsPhase's objects_backup) and
// dialogs/place_objects.lua (UIPlaceObjects:removeAllObjects, addObjects).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Rooms;

/// <summary>
/// A finished room of the player's taken back to its blueprint while edited (CorsixTH's <c>returnToDoorPhase</c>): its
/// walls, door and objects are gone, the objects sold back at full price, and the room stays paid for, so that the
/// player can resize it or move its door and build it again without paying for the room a second time. Its machines'
/// wear is kept for the machines placed again (the objects' saved <c>state</c>).
/// </summary>
/// <remarks>
/// CorsixTH puts every object back on the list to place, buying them all again; here the room's needed objects are
/// bought again when it is built, as for a new room, and extra objects are bought again from the edit panel.
/// </remarks>
public sealed class RoomRebuild
{
    private const int MaxMachines = 1 << 10;

    private readonly List<(string ObjectType, int Strength, int TimesUsed, int TotalUsage)> _machines = [];

    internal RoomRebuild(string roomType, TileRect area)
    {
        RoomType = roomType;
        Area = area;
    }

    /// <summary>The room's type.</summary>
    public string RoomType { get; }

    /// <summary>Where the room stood.</summary>
    public TileRect Area { get; }

    /// <summary>The room built again, still being furnished, or 0 while it is a blueprint.</summary>
    public int RoomId { get; internal set; }

    /// <summary>
    /// <c>returnToDoorPhase</c>: the room, empty of people while edited, comes down; its objects are sold at full price
    /// (<c>removeAllObjects</c>) and its machines' wear remembered.
    /// </summary>
    internal static bool Start(Simulation simulation, Room room, int player)
    {
        if (room.IsActive || room.IsClosing || !room.IsPaid || room.IsCrashed || player < 0 || player >= simulation.Hospitals.Count
            || !RoomBuilder.IsOwnedBy(simulation.Map, room, player)
            || simulation.Entities.Any(e => e is Humanoid { IsDespawning: false } h && room.Area.Contains(h.Tile)))
        {
            return false;
        }

        var hospital = simulation.Hospitals[player];
        var rebuild = new RoomRebuild(room.RoomType, room.Area);
        if (hospital.Rebuild is { } earlier && earlier.RoomType == room.RoomType)
        {
            // Machines not placed again since the last time keep their wear.
            rebuild._machines.AddRange(earlier._machines);
        }

        foreach (var placed in simulation.Entities.OfType<RoomObject>().Where(o => o.RoomId == room.Id && !o.IsDestroyed).ToList())
        {
            hospital.Receive(simulation.Hours, hospital.Research.ObjectCost(simulation.Rules, placed.ObjectType), TransactionKind.SellObject, placed.ObjectType);
            if (placed.Machine is { } machine)
            {
                rebuild._machines.Add((placed.ObjectType, machine.Strength, machine.TimesUsed, machine.TotalUsage));
            }
        }

        RoomBuilder.Remove(simulation, room);
        hospital.Rebuild = rebuild;
        return true;
    }

    /// <summary>The room is built again from the blueprint: it is already paid for.</summary>
    internal static void OnBuilt(Simulation simulation, Room room, int player)
    {
        if (player >= 0 && player < simulation.Hospitals.Count && simulation.Hospitals[player].Rebuild is { RoomId: 0 } rebuild
            && rebuild.RoomType == room.RoomType)
        {
            room.IsPaid = true;
            rebuild.RoomId = room.Id;
        }
    }

    /// <summary>A machine placed in the room built again takes over the wear of one of its kind taken down.</summary>
    internal static void OnMachinePlaced(Simulation simulation, RoomObject placed, int player)
    {
        if (placed.Machine is not { } machine || player < 0 || player >= simulation.Hospitals.Count
            || simulation.Hospitals[player].Rebuild is not { } rebuild || rebuild.RoomId == 0 || rebuild.RoomId != placed.RoomId)
        {
            return;
        }

        var index = rebuild._machines.FindIndex(m => m.ObjectType == placed.ObjectType);
        if (index >= 0)
        {
            var saved = rebuild._machines[index];
            rebuild._machines.RemoveAt(index);
            machine.Strength = saved.Strength;
            machine.TimesUsed = saved.TimesUsed;
            machine.TotalUsage = saved.TotalUsage;
        }
    }

    /// <summary>The room built again is confirmed or taken down: nothing more to remember.</summary>
    internal static void OnRoomFinished(Simulation simulation, Room room, int player)
    {
        if (player >= 0 && player < simulation.Hospitals.Count && simulation.Hospitals[player].Rebuild is { } rebuild && rebuild.RoomId == room.Id)
        {
            simulation.Hospitals[player].Rebuild = null;
        }
    }

    /// <summary>
    /// <c>cancel</c> in the walls phase of a paid room, after the player confirms (<c>abortRoom</c>): half the room's cost
    /// back, as when taking a room down, and the blueprint is given up.
    /// </summary>
    internal static void Abandon(Simulation simulation, int player)
    {
        if (player < 0 || player >= simulation.Hospitals.Count || simulation.Hospitals[player].Rebuild is not { RoomId: 0 } rebuild)
        {
            return;
        }

        simulation.Hospitals[player].Rebuild = null;
        ConstructionSite.Of(simulation, player)?.Unmark(simulation);
        if (simulation.Content.FindRoom(rebuild.RoomType) is { } definition)
        {
            RoomPurchases.RefundPaidRoom(simulation, definition, player);
        }
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteString(RoomType);
        writer.WriteInt32(Area.X);
        writer.WriteInt32(Area.Y);
        writer.WriteInt32(Area.Width);
        writer.WriteInt32(Area.Height);
        writer.WriteInt32(RoomId);
        writer.WriteInt32(_machines.Count);
        foreach (var (type, strength, used, total) in _machines)
        {
            writer.WriteString(type);
            writer.WriteInt32(strength);
            writer.WriteInt32(used);
            writer.WriteInt32(total);
        }
    }

    internal static RoomRebuild Read(SimReader reader)
    {
        var type = reader.ReadString();
        var area = new TileRect(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        var rebuild = new RoomRebuild(type, area) { RoomId = reader.ReadInt32() };
        var count = reader.ReadCount(MaxMachines);
        for (var i = 0; i < count; i++)
        {
            rebuild._machines.Add((reader.ReadString(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
        }

        return rebuild;
    }
}
