// Ported from CorsixTH CorsixTH/Lua/room.lua (tryAdvanceQueue, canHumanoidEnter, getUsageScore,
// getEntranceXY, tryToFindNearbyPatients, tryMovePatient), CorsixTH/Lua/rooms/gp.lua and CorsixTH/Lua/world.lua (findRoomNear).
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

    /// <summary>The toilets' room type: no staff, as many patients at once as there are toilets.</summary>
    internal const string ToiletsRoom = "toilets";

    /// <summary>The object patients lie on in general diagnosis.</summary>
    internal const string TrolleyType = "crash_trolley";

    /// <summary>The research department's room type.</summary>
    internal const string ResearchRoom = "research";

    /// <summary>What the research department's researchers work at (<c>staff_usage_objects</c>; not the autopsy).</summary>
    internal static readonly string[] ResearchObjects = ["desk", "cabinet", "computer", "analyser"];

    /// <summary>The ultrascan's room type.</summary>
    internal const string UltrascanRoom = "ultrascan";

    /// <summary>
    /// The rooms whose doctor meanders until a patient is ready, then waits at the secondary use position of the object
    /// the patient is examined on, which they use together: the crash trolley, the ultrascanner.
    /// </summary>
    private static readonly Dictionary<string, string> ExaminingObjects = new(StringComparer.Ordinal)
    {
        [GeneralDiagnosisRoom] = TrolleyType,
        [UltrascanRoom] = "ultrascanner",
    };

    /// <summary>
    /// What each room type patients can be seen in needs: the kind of staff it sends for (CorsixTH's
    /// <c>required_staff</c>) and the object the patient is seen at.
    /// </summary>
    private static readonly Dictionary<string, (BehaviourKind Staff, string PatientObject)> Rules = new(StringComparer.Ordinal)
    {
        [PatientBehaviour.FirstRoom] = (BehaviourKind.Doctor, "chair"),
        [PharmacyRoom] = (BehaviourKind.Nurse, "pharmacy_cabinet"),
        [GeneralDiagnosisRoom] = (BehaviourKind.Doctor, "screen"),
        [UltrascanRoom] = (BehaviourKind.Doctor, "ultrascanner"),
    };

    /// <summary>The object the room's patient is examined on with the doctor, or null for rooms without one.</summary>
    internal static RoomObject? ExaminingObject(Simulation simulation, Room room) =>
        ExaminingObjects.TryGetValue(room.RoomType, out var type) ? FindObject(simulation, room.Id, type) : null;

    /// <summary>Whether the room's doctor meanders until a patient is ready to be examined.</summary>
    internal static bool HasExaminingObject(string roomType) => ExaminingObjects.ContainsKey(roomType);

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
        if (string.Equals(room.RoomType, ToiletsRoom, StringComparison.Ordinal))
        {
            TryAdvanceToiletsQueue(simulation, room);
            return;
        }

        if (string.Equals(room.RoomType, ResearchRoom, StringComparison.Ordinal))
        {
            CallResearchers(simulation, room);
            return;
        }

        EnsureQueue(simulation, room);
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
        if (string.Equals(room.RoomType, ToiletsRoom, StringComparison.Ordinal))
        {
            // Patient:notifyNewRoom: patients who found no toilets may try again.
            var hospital = Owner(simulation, room);
            foreach (var entity in simulation.Entities)
            {
                if (entity is Humanoid { Behaviour: PatientBehaviour { GoingToToilet: ToiletVisit.NoToilets } waiting } humanoid && humanoid.HospitalPlayer == hospital)
                {
                    waiting.GoingToToilet = ToiletVisit.No;
                }
            }

            return;
        }

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

        // Room:roomFinished.
        TryToFindNearbyPatients(simulation, room);
    }

    /// <summary>
    /// <c>Room:tryToFindNearbyPatients</c>: takes patients from the back of the queues of the hospital's other rooms of
    /// the same type that have two or more in line, while they would be better off coming here (<c>tryMovePatient</c>).
    /// The first in each line stays.
    /// </summary>
    internal static void TryToFindNearbyPatients(Simulation simulation, Room room)
    {
        if (!IsSupported(room.RoomType) || EnsureQueue(simulation, room) is null)
        {
            return;
        }

        var owner = Owner(simulation, room);
        foreach (var oldRoom in simulation.Entities.OfType<Room>().ToList())
        {
            if (oldRoom.Id == room.Id || !string.Equals(oldRoom.RoomType, room.RoomType, StringComparison.Ordinal)
                || Owner(simulation, oldRoom) != owner || oldRoom.DoorQueue is not { Count: >= 2 } oldQueue)
            {
                continue;
            }

            for (var index = oldQueue.Count - 1; index >= 1; index--)
            {
                if (simulation.FindEntity(oldQueue.Members[index]) is Humanoid patient && TryMovePatient(simulation, oldRoom, room, patient))
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// <c>tryMovePatient</c>: sends a patient queueing for <paramref name="oldRoom"/> to <paramref name="newRoom"/> if
    /// the new room's usage score and distance beat the old one's. Returns whether to stop looking in the old room's
    /// queue: when the new room can't be reached or isn't better. Someone who isn't a patient, or who is going home or to
    /// die, is skipped.
    /// </summary>
    private static bool TryMovePatient(Simulation simulation, Room oldRoom, Room newRoom, Humanoid humanoid)
    {
        if (humanoid.Behaviour is not PatientBehaviour patient || patient.IsGoingHomeOrToDie
            || simulation.FindEntity(newRoom.DoorId) is not RoomDoor newDoor || simulation.FindEntity(oldRoom.DoorId) is not RoomDoor oldDoor)
        {
            return false;
        }

        if (simulation.Pathfinder.FindDistance(simulation.Map, humanoid.StepTarget, OutsideTile(newRoom, newDoor)) is not { } newDistance)
        {
            return true;
        }

        var newScore = UsageScore(simulation, newRoom) + newDistance;
        var oldScore = simulation.Pathfinder.FindDistance(simulation.Map, humanoid.StepTarget, OutsideTile(oldRoom, oldDoor)) is { } oldDistance
            ? UsageScore(simulation, oldRoom) + oldDistance
            : newScore + 1;
        if (newScore >= oldScore)
        {
            return true;
        }

        patient.SendToRoom(simulation, humanoid, newRoom);
        return false;
    }

    private static HumanoidQueue? EnsureQueue(Simulation simulation, Room room)
    {
        if (room.DoorQueue is null && simulation.FindEntity(room.DoorId) is RoomDoor door)
        {
            room.DoorQueue = new HumanoidQueue(OutsideTile(room, door), door.InsideTile(room.Area), MaxQueue);
        }

        return room.DoorQueue;
    }

    /// <summary>Whether the player's hospital has an active room of the type (<c>World:findRoomNear</c> finding one).</summary>
    internal static bool HasRoom(Simulation simulation, int player, string roomType)
    {
        foreach (var entity in simulation.Entities)
        {
            if (entity is Room { IsActive: true } room && string.Equals(room.RoomType, roomType, StringComparison.Ordinal) && Owner(simulation, room) == player)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <c>World:findFreeObjectNearToUse</c>: the object of the type in the room that nobody else goes to or uses, nearest to
    /// the humanoid by path.
    /// </summary>
    internal static RoomObject? FindFreeObject(Simulation simulation, Room room, Humanoid humanoid, string objectType)
    {
        var taken = new HashSet<int>();
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Behaviour: PatientBehaviour { ToiletObjectId: > 0 } other } someone && someone.Id != humanoid.Id)
            {
                taken.Add(other.ToiletObjectId);
            }
        }

        RoomObject? best = null;
        var bestDistance = 0;
        foreach (var entity in simulation.Entities)
        {
            if (entity is RoomObject placed && placed.RoomId == room.Id && string.Equals(placed.ObjectType, objectType, StringComparison.Ordinal)
                && !taken.Contains(placed.Id)
                && simulation.Pathfinder.FindDistance(simulation.Map, humanoid.Tile, UseTile(simulation, placed)) is { } distance
                && (best is null || distance < bestDistance))
            {
                best = placed;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// <c>ToiletRoom:getPatientCount</c>: the patients inside, not counting those at a sink unless the room has a single
    /// toilet (then it is private).
    /// </summary>
    internal static int ToiletsPatientCount(Simulation simulation, Room room, int toilets)
    {
        var users = 0;
        var atSink = 0;
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Behaviour: PatientBehaviour patient } && patient.TargetId == room.Id
                && patient.Phase is >= PatientPhase.EnteringRoom and < PatientPhase.LeavingRoom)
            {
                users++;
                if (patient.Phase == PatientPhase.UsingSink)
                {
                    atSink++;
                }
            }
        }

        return toilets == 1 ? users : users - atSink;
    }

    /// <summary>How many toilets the room has (<c>ToiletRoom:roomFinished</c>'s <c>maximum_patients</c>).</summary>
    private static int CountToilets(Simulation simulation, Room room) =>
        simulation.Entities.OfType<RoomObject>().Count(o => o.RoomId == room.Id && string.Equals(o.ObjectType, "loo", StringComparison.Ordinal));

    /// <summary>The toilets let the first in line in while fewer patients are inside than it has toilets (<c>canHumanoidEnter</c>).</summary>
    private static void TryAdvanceToiletsQueue(Simulation simulation, Room room)
    {
        if (EnsureQueue(simulation, room) is not { Count: > 0 } queue || simulation.FindEntity(room.DoorId) is not RoomDoor door
            || ToiletsPatientCount(simulation, room, CountToilets(simulation, room)) >= CountToilets(simulation, room))
        {
            return;
        }

        var front = queue.Pop(simulation);
        queue.VisitorCount++;
        if (simulation.FindEntity(front) is Humanoid { Behaviour: PatientBehaviour patient } humanoid)
        {
            patient.LetIn(simulation, humanoid, room, door);
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

        var toilets = string.Equals(room.RoomType, ToiletsRoom, StringComparison.Ordinal);
        var maximum = toilets ? CountToilets(simulation, room) : MaximumPatients;
        var patients = toilets ? ToiletsPatientCount(simulation, room, maximum) : room.PatientId != 0 ? 1 : 0;
        var score = ((queue?.Count ?? 0) + expected + patients - maximum) * TileFactor;

        // A room without required staff (the toilets) is always ready.
        if (room.StaffPresent || toilets)
        {
            score -= ReadinessBonus;
        }

        return queue is { IsFull: true } ? score + 1000 : score;
    }

    /// <summary>
    /// The research department sends for researchers (doctors with the research ability, <c>required_staff</c>
    /// <c>Researcher</c>) until it has as many as desks (<c>ResearchRoom:roomFinished</c>'s <c>maximum_staff</c>): the
    /// nearest free one each time.
    /// </summary>
    private static void CallResearchers(Simulation simulation, Room room)
    {
        if (simulation.FindEntity(room.DoorId) is not RoomDoor door)
        {
            return;
        }

        var desks = 0;
        foreach (var entity in simulation.Entities)
        {
            if (entity is RoomObject { ObjectType: "desk" } desk && desk.RoomId == room.Id)
            {
                desks++;
            }
        }

        var owner = Owner(simulation, room);
        var researchers = 0;
        Humanoid? nearest = null;
        var nearestDistance = 0;
        var target = door.InsideTile(room.Area);
        foreach (var entity in simulation.Entities)
        {
            if (entity is not Humanoid { Behaviour: DoctorBehaviour doctor, Profile: { IsResearcher: true } } staff || staff.Employer != owner || staff.IsFired)
            {
                continue;
            }

            if (doctor.RoomId == room.Id)
            {
                researchers++;
            }
            else if (doctor.RoomId == 0 && simulation.Pathfinder.FindDistance(simulation.Map, staff.StepTarget, target) is { } distance
                && (nearest is null || distance < nearestDistance))
            {
                nearest = staff;
                nearestDistance = distance;
            }
        }

        if (researchers < desks && nearest is not null)
        {
            if (room.StaffId == 0)
            {
                room.StaffId = nearest.Id;
            }

            ((IRoomStaff)nearest.Behaviour!).GoToRoom(simulation, nearest, room, target);
        }
    }

    /// <summary>
    /// <c>World:findFreeObjectNearToUse</c> for a researcher: the object they can work at that no other researcher goes to
    /// or uses, nearest by path, other than the one they just used.
    /// </summary>
    internal static RoomObject? FindFreeResearchObject(Simulation simulation, Room room, Humanoid humanoid, int previous)
    {
        var taken = new HashSet<int>();
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Behaviour: DoctorBehaviour { ResearchObjectId: > 0 } other } someone && someone.Id != humanoid.Id)
            {
                taken.Add(other.ResearchObjectId);
            }
        }

        RoomObject? best = null;
        var bestDistance = 0;
        foreach (var entity in simulation.Entities)
        {
            if (entity is RoomObject placed && placed.RoomId == room.Id && placed.Id != previous && !taken.Contains(placed.Id)
                && ResearchObjects.Contains(placed.ObjectType, StringComparer.Ordinal)
                && simulation.Pathfinder.FindDistance(simulation.Map, humanoid.Tile, UseTile(simulation, placed)) is { } distance
                && (best is null || distance < bestDistance))
            {
                best = placed;
                bestDistance = distance;
            }
        }

        return best;
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
