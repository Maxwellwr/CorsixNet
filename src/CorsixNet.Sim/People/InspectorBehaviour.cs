// The health inspector, ported from CorsixTH CorsixTH/Lua/entities/humanoids/inspector.lua (Inspector:goHome,
// checkForReceptionDesk), humanoid_actions/seek_reception.lua and objects/reception_desk.lua (handleInspector).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Disasters;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.People;

/// <summary>Where the health inspector is in their visit.</summary>
public enum InspectorPhase : byte
{
    SeekReception,
    WalkingToReception,
    InReceptionQueue,
    GoingHome,
}

/// <summary>
/// The health inspector, who comes when an epidemic's cover-up is over: they report at reception, which settles the
/// epidemic, and leave. A hospital without a staffed desk gets the verdict anyway.
/// </summary>
/// <remarks>Not yet: the announcement when they come on the grounds.</remarks>
public sealed class InspectorBehaviour : HumanoidBehaviour, IQueueMember
{
    public override BehaviourKind Kind => BehaviourKind.Inspector;

    public InspectorPhase Phase { get; private set; }

    /// <summary>The desk whose queue they are in or heading for, or 0.</summary>
    public int DeskId { get; private set; }

    private TilePosition QueueTile { get; set; }

    private TilePosition QueueFace { get; set; }

    private TilePosition HomeTile { get; set; }

    /// <inheritdoc/>
    public bool IsStandingInQueue(Humanoid humanoid) => !humanoid.IsMoving && humanoid.Tile == QueueTile;

    /// <inheritdoc/>
    public void TakeQueuePlace(Simulation simulation, Humanoid humanoid, TilePosition tile, TilePosition face)
    {
        QueueFace = face;
        if (QueueTile == tile && (humanoid.Tile == tile || humanoid.Destination == tile))
        {
            return;
        }

        QueueTile = tile;
        if (humanoid.Tile != tile || humanoid.IsMoving)
        {
            humanoid.WalkTo(simulation, tile);
        }
    }

    internal override void Update(Simulation simulation, Humanoid humanoid)
    {
        switch (Phase)
        {
            case InspectorPhase.SeekReception:
            case InspectorPhase.WalkingToReception:
                if (Phase == InspectorPhase.WalkingToReception && humanoid.IsMoving)
                {
                    break;
                }

                SeekReception(simulation, humanoid);
                break;
            case InspectorPhase.InReceptionQueue:
                if (humanoid.Tile == QueueTile)
                {
                    humanoid.FaceTowards(QueueFace);
                }

                break;
            case InspectorPhase.GoingHome:
                if (humanoid.Tile == HomeTile || !humanoid.WalkTo(simulation, HomeTile))
                {
                    humanoid.IsDespawning = true;
                }

                break;
        }
    }

    /// <summary><c>handleInspector</c>: at the front of the queue, the inspector settles the epidemic and leaves.</summary>
    internal void OnSeenAtReception(Simulation simulation, Humanoid humanoid)
    {
        DeskId = 0;
        if (Phase == InspectorPhase.GoingHome)
        {
            return;
        }

        if (Hospital(simulation, humanoid) is { } hospital)
        {
            Epidemics.InspectorArrived(simulation, hospital);
        }

        GoHome(simulation, humanoid);
    }

    /// <summary><c>Inspector:goHome</c>: out through a random spawn point.</summary>
    internal void GoHome(Simulation simulation, Humanoid humanoid)
    {
        if (Phase == InspectorPhase.GoingHome)
        {
            return;
        }

        if (simulation.FindEntity(DeskId) is RoomObject { Desk: { } desk })
        {
            desk.Queue.Remove(simulation, humanoid.Id);
        }

        DeskId = 0;
        Phase = InspectorPhase.GoingHome;
        var points = simulation.Arrivals.SpawnPointsOf(simulation.Map);
        HomeTile = points.Count > 0 ? points[simulation.Random.Next(points.Count)] : humanoid.Tile;
        if (!humanoid.WalkTo(simulation, HomeTile))
        {
            HomeTile = humanoid.Tile;
        }
    }

    /// <summary>
    /// <c>SeekReceptionAction</c> and <c>Inspector:checkForReceptionDesk</c>: to the best staffed desk; when the
    /// hospital has none, the verdict comes anyway and the inspector leaves. With a desk out of reach they look again
    /// (CorsixTH has them meander meanwhile).
    /// </summary>
    private void SeekReception(Simulation simulation, Humanoid humanoid)
    {
        var approach = ReceptionDesks.Approach(simulation, humanoid, out var deskId);
        if (deskId != 0)
        {
            DeskId = deskId;
        }

        switch (approach)
        {
            case DeskApproach.Joined:
                Phase = InspectorPhase.InReceptionQueue;
                break;
            case DeskApproach.Walking:
                Phase = InspectorPhase.WalkingToReception;
                break;
            case DeskApproach.NoDesk when !ReceptionDesks.OfHospital(simulation, humanoid.HospitalPlayer, staffedOnly: true).Any():
                if (Hospital(simulation, humanoid) is { } hospital)
                {
                    Epidemics.InspectorArrived(simulation, hospital);
                }

                GoHome(simulation, humanoid);
                break;
            default:
                Phase = InspectorPhase.SeekReception;
                break;
        }
    }

    private static Economy.Hospital? Hospital(Simulation simulation, Humanoid humanoid) =>
        humanoid.HospitalPlayer >= 0 && humanoid.HospitalPlayer < simulation.Hospitals.Count ? simulation.Hospitals[humanoid.HospitalPlayer] : null;

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteByte((byte)Phase);
        writer.WriteInt32(DeskId);
        writer.WriteTile(QueueTile);
        writer.WriteTile(QueueFace);
        writer.WriteTile(HomeTile);
    }

    internal static InspectorBehaviour ReadState(SimReader reader)
    {
        var phase = reader.ReadByte();
        if (phase > (byte)InspectorPhase.GoingHome)
        {
            throw new InvalidDataException($"Unknown inspector phase {phase}.");
        }

        return new InspectorBehaviour
        {
            Phase = (InspectorPhase)phase,
            DeskId = reader.ReadInt32(),
            QueueTile = reader.ReadTile(),
            QueueFace = reader.ReadTile(),
            HomeTile = reader.ReadTile(),
        };
    }
}
