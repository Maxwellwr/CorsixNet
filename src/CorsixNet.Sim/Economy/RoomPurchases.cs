// When rooms and their objects are paid for and refunded, ported from CorsixTH
// CorsixTH/Lua/dialogs/build_room.lua (UIBuildRoom:buildRoom), CorsixTH/Lua/dialogs/edit_room.lua
// (UIEditRoom:_enterObjectsPhase, UIEditRoom:confirm) and CorsixTH/Lua/dialogs/place_objects.lua
// (UIPlaceObjects:addObjects, UIPlaceObjects:removeObject).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Rooms;

namespace CorsixNet.Sim.Economy;

/// <summary>
/// Paying for rooms as the original does: a room can only be started with its whole cost in the
/// bank; its needed objects are bought when it is built (they come to the cursor), the room itself
/// is paid for when the player confirms it, and taking down an unfinished room sells its objects
/// back at full price.
/// </summary>
public static class RoomPurchases
{
    /// <summary>Whether <paramref name="player"/> (0-based) has the money to start a room of this type.</summary>
    public static bool CanAfford(Simulation simulation, RoomDefinition room, int player)
    {
        return HospitalOf(simulation, player) is { } hospital && hospital.Balance >= BuildCost(simulation, hospital, room);
    }

    /// <summary>
    /// Whether <paramref name="player"/> (0-based) may build a room of this type: the level has it and the hospital has
    /// discovered it (CorsixTH's build room dialog lists only discovered rooms).
    /// </summary>
    public static bool CanBuild(Simulation simulation, RoomDefinition room, int player)
    {
        return HospitalOf(simulation, player) is { } hospital && hospital.IsRoomDiscovered(room.Id);
    }

    /// <summary>Buys the objects a newly built room needs, one transaction per object type.</summary>
    internal static void BuyNeededObjects(Simulation simulation, RoomDefinition room, int player)
    {
        if (HospitalOf(simulation, player) is not { } hospital)
        {
            return;
        }

        foreach (var (objectId, count) in LevelRules.Needed(room))
        {
            hospital.Spend(simulation.Hours, count * hospital.Research.ObjectCost(simulation.Rules, objectId), TransactionKind.BuyObject, objectId);
        }
    }

    /// <summary>
    /// Whether <paramref name="player"/> can buy an object for a corridor: the hospital needs its whole price
    /// (CorsixTH's <c>UIFurnishCorridor:purchase</c>).
    /// </summary>
    public static bool CanAffordObject(Simulation simulation, string objectId, int player)
    {
        return HospitalOf(simulation, player) is { } hospital && hospital.Balance >= hospital.Research.ObjectCost(simulation.Rules, objectId);
    }

    /// <summary>Pays for an object bought for a corridor.</summary>
    internal static void BuyObject(Simulation simulation, string objectId, int player)
    {
        if (HospitalOf(simulation, player) is { } hospital)
        {
            hospital.Spend(simulation.Hours, hospital.Research.ObjectCost(simulation.Rules, objectId), TransactionKind.BuyObject, objectId);
        }
    }

    /// <summary>Pays for the room itself when it is confirmed; its objects were paid for already.</summary>
    internal static void PayForRoom(Simulation simulation, RoomDefinition room, int player)
    {
        HospitalOf(simulation, player)?.Spend(simulation.Hours, simulation.Rules.RoomCost(room.Id), TransactionKind.BuildRoom, room.Id);
    }

    /// <summary>Sells back the objects bought for a room that is taken down unfinished, one transaction per object.</summary>
    internal static void SellNeededObjects(Simulation simulation, RoomDefinition room, int player)
    {
        if (HospitalOf(simulation, player) is not { } hospital)
        {
            return;
        }

        foreach (var (objectId, count) in LevelRules.Needed(room))
        {
            for (var i = 0; i < count; i++)
            {
                hospital.Receive(simulation.Hours, hospital.Research.ObjectCost(simulation.Rules, objectId), TransactionKind.SellObject, objectId);
            }
        }
    }

    /// <summary>
    /// <c>UIEditRoom:abortRoom</c> for a room already paid for: half the room's whole cost, less half the price of each
    /// kind of object it needs (once per kind, as CorsixTH does); extra objects are not paid back (a TODO there).
    /// </summary>
    internal static void RefundPaidRoom(Simulation simulation, RoomDefinition room, int player)
    {
        if (HospitalOf(simulation, player) is not { } hospital)
        {
            return;
        }

        decimal refund = BuildCost(simulation, hospital, room) / 2;
        foreach (var (objectId, _) in LevelRules.Needed(room))
        {
            refund -= hospital.Research.ObjectCost(simulation.Rules, objectId) / 2m;
        }

        if (refund > 0)
        {
            hospital.Receive(simulation.Hours, (int)decimal.Floor(refund), TransactionKind.SellObject, room.Id);
        }
    }

    /// <summary>
    /// Whether one more object of this type in the room is beyond what the room needs, so that it is bought on its own
    /// as in CorsixTH's buy items window (<c>UIPlaceObjects:addObjects</c>); every corridor object is.
    /// </summary>
    internal static bool IsExtra(Simulation simulation, int roomId, string objectId)
    {
        if (roomId == 0)
        {
            return true;
        }

        var needed = simulation.FindEntity(roomId) is Room room && simulation.Content.FindRoom(room.RoomType)?.NeededObjects is { } objects
            ? objects.GetValueOrDefault(objectId)
            : 0;
        return CountIn(simulation, roomId, objectId) >= needed;
    }

    /// <summary>Sells back an extra object picked up again (<c>UIPlaceObjects:removeObject</c>'s sale, at full price).</summary>
    internal static void SellObject(Simulation simulation, string objectId, int player)
    {
        if (HospitalOf(simulation, player) is { } hospital)
        {
            hospital.Receive(simulation.Hours, hospital.Research.ObjectCost(simulation.Rules, objectId), TransactionKind.SellObject, objectId);
        }
    }

    /// <summary>
    /// Sells back the extra objects standing in an unfinished room that is taken down (CorsixTH leaves this as a TODO in
    /// <c>UIEditRoom:abortRoom</c>, but sells the needed ones back here too).
    /// </summary>
    internal static void SellExtraObjects(Simulation simulation, Room room, RoomDefinition definition, int player)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var obj in simulation.Entities.OfType<RoomObject>())
        {
            if (obj.RoomId == room.Id)
            {
                counts[obj.ObjectType] = counts.GetValueOrDefault(obj.ObjectType) + 1;
            }
        }

        foreach (var (objectId, count) in counts)
        {
            for (var i = definition.NeededObjects?.GetValueOrDefault(objectId) ?? 0; i < count; i++)
            {
                SellObject(simulation, objectId, player);
            }
        }
    }

    private static int CountIn(Simulation simulation, int roomId, string objectId) =>
        simulation.Entities.OfType<RoomObject>().Count(o => o.RoomId == roomId && string.Equals(o.ObjectType, objectId, StringComparison.Ordinal));

    /// <summary>What a room costs this hospital in all: the room and its needed objects at their current prices (<c>build_cost</c>).</summary>
    public static int BuildCost(Simulation simulation, Hospital hospital, RoomDefinition room)
    {
        var cost = simulation.Rules.RoomCost(room.Id);
        foreach (var (objectId, count) in LevelRules.Needed(room))
        {
            cost += count * hospital.Research.ObjectCost(simulation.Rules, objectId);
        }

        return cost;
    }

    private static Hospital? HospitalOf(Simulation simulation, int player)
    {
        return player >= 0 && player < simulation.Hospitals.Count ? simulation.Hospitals[player] : null;
    }
}
