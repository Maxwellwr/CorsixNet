// Ported from CorsixTH CorsixTH/Lua/room.lua (tryAdvanceQueue, canHumanoidEnter, getUsageScore,
// getEntranceXY), CorsixTH/Lua/rooms/gp.lua and CorsixTH/Lua/world.lua (findRoomNear).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Sim.People;

/// <summary>
/// Patients and staff visiting rooms: a doctor is sent to each GP's office, patients queue at its door
/// and go in one at a time once the doctor is there.
/// </summary>
/// <remarks>
/// CorsixTH calls staff to rooms through its call dispatcher; here a room without staff sends for the
/// nearest free doctor itself. Only the GP's office exists so far, so the room's rules are the GP's.
/// </remarks>
public static class RoomVisits
{
    /// <summary>Patients a room takes at once (CorsixTH's <c>maximum_patients</c>).</summary>
    public const int MaximumPatients = 1;

    /// <summary>The longest a room's queue gets before it counts as full (CorsixTH's default <c>max_size</c>).</summary>
    public const int MaxQueue = 6;

    /// <summary>How often the GP may fail to conclude before the patient is dealt with anyway.</summary>
    internal const int GpMaxTimes = 3;

    /// <summary>The pharmacy's room type.</summary>
    internal const string PharmacyRoom = "pharmacy";

    /// <summary>General diagnosis's room type.</summary>
    internal const string GeneralDiagnosisRoom = "general_diag";

    /// <summary>The object patients lie on in general diagnosis.</summary>
    internal const string TrolleyType = "crash_trolley";

    /// <summary>
    /// What each room type patients can be seen in needs: the kind of staff it sends for (CorsixTH's
    /// <c>required_staff</c>) and the object the patient is seen at.
    /// </summary>
    private static readonly Dictionary<string, (BehaviourKind Staff, string PatientObject)> Rules = new(StringComparer.Ordinal)
    {
        [PatientBehaviour.FirstRoom] = (BehaviourKind.Doctor, "chair"),
        [PharmacyRoom] = (BehaviourKind.Nurse, "pharmacy_cabinet"),
        [GeneralDiagnosisRoom] = (BehaviourKind.Doctor, "screen"),
    };

    /// <summary>How many tiles further a patient walks for one person fewer in the queue.</summary>
    private const int TileFactor = 10;

    /// <summary>How many tiles further a patient walks to a room that has its staff.</summary>
    private const int ReadinessBonus = 50;

    /// <summary>The tile outside the room's door, where patients queue and leave to.</summary>
    public static TilePosition OutsideTile(Room room, RoomDoor door) => room.Area.Contains(door.Tile) ? door.OtherSide : door.Tile;

    /// <summary>The player (0-based) whose hospital the room is in.</summary>
    public static int Owner(Simulation simulation, Room room) => simulation.Map.GetParcelOwner(simulation.Map[room.Area.X, room.Area.Y].Parcel) - 1;

    /// <summary>Where someone stands to use a placed object.</summary>
    public static TilePosition UseTile(Simulation simulation, RoomObject placed)
    {
        var use = simulation.Content.FindObject(placed.ObjectType)?.Orientations[placed.Orientation].Use ?? default;
        return new TilePosition(placed.Tile.X + use.X, placed.Tile.Y + use.Y);
    }

    /// <summary>The first object of a type in a room, in id order (CorsixTH's <c>findObjectNear</c> in a small room).</summary>
    public static RoomObject? FindObject(Simulation simulation, int roomId, string objectType)
    {
        return simulation.Entities.OfType<RoomObject>().FirstOrDefault(o => o.RoomId == roomId && string.Equals(o.ObjectType, objectType, StringComparison.Ordinal));
    }

    /// <summary>The object a patient is seen at in the room: the GP's chair, the pharmacy cabinet.</summary>
    internal static RoomObject? PatientObject(Simulation simulation, Room room)
    {
        return Rules.TryGetValue(room.RoomType, out var rules) ? FindObject(simulation, room.Id, rules.PatientObject) : null;
    }

    /// <summary>Where the patient stands to be seen at <paramref name="placed"/>: its secondary use position if it has one.</summary>
    internal static TilePosition SecondaryUseTile(Simulation simulation, RoomObject placed)
    {
        var layout = simulation.Content.FindObject(placed.ObjectType)?.Orientations[placed.Orientation];
        var use = layout?.SecondaryUsePosition ?? layout?.Use ?? default;
        return new TilePosition(placed.Tile.X + use.X, placed.Tile.Y + use.Y);
    }

    internal static void Update(Simulation simulation, Room room)
    {
        if (room.DoorQueue is null && simulation.FindEntity(room.DoorId) is RoomDoor door)
        {
            room.DoorQueue = new HumanoidQueue(OutsideTile(room, door), door.InsideTile(room.Area), MaxQueue);
        }

        if (room.StaffId == 0)
        {
            CallStaff(simulation, room);
        }

        TryAdvanceQueue(simulation, room);
    }

    /// <summary>
    /// The best room of a type for a patient: the nearest one, counting a long queue and missing staff as
    /// extra distance (<c>Room:getUsageScore</c>).
    /// </summary>
    internal static Room? FindBest(Simulation simulation, Humanoid patient, string roomType)
    {
        Room? best = null;
        var bestScore = 0;
        foreach (var entity in simulation.Entities)
        {
            if (entity is not Room { IsActive: true } room || !string.Equals(room.RoomType, roomType, StringComparison.Ordinal)
                || Owner(simulation, room) != patient.HospitalPlayer || simulation.FindEntity(room.DoorId) is not RoomDoor door
                || simulation.Pathfinder.FindDistance(simulation.Map, patient.StepTarget, OutsideTile(room, door)) is not { } distance)
            {
                continue;
            }

            var score = distance + UsageScore(simulation, room);
            if (best is null || score < bestScore)
            {
                best = room;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>Whether patients can be seen in rooms of this type yet: the GP's office, the pharmacy and general diagnosis so far.</summary>
    public static bool IsSupported(string roomType) => Rules.ContainsKey(roomType);

    /// <summary>
    /// The staff is done with the patient: after the GP they pay and get further in their diagnosis, after a treatment
    /// they pay and are cured; then they get up and leave, and the staff goes on (<c>Room:dealtWithPatient</c>).
    /// </summary>
    internal static void DealtWithPatient(Simulation simulation, Room room)
    {
        if (simulation.FindEntity(room.PatientId) is Humanoid { Behaviour: PatientBehaviour patient } humanoid)
        {
            patient.DealtWith(simulation, room, humanoid);
        }
    }

    /// <summary>A room started working: the patients of its hospital who wait for a room of its kind go to look for it.</summary>
    internal static void OnRoomActivated(Simulation simulation, Room room)
    {
        if (!IsSupported(room.RoomType))
        {
            return;
        }

        var owner = Owner(simulation, room);
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Behaviour: PatientBehaviour patient } humanoid && humanoid.HospitalPlayer == owner)
            {
                patient.OnRoomBuilt(room.RoomType);
            }
        }
    }

    private static int UsageScore(Simulation simulation, Room room)
    {
        var queue = room.DoorQueue;
        var expected = 0;
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Behaviour: PatientBehaviour { Phase: PatientPhase.WalkingToRoom } patient } && patient.TargetId == room.Id)
            {
                expected++;
            }
        }

        var patients = room.PatientId != 0 ? 1 : 0;
        var score = ((queue?.Count ?? 0) + expected + patients - MaximumPatients) * TileFactor;
        if (room.StaffPresent)
        {
            score -= ReadinessBonus;
        }

        return queue is { IsFull: true } ? score + 1000 : score;
    }

    /// <summary>Sends the nearest member of staff of the kind the room needs, without a room, of the room's hospital, to the room.</summary>
    private static void CallStaff(Simulation simulation, Room room)
    {
        if (!Rules.TryGetValue(room.RoomType, out var rules) || simulation.FindEntity(room.DoorId) is not RoomDoor door)
        {
            return;
        }

        var owner = Owner(simulation, room);
        var target = door.InsideTile(room.Area);
        Humanoid? nearest = null;
        var nearestDistance = 0;
        foreach (var entity in simulation.Entities)
        {
            if (entity is not Humanoid { Behaviour: { } behaviour } staff || behaviour is not IRoomStaff { RoomId: 0 } || behaviour is NurseBehaviour { IsFree: false } || behaviour.Kind != rules.Staff
                || staff.Employer != owner)
            {
                continue;
            }

            if (simulation.Pathfinder.FindDistance(simulation.Map, staff.StepTarget, target) is { } distance
                && (nearest is null || distance < nearestDistance))
            {
                nearest = staff;
                nearestDistance = distance;
            }
        }

        if (nearest is not null)
        {
            room.StaffId = nearest.Id;
            ((IRoomStaff)nearest.Behaviour!).GoToRoom(simulation, nearest, room, target);
        }
    }

    /// <summary>
    /// Lets the first in line in when the room may take them: it is active, its doctor is in and nobody
    /// else is being seen (<c>Room:tryAdvanceQueue</c>, <c>Room:canHumanoidEnter</c>).
    /// </summary>
    private static void TryAdvanceQueue(Simulation simulation, Room room)
    {
        if (room.DoorQueue is not { Count: > 0 } queue || room.PatientId != 0 || !room.StaffPresent
            || simulation.FindEntity(room.DoorId) is not RoomDoor door)
        {
            return;
        }

        var front = queue.Pop(simulation);
        queue.VisitorCount++;
        if (simulation.FindEntity(front) is Humanoid { Behaviour: PatientBehaviour patient } humanoid)
        {
            room.PatientId = front;
            patient.LetIn(simulation, humanoid, room, door);
        }
    }
}
