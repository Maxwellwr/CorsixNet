// Ported from CorsixTH CorsixTH/Lua/objects/reception_desk.lua, CorsixTH/Lua/humanoid_actions/seek_reception.lua
// and CorsixTH/Lua/hospital.lua (getReceptionDesks).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Sim.People;

/// <summary>A reception desk's receptionist and queue.</summary>
public sealed class ReceptionDeskState
{
    internal ReceptionDeskState(HumanoidQueue queue)
    {
        Queue = queue;
    }

    /// <summary>The receptionist standing at the desk, or 0.</summary>
    public int ReceptionistId { get; internal set; }

    /// <summary>The receptionist on the way to the desk, or 0.</summary>
    public int ReservedForId { get; internal set; }

    /// <summary>Patients waiting to be seen.</summary>
    public HumanoidQueue Queue { get; }

    /// <summary>How long the first in line has stood there waiting (CorsixTH's <c>queue_advance_timer</c>).</summary>
    public int AdvanceTimer { get; internal set; }

    /// <summary>Whether a receptionist is at the desk or on the way.</summary>
    public bool IsStaffed => ReceptionistId != 0 || ReservedForId != 0;

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(ReceptionistId);
        writer.WriteInt32(ReservedForId);
        writer.WriteInt32(AdvanceTimer);
        Queue.Write(writer);
    }

    internal static ReceptionDeskState Read(SimReader reader)
    {
        var receptionist = reader.ReadInt32();
        var reserved = reader.ReadInt32();
        var timer = reader.ReadInt32();
        return new ReceptionDeskState(HumanoidQueue.Read(reader)) { ReceptionistId = receptionist, ReservedForId = reserved, AdvanceTimer = timer };
    }
}

/// <summary>
/// Reception desks: a receptionist comes to staff each one, and patients queue there when they arrive and
/// are sent on to the GP.
/// </summary>
public static class ReceptionDesks
{
    /// <summary>The reception desk's object id.</summary>
    public const string ObjectId = "reception_desk";

    /// <summary>Reception desks have longer queues than rooms (<c>setMaxQueue(20)</c>).</summary>
    public const int MaxQueue = 20;

    /// <summary>How many tiles further a patient walks for one person fewer in the queue.</summary>
    private const int TileFactor = 10;

    /// <summary>How close to the desk a patient must be to join its queue.</summary>
    internal const int CanJoinQueueDistance = 6;

    /// <summary>Sets up a reception desk that has just been placed, and looks for a receptionist for it.</summary>
    internal static void OnPlaced(Simulation simulation, RoomObject desk)
    {
        if (!string.Equals(desk.ObjectType, ObjectId, StringComparison.Ordinal))
        {
            return;
        }

        desk.Desk = new ReceptionDeskState(new HumanoidQueue(UseTile(simulation, desk), SecondaryUseTile(simulation, desk), MaxQueue));
        CheckForNearbyStaff(simulation, desk);
    }

    /// <summary>The player (0-based) whose hospital the desk stands in, or −1.</summary>
    public static int Owner(Simulation simulation, RoomObject desk)
    {
        var map = simulation.Map;
        return map.GetParcelOwner(map[desk.Tile.X, desk.Tile.Y].Parcel) - 1;
    }

    /// <summary>Where patients stand to be seen.</summary>
    public static TilePosition UseTile(Simulation simulation, RoomObject desk)
    {
        var use = simulation.Content.FindObject(desk.ObjectType)?.Orientations[desk.Orientation].Use ?? default;
        return new TilePosition(desk.Tile.X + use.X, desk.Tile.Y + use.Y);
    }

    /// <summary>Where the receptionist comes from.</summary>
    public static TilePosition SecondaryUseTile(Simulation simulation, RoomObject desk)
    {
        var use = simulation.Content.FindObject(desk.ObjectType)?.Orientations[desk.Orientation].SecondaryUsePosition ?? default;
        return new TilePosition(desk.Tile.X + use.X, desk.Tile.Y + use.Y);
    }

    /// <summary>
    /// The desks of a hospital, in id order; with <paramref name="staffedOnly"/>, only those with a
    /// receptionist at them or on the way (CorsixTH's <c>getReceptionDesks(true)</c>).
    /// </summary>
    public static IEnumerable<RoomObject> OfHospital(Simulation simulation, int player, bool staffedOnly)
    {
        foreach (var entity in simulation.Entities)
        {
            if (entity is RoomObject { Desk: { } state } desk && Owner(simulation, desk) == player && (!staffedOnly || state.IsStaffed))
            {
                yield return desk;
            }
        }
    }

    /// <summary>
    /// How busy a desk is, in tiles a patient would rather walk: ten per person queueing or on the way, and a
    /// large penalty when the queue is full.
    /// </summary>
    internal static int UsageScore(Simulation simulation, RoomObject desk)
    {
        var queue = desk.Desk!.Queue;
        var expected = 0;
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Behaviour: PatientBehaviour { Phase: PatientPhase.WalkingToReception } patient } && patient.TargetId == desk.Id)
            {
                expected++;
            }
        }

        var score = (queue.Count + expected) * TileFactor;
        return queue.IsFull ? score + 1000 : score;
    }

    /// <summary>
    /// Serves the first in line once they have stood there long enough: 4 hours plus up to a day, less the
    /// more skilled the receptionist is (CorsixTH's <c>ReceptionDesk:tick</c>).
    /// </summary>
    internal static void Update(Simulation simulation, RoomObject desk)
    {
        var state = desk.Desk!;
        if (!state.IsStaffed)
        {
            CheckForNearbyStaff(simulation, desk);
        }

        var resetTimer = true;
        if (simulation.FindEntity(state.ReceptionistId) is Humanoid { Profile: { } profile }
            && simulation.FindEntity(state.Queue.Front) is Humanoid { Behaviour: PatientBehaviour patient } front)
        {
            if (patient.IsStandingInQueue(front))
            {
                state.AdvanceTimer++;
            }

            // queue_advance_timer >= 4 + hoursPerDay * (1 - skill), with skill in thousandths.
            if (state.AdvanceTimer * StaffProfile.MaxSkill < (4 * StaffProfile.MaxSkill) + (GameDate.HoursPerDay * (StaffProfile.MaxSkill - profile.Skill)))
            {
                resetTimer = false;
            }
            else
            {
                state.Queue.Pop(simulation);
                state.Queue.VisitorCount++;
                patient.OnSeenAtReception();
            }
        }

        if (resetTimer)
        {
            state.AdvanceTimer = 0;
        }
    }

    /// <summary>
    /// Sends the nearest receptionist without a desk to this one, if it has nobody (CorsixTH's
    /// <c>ReceptionDesk:checkForNearbyStaff</c>).
    /// </summary>
    internal static void CheckForNearbyStaff(Simulation simulation, RoomObject desk)
    {
        var state = desk.Desk!;
        if (state.IsStaffed)
        {
            return;
        }

        var owner = Owner(simulation, desk);
        var target = SecondaryUseTile(simulation, desk);
        Humanoid? nearest = null;
        var nearestDistance = 0;
        foreach (var entity in simulation.Entities)
        {
            if (entity is not Humanoid { Behaviour: ReceptionistBehaviour { DeskId: 0 } } receptionist || receptionist.Employer != owner)
            {
                continue;
            }

            if (simulation.Pathfinder.FindDistance(simulation.Map, receptionist.StepTarget, target) is { } distance
                && (nearest is null || distance < nearestDistance))
            {
                nearest = receptionist;
                nearestDistance = distance;
            }
        }

        if (nearest is not null)
        {
            state.ReservedForId = nearest.Id;
            ((ReceptionistBehaviour)nearest.Behaviour!).GoToDesk(simulation, nearest, desk);
        }
    }

    /// <summary>
    /// The desk a patient should go to: the nearest staffed one, counting a busy queue as extra distance
    /// (<c>action_seek_reception_start</c>). Returns the distance to its use position too.
    /// </summary>
    internal static (RoomObject Desk, int Distance)? FindBest(Simulation simulation, Humanoid patient)
    {
        (RoomObject Desk, int Distance)? best = null;
        var bestScore = 0;
        foreach (var desk in OfHospital(simulation, patient.HospitalPlayer, staffedOnly: true))
        {
            if (simulation.Pathfinder.FindDistance(simulation.Map, patient.StepTarget, UseTile(simulation, desk)) is { } distance)
            {
                var score = distance + UsageScore(simulation, desk);
                if (best is null || score < bestScore)
                {
                    best = (desk, distance);
                    bestScore = score;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Whether a patient on <paramref name="tile"/>, <paramref name="distance"/> steps from the desk, may join
    /// its queue there: inside their hospital, outside rooms and close enough (<c>can_join_queue_at</c>).
    /// </summary>
    internal static bool CanJoinQueueAt(Simulation simulation, Humanoid patient, TilePosition tile, int distance)
    {
        var map = simulation.Map;
        var cell = map[tile.X, tile.Y];
        return cell.Has(TileFlags.Hospital) && !cell.Has(TileFlags.Room) && distance <= CanJoinQueueDistance
            && map.GetParcelOwner(cell.Parcel) == patient.HospitalPlayer + 1;
    }
}
