// When rooms and their objects are paid for and refunded, ported from CorsixTH
// CorsixTH/Lua/dialogs/build_room.lua (UIBuildRoom:buildRoom), CorsixTH/Lua/dialogs/edit_room.lua
// (UIEditRoom:_enterObjectsPhase, UIEditRoom:confirm) and CorsixTH/Lua/dialogs/place_objects.lua
// (UIPlaceObjects:addObjects, UIPlaceObjects:removeObject).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Definitions;

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
        return HospitalOf(simulation, player) is { } hospital && hospital.Balance >= simulation.Rules.BuildCost(room);
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
            hospital.Spend(simulation.Tick, count * simulation.Rules.ObjectCost(objectId), TransactionKind.BuyObject, objectId);
        }
    }

    /// <summary>
    /// Whether <paramref name="player"/> can buy an object for a corridor: the hospital needs its whole price
    /// (CorsixTH's <c>UIFurnishCorridor:purchase</c>).
    /// </summary>
    public static bool CanAffordObject(Simulation simulation, string objectId, int player)
    {
        return HospitalOf(simulation, player) is { } hospital && hospital.Balance >= simulation.Rules.ObjectCost(objectId);
    }

    /// <summary>Pays for an object bought for a corridor.</summary>
    internal static void BuyObject(Simulation simulation, string objectId, int player)
    {
        HospitalOf(simulation, player)?.Spend(simulation.Tick, simulation.Rules.ObjectCost(objectId), TransactionKind.BuyObject, objectId);
    }

    /// <summary>Pays for the room itself when it is confirmed; its objects were paid for already.</summary>
    internal static void PayForRoom(Simulation simulation, RoomDefinition room, int player)
    {
        HospitalOf(simulation, player)?.Spend(simulation.Tick, simulation.Rules.RoomCost(room.Id), TransactionKind.BuildRoom, room.Id);
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
                hospital.Receive(simulation.Tick, simulation.Rules.ObjectCost(objectId), TransactionKind.SellObject, objectId);
            }
        }
    }

    private static Hospital? HospitalOf(Simulation simulation, int player)
    {
        return player >= 0 && player < simulation.Hospitals.Count ? simulation.Hospitals[player] : null;
    }
}
