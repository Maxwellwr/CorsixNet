// Ported from CorsixTH CorsixTH/Lua/objects/reception_desk.lua (ReceptionDesk:occupy) and
// CorsixTH/Lua/humanoid_actions/staff_reception.lua.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.People;

/// <summary>Where a receptionist is in their job.</summary>
public enum ReceptionistPhase : byte
{
    /// <summary>Without a desk.</summary>
    Idle,

    /// <summary>Walking to the desk's secondary use position.</summary>
    WalkingToDesk,

    /// <summary>Stepping from there onto the desk's tile.</summary>
    SteppingIn,

    /// <summary>Standing at the desk, seeing patients.</summary>
    Staffing,
}

/// <summary>
/// A receptionist: sent to a desk, they walk to the desk's back, step onto its tile and stay there.
/// </summary>
public sealed class ReceptionistBehaviour : HumanoidBehaviour
{
    public override BehaviourKind Kind => BehaviourKind.Receptionist;

    public ReceptionistPhase Phase { get; private set; }

    /// <summary>The desk the receptionist staffs or is going to, or 0.</summary>
    public int DeskId { get; private set; }

    /// <summary>Wandering about while without a desk.</summary>
    private Meandering Meander { get; init; } = new();

    internal void GoToDesk(Simulation simulation, Humanoid humanoid, RoomObject desk)
    {
        DeskId = desk.Id;
        Phase = ReceptionistPhase.WalkingToDesk;
        humanoid.WalkTo(simulation, ReceptionDesks.SecondaryUseTile(simulation, desk));
    }

    internal override void Update(Simulation simulation, Humanoid humanoid)
    {
        if (Phase == ReceptionistPhase.Idle)
        {
            Meander.Update(simulation, humanoid);
            return;
        }

        Meander.Reset();

        if (simulation.FindEntity(DeskId) is not RoomObject { Desk: { } state } desk)
        {
            DeskId = 0;
            Phase = ReceptionistPhase.Idle;
            return;
        }

        switch (Phase)
        {
            case ReceptionistPhase.WalkingToDesk:
                var back = ReceptionDesks.SecondaryUseTile(simulation, desk);
                if (humanoid.Tile != back)
                {
                    if (!humanoid.WalkTo(simulation, back))
                    {
                        // The desk cannot be reached: give it up, so another receptionist may be sent.
                        state.ReservedForId = 0;
                        DeskId = 0;
                        Phase = ReceptionistPhase.Idle;
                    }

                    return;
                }

                Phase = ReceptionistPhase.SteppingIn;
                humanoid.RawStepTo(desk.Tile);
                break;
            case ReceptionistPhase.SteppingIn:
                Phase = ReceptionistPhase.Staffing;
                state.ReceptionistId = humanoid.Id;
                state.ReservedForId = 0;
                break;
        }
    }

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteByte((byte)Phase);
        writer.WriteInt32(DeskId);
        Meander.Write(writer);
    }

    internal static ReceptionistBehaviour ReadState(SimReader reader)
    {
        return new ReceptionistBehaviour { Phase = (ReceptionistPhase)reader.ReadByte(), DeskId = reader.ReadInt32(), Meander = Meandering.Read(reader) };
    }
}
