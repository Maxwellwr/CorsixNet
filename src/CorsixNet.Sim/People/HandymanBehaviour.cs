// Handymen, ported from CorsixTH CorsixTH/Lua/entities/humanoids/staff/handyman.lua (parcelNr, doMeandering),
// humanoid_actions/meander.lua (the handyman's parcel) and dialogs/staff_dialog.lua (UIStaff:changeParcel).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.People;

/// <summary>
/// A handyman. Sweeping, watering and repairs are not ported yet, so for now he wanders about, outside rooms only
/// within his assigned parcel (<c>parcelNr</c>), if he has one.
/// </summary>
public sealed class HandymanBehaviour : HumanoidBehaviour
{
    public override BehaviourKind Kind => BehaviourKind.Handyman;

    /// <summary>The parcel he keeps to, or 0 for all of them (<c>parcelNr</c>).</summary>
    public int Parcel { get; internal set; }

    private Meandering Meander { get; init; } = new();

    internal override void Update(Simulation simulation, Humanoid humanoid)
    {
        // Handymen are free to visit any room, so the parcel holds only outside rooms.
        var inRoom = simulation.Map.Contains(humanoid.Tile.X, humanoid.Tile.Y) && simulation.Map[humanoid.Tile.X, humanoid.Tile.Y].RoomId != 0;
        Meander.Update(simulation, humanoid, inRoom ? 0 : Parcel);
    }

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteInt32(Parcel);
        Meander.Write(writer);
    }

    internal static HandymanBehaviour ReadState(SimReader reader) =>
        new() { Parcel = reader.ReadInt32(), Meander = Meandering.Read(reader) };
}
