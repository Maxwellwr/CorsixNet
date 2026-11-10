// Ported from CorsixTH CorsixTH/Lua/room.lua (tryAdvanceQueue, canHumanoidEnter, getUsageScore,
// getEntranceXY, tryToFindNearbyPatients, tryMovePatient), CorsixTH/Lua/rooms/gp.lua and CorsixTH/Lua/world.lua (findRoomNear).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Staff;

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

    /// <summary>The cardiogram's room type (rooms/cardiogram.lua): undressing, the machine with the doctor, dressing.</summary>
    internal const string CardiogramRoom = "cardiogram";

    /// <summary>The scanner's room type (rooms/scanner_room.lua): undressing, the scanner while the doctor works the console, dressing.</summary>
    internal const string ScannerRoom = "scanner";

    /// <summary>Whether patients undress behind the room's screen before they are examined: general diagnosis, the cardiogram, the scanner.</summary>
    internal static bool UndressesFirst(string roomType) =>
        roomType is GeneralDiagnosisRoom or CardiogramRoom or ScannerRoom or OperatingTheatreRoom;

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
        [CardiogramRoom] = "cardio",
        [ScannerRoom] = "scanner",
        [OperatingTheatreRoom] = "operating_table",
        [UltrascanRoom] = "ultrascanner",
        [XRayRoom] = "x_ray",
        [InflationRoom] = "inflator",
        [SlackTongueRoom] = "slicer",
        [HairRestorationRoom] = "hair_restorer",
        [ElectrolysisRoom] = "electrolyser",
        [FractureClinicRoom] = "cast_remover",
        [BloodMachineRoom] = "blood_machine",
        [DecontaminationRoom] = "shower",
        [JellyVatRoom] = "jelly_moulder",
    };

    /// <summary>The decontamination room's type (rooms/decontamination.lua), the treatment of serious radiation.</summary>
    internal const string DecontaminationRoom = "decontamination";

    /// <summary>
    /// What the doctor works at while the patient uses the machine alone: the X-ray's radiation shield, the
    /// decontamination shower's console; null for rooms where they use the machine together.
    /// </summary>
    internal static string? DoctorConsole(string roomType) => roomType switch
    {
        XRayRoom => "radiation_shield",
        DecontaminationRoom or ScannerRoom or HairRestorationRoom or ElectrolysisRoom => "console",
        _ => null,
    };

    /// <summary>
    /// The blood machine room's type (rooms/blood_machine_room.lua): the doctor works the machine at its use position and
    /// the patient waits at its other end, the reverse of the other machines.
    /// </summary>
    internal const string BloodMachineRoom = "blood_machine";

    /// <summary>
    /// The jelly vat's room type (rooms/jelly_vat.lua): like the blood machine, the doctor works the jelly moulder from its
    /// use position while the patient waits at its other end, and is in the doctor's animations most of the time.
    /// </summary>
    internal const string JellyVatRoom = "jelly_vat";

    /// <summary>Whether the room's doctor uses its machine with the patient, rather than the patient with the doctor.</summary>
    internal static bool DoctorUsesMachine(string roomType) => roomType is BloodMachineRoom or JellyVatRoom;


    /// <summary>The operating theatre's room type (rooms/operating_theatre.lua): two surgeons operate on one patient.</summary>
    internal const string OperatingTheatreRoom = "operating_theatre";

    /// <summary>The tile of the operating table's far side (operating_table_b's <c>slave_position</c>), where the second surgeon stands.</summary>
    internal static TilePosition OperatingTableSlaveTile(Simulation simulation, RoomObject table) =>
        table.Orientation is Direction.North or Direction.South
            ? new TilePosition(table.Tile.X + 1, table.Tile.Y - 1)
            : new TilePosition(table.Tile.X - 1, table.Tile.Y + 1);

    /// <summary>The tile in whose pass an object's animations are drawn (its render attach tile).</summary>
    internal static TilePosition RenderTile(Simulation simulation, RoomObject placed)
    {
        var attach = simulation.Content.FindObject(placed.ObjectType)?.Orientations.GetValueOrDefault(placed.Orientation)?.RenderAttach ?? default;
        return new TilePosition(placed.Tile.X + attach.X, placed.Tile.Y + attach.Y);
    }

    /// <summary>Whether someone other than <paramref name="except"/> is using <paramref name="placed"/> (<c>wait_for_object</c>'s <c>obj.user</c>).</summary>
    internal static bool IsObjectBusy(Simulation simulation, RoomObject placed, Humanoid except) =>
        simulation.Entities.Any(e => e is Humanoid { ActionAnimation: not null } other && other.Id != except.Id && other.HiddenObjectId == placed.Id);

    /// <summary>The surgeons of the operating theatre already in scrubs (the screen's white outfits).</summary>
    internal static int SurgeonsInScrubs(Simulation simulation, Room room) =>
        simulation.Entities.Count(e => e is Humanoid { Type: HumanoidType.Surgeon, Behaviour: DoctorBehaviour doctor } && doctor.RoomId == room.Id);

    /// <summary>The doctors working in the operating theatre or on their way to it.</summary>
    internal static List<Humanoid> Surgeons(Simulation simulation, Room room) =>
        [.. simulation.Entities.OfType<Humanoid>().Where(h => h is { Behaviour: DoctorBehaviour doctor, IsFired: false } && doctor.RoomId == room.Id)];

    /// <summary>
    /// The operating theatre's turn: call surgeons until it has two (<c>required_staff</c>), and let a patient in only
    /// while both are in scrubs and ready (<c>OperatingTheatreRoom:canHumanoidEnter</c>).
    /// </summary>
    private static void UpdateOperatingTheatre(Simulation simulation, Room room)
    {
        var queue = EnsureQueue(simulation, room);
        var surgeons = Surgeons(simulation, room);
        if (room.StaffId != 0 && !surgeons.Any(surgeon => surgeon.Id == room.StaffId))
        {
            room.StaffId = surgeons.FirstOrDefault()?.Id ?? 0;
        }

        if (room.StaffId == 0)
        {
            CallStaff(simulation, room);
        }
        else if (surgeons.Count < 2)
        {
            CallExtraStaff(simulation, room, BehaviourKind.Doctor);
        }

        var ready = surgeons.Count == 2 && surgeons.All(s => s is { Type: HumanoidType.Surgeon, Behaviour: DoctorBehaviour { Phase: DoctorPhase.OtReady } });
        room.StaffPresent = surgeons.Count == 2 && surgeons.All(s => room.Area.Contains(s.Tile) && s.Behaviour is DoctorBehaviour { Phase: not DoctorPhase.WalkingToRoom });
        if (!ready || room.PatientId != 0 || queue is not { Count: > 0 } || HandymanBehaviour.IsRepairingIn(simulation, room)
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

    /// <summary>The inflation room's type (rooms/inflation.lua), the treatment of bloaty heads.</summary>
    internal const string InflationRoom = "inflation";

    /// <summary>The slack tongue room's type (rooms/slack_tongue.lua): the slicer and a doctor.</summary>
    internal const string SlackTongueRoom = "slack_tongue";

    /// <summary>The hair restoration room's type (rooms/hair_restoration.lua): the hair restorer, a console and a doctor.</summary>
    internal const string HairRestorationRoom = "hair_restoration";

    /// <summary>The electrolysis room's type (rooms/electrolysis.lua): the electrolyser, a console and a doctor.</summary>
    internal const string ElectrolysisRoom = "electrolysis";

    /// <summary>The fracture clinic's type (rooms/fracture_clinic.lua): the cast remover and a nurse.</summary>
    internal const string FractureClinicRoom = "fracture_clinic";

    /// <summary>The X-ray room's type.</summary>
    internal const string XRayRoom = "x_ray";

    /// <summary>
    /// Where the room's doctor waits for the patient: at the X-ray's radiation shield, otherwise the examining object's
    /// other end (its secondary use position).
    /// </summary>
    internal static TilePosition? DoctorStation(Simulation simulation, Room room)
    {
        if (DoctorConsole(room.RoomType) is { } console)
        {
            return FindObject(simulation, room.Id, console) is { } shield ? UseTile(simulation, shield) : null;
        }

        if (DoctorUsesMachine(room.RoomType))
        {
            return ExaminingObject(simulation, room) is { } machine ? UseTile(simulation, machine) : null;
        }

        return ExaminingObject(simulation, room) is { } examining ? SecondaryUseTile(simulation, examining) : null;
    }

    /// <summary>
    /// What each room type patients can be seen in needs: the kind of staff it sends for (CorsixTH's
    /// <c>required_staff</c>) and the object the patient is seen at.
    /// </summary>
    private static readonly Dictionary<string, (BehaviourKind Staff, string PatientObject)> Rules = new(StringComparer.Ordinal)
    {
        [PatientBehaviour.FirstRoom] = (BehaviourKind.Doctor, "chair"),
        [PharmacyRoom] = (BehaviourKind.Nurse, "pharmacy_cabinet"),
        [GeneralDiagnosisRoom] = (BehaviourKind.Doctor, "screen"),
        [CardiogramRoom] = (BehaviourKind.Doctor, "screen"),
        [ScannerRoom] = (BehaviourKind.Doctor, "screen"),
        [UltrascanRoom] = (BehaviourKind.Doctor, "ultrascanner"),
        [XRayRoom] = (BehaviourKind.Doctor, "x_ray"),
        [InflationRoom] = (BehaviourKind.Doctor, "inflator"),
        [SlackTongueRoom] = (BehaviourKind.Doctor, "slicer"),
        [HairRestorationRoom] = (BehaviourKind.Doctor, "hair_restorer"),
        [ElectrolysisRoom] = (BehaviourKind.Doctor, "electrolyser"),
        [FractureClinicRoom] = (BehaviourKind.Nurse, "cast_remover"),
        [BloodMachineRoom] = (BehaviourKind.Doctor, "blood_machine"),
        [DecontaminationRoom] = (BehaviourKind.Doctor, "shower"),
        [JellyVatRoom] = (BehaviourKind.Doctor, "jelly_moulder"),
        [PsychRoom] = (BehaviourKind.Doctor, "couch"),
        [WardRoom] = (BehaviourKind.Nurse, "bed"),
        [OperatingTheatreRoom] = (BehaviourKind.Doctor, "surgeon_screen"),
    };

    /// <summary>The ward's room type (rooms/ward.lua): as many patients as beds and as many nurses as desks.</summary>
    internal const string WardRoom = "ward";

    /// <summary>
    /// The ward's turn: call nurses until it has one for each desk (<c>WardRoom:roomFinished</c>'s
    /// <c>maximum_staff</c>), note whether one of them is in, and let patients in while a bed is free.
    /// </summary>
    private static void UpdateWard(Simulation simulation, Room room)
    {
        var queue = EnsureQueue(simulation, room);
        var nurses = WardNurses(simulation, room).ToList();
        room.StaffPresent = nurses.Any(nurse => room.Area.Contains(nurse.Tile)
            && nurse.Behaviour is NurseBehaviour { Phase: not NursePhase.WalkingToRoom });
        if (room.StaffId != 0 && !nurses.Any(nurse => nurse.Id == room.StaffId))
        {
            room.StaffId = nurses.FirstOrDefault()?.Id ?? 0;
        }

        if (room.StaffId == 0)
        {
            CallStaff(simulation, room);
        }
        else if (nurses.Count < CountObjects(simulation, room, "desk"))
        {
            CallExtraStaff(simulation, room, BehaviourKind.Nurse);
        }

        if (queue is not { Count: > 0 } || !room.StaffPresent || simulation.FindEntity(room.DoorId) is not RoomDoor door
            || WardPatients(simulation, room).Count() >= CountObjects(simulation, room, "bed") || FindFreeBed(simulation, room) is null)
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

    /// <summary>The nurses who work in the ward or are on their way to it.</summary>
    private static IEnumerable<Humanoid> WardNurses(Simulation simulation, Room room) =>
        simulation.Entities.OfType<Humanoid>().Where(h => h is { Behaviour: NurseBehaviour nurse, IsFired: false } && nurse.RoomId == room.Id);

    /// <summary>The patients let into the ward who have not left it yet.</summary>
    internal static IEnumerable<Humanoid> WardPatients(Simulation simulation, Room room) =>
        simulation.Entities.OfType<Humanoid>().Where(h => h.Behaviour is PatientBehaviour patient && patient.TargetId == room.Id
            && patient.Phase is >= PatientPhase.EnteringRoom and < PatientPhase.LeavingRoom);

    /// <summary>
    /// A second (or later) member of staff for a room that takes several (a ward with more desks, the operating
    /// theatre): the free one of the kind, qualified for the room, who scores best for it.
    /// </summary>
    private static void CallExtraStaff(Simulation simulation, Room room, BehaviourKind kind)
    {
        if (simulation.FindEntity(room.DoorId) is not RoomDoor door)
        {
            return;
        }

        var owner = Owner(simulation, room);
        var target = door.InsideTile(room.Area);
        Humanoid? best = null;
        var bestScore = 0m;
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid staff && IsFreeFor(staff, kind, owner) && IsQualifiedFor(room.RoomType, staff)
                && CallScore(simulation, room, target, staff) is { } score && (best is null || score < bestScore))
            {
                best = staff;
                bestScore = score;
            }
        }

        if (best is not null)
        {
            ((IRoomStaff)best.Behaviour!).GoToRoom(simulation, best, room, target);
        }
    }

    /// <summary><c>findFreeObjectNearToUse(humanoid, "bed")</c>: a bed of the ward no patient has taken.</summary>
    internal static RoomObject? FindFreeBed(Simulation simulation, Room room)
    {
        var taken = WardPatients(simulation, room).Select(h => ((PatientBehaviour)h.Behaviour!).WardBedId).ToHashSet();
        return simulation.Entities.OfType<RoomObject>().FirstOrDefault(o => o.RoomId == room.Id && o.ObjectType == "bed" && !taken.Contains(o.Id));
    }

    /// <summary><c>findFreeObjectNearToUse(humanoid, "desk")</c>: a desk of the ward no other nurse has taken, nearest first.</summary>
    internal static RoomObject? FindFreeWardDesk(Simulation simulation, Room room, Humanoid nurse)
    {
        var taken = WardNurses(simulation, room).Where(h => h.Id != nurse.Id).Select(h => ((NurseBehaviour)h.Behaviour!).WardDeskId).ToHashSet();
        return simulation.Entities.OfType<RoomObject>()
            .Where(o => o.RoomId == room.Id && o.ObjectType == "desk" && !taken.Contains(o.Id))
            .OrderBy(o => Math.Abs(o.Tile.X - nurse.Tile.X) + Math.Abs(o.Tile.Y - nurse.Tile.Y))
            .ThenBy(o => o.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// <c>WardRoom:updateHealingAmount</c>: how much of a patient's stay one round of lying in bed gets done. Each nurse in
    /// the room gives a half and her service quality; it is divided by the logarithm of one more than the patients in the
    /// room and by 0.8 plus a tenth of the difficulty (easier is faster); without a nurse in the room nothing heals.
    /// </summary>
    public static decimal WardHealingAmount(Simulation simulation, Room room)
    {
        var nurseFactor = 0m;
        foreach (var nurse in WardNurses(simulation, room))
        {
            if (room.Area.Contains(nurse.Tile) && nurse.Behaviour is NurseBehaviour { Phase: not NursePhase.WalkingToRoom })
            {
                nurseFactor += 0.5m + NurseBehaviour.ServiceQuality(nurse);
            }
        }

        if (nurseFactor == 0)
        {
            return 0;
        }

        var patients = 1 + WardPatients(simulation, room).Count(h => room.Area.Contains(h.Tile));
        var difficulty = 0.8m + ((int)simulation.Rules.Difficulty * 0.1m);
        return nurseFactor / DecimalMath.Ln(Math.Max(2, patients)) / difficulty;
    }

    /// <summary>The psychiatric room's type (rooms/psych.lua): a diagnosis and treatment room for a psychiatrist.</summary>
    internal const string PsychRoom = "psych";

    /// <summary>
    /// Whether <paramref name="staff"/> has what the room type asks for beyond their kind (CorsixTH's
    /// <c>required_staff</c> of <c>Psychiatrist</c>): the psychiatric room takes only psychiatrists.
    /// </summary>
    internal static bool IsQualifiedFor(string roomType, Humanoid staff) => roomType switch
    {
        PsychRoom => staff.Profile is { IsPsychiatrist: true },
        OperatingTheatreRoom => staff.Profile is { IsSurgeon: true },
        _ => true,
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

    /// <summary>
    /// <c>Room:onHumanoidEnter</c> for a doctor or nurse without a job who stands in a room: if the room needs them and has
    /// room for them, they take it over; otherwise they leave it and wander about outside. Only someone just put down
    /// there (<paramref name="placed"/>) takes over a room for one from its member of staff, who is sent out unless busy
    /// with a patient; someone left there, such as the one sent out, does not.
    /// </summary>
    /// <returns>Whether the staff member was in a room and has been dealt with.</returns>
    /// <remarks>
    /// CorsixTH counts a member of staff as busy with a patient while the patient is in the room
    /// (<c>dealing_with_patient</c>) unless they meander; here, while the room has a patient at all.
    /// </remarks>
    internal static bool SettleIdleStaff(Simulation simulation, Humanoid staff, bool placed = false)
    {
        var tile = staff.Tile;
        if (!simulation.Map.Contains(tile.X, tile.Y) || simulation.Map[tile.X, tile.Y].RoomId is var roomId && roomId == 0
            || simulation.FindEntity(roomId) is not Room room || simulation.FindEntity(room.DoorId) is not RoomDoor door
            || staff.Behaviour is not IRoomStaff roomStaff)
        {
            return false;
        }

        if (room.IsActive && Owner(simulation, room) == staff.Employer)
        {
            if (string.Equals(room.RoomType, ResearchRoom, StringComparison.Ordinal))
            {
                // ResearchRoom's maximum_staff: as many researchers as desks.
                if (staff is { Behaviour: DoctorBehaviour, Profile.IsResearcher: true } && CountResearchers(simulation, room) < CountObjects(simulation, room, "desk"))
                {
                    if (room.StaffId == 0)
                    {
                        room.StaffId = staff.Id;
                    }

                    roomStaff.GoToRoom(simulation, staff, room, tile);
                    return true;
                }
            }
            else if (Rules.TryGetValue(room.RoomType, out var rules) && rules.Staff == staff.Behaviour!.Kind && IsQualifiedFor(room.RoomType, staff))
            {
                if (placed && room.StaffId != 0 && room.StaffId != staff.Id && room.PatientId == 0 && simulation.FindEntity(room.StaffId) is Humanoid { Profile: not null } current)
                {
                    // The previous member of staff is not busy with a patient: send them out and appoint the newcomer.
                    StaffEmployment.Detach(simulation, current);
                    current.Behaviour = StaffHiring.NewBehaviour(current.Profile.Class);
                }

                if (room.StaffId == 0)
                {
                    room.StaffId = staff.Id;
                    roomStaff.GoToRoom(simulation, staff, room, tile);
                    return true;
                }
            }
        }

        // createLeaveAction, then MeanderAction: out of the door, and wander about from there.
        var outside = OutsideTile(room, door);
        return staff.WalkTo(simulation, outside);
    }

    private static int CountResearchers(Simulation simulation, Room room) =>
        simulation.Entities.Count(e => e is Humanoid { Behaviour: DoctorBehaviour doctor, IsFired: false } && doctor.RoomId == room.Id);

    private static int CountObjects(Simulation simulation, Room room, string objectType) =>
        simulation.Entities.Count(e => e is RoomObject placed && placed.RoomId == room.Id && string.Equals(placed.ObjectType, objectType, StringComparison.Ordinal));

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

        if (string.Equals(room.RoomType, Staff.Tiredness.StaffRoomType, StringComparison.Ordinal))
        {
            // No patients and no staff of its own: tired staff come by themselves, counted at its door.
            EnsureQueue(simulation, room);
            return;
        }

        if (string.Equals(room.RoomType, WardRoom, StringComparison.Ordinal))
        {
            UpdateWard(simulation, room);
            return;
        }

        if (string.Equals(room.RoomType, OperatingTheatreRoom, StringComparison.Ordinal))
        {
            UpdateOperatingTheatre(simulation, room);
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

    /// <summary>
    /// A room emptying to be edited (<c>Room:tryToEdit</c>, <c>Room:deactivate</c>): patients on their way or queueing go
    /// elsewhere; once no patient is in it, its staff are let go and walk out; when nobody is left inside it can be
    /// edited.
    /// </summary>
    internal static void UpdateClosing(Simulation simulation, Room room)
    {
        foreach (var entity in simulation.Entities.ToList())
        {
            if (entity is Humanoid { Behaviour: PatientBehaviour patient } humanoid && patient.TargetId == room.Id)
            {
                patient.LeaveClosedRoom(simulation, humanoid, room);
            }
        }

        if (room.PatientId == 0)
        {
            foreach (var entity in simulation.Entities)
            {
                if (entity is Humanoid { Behaviour: IRoomStaff staff, Profile: { } profile } humanoid && staff.RoomId == room.Id)
                {
                    StaffEmployment.Detach(simulation, humanoid);
                    humanoid.Behaviour = StaffHiring.NewBehaviour(profile.Class);
                }
            }

            room.StaffId = 0;
            room.StaffPresent = false;
        }

        if (room.PatientId != 0 || simulation.Entities.Any(e => e is Humanoid h && (room.Area.Contains(h.Tile) || room.Area.Contains(h.StepTarget))))
        {
            return;
        }

        room.IsClosing = false;
    }

    /// <summary>
    /// <c>CallsDispatcher:callForStaff</c> and <c>answerCall</c>: of the free staff of the kind the room needs, the one
    /// with the best score for it (<see cref="CallScore"/>) goes, unless another room waiting for staff scores better
    /// for them; that room takes them when it calls, as CorsixTH's free staff answer the most urgent call.
    /// </summary>
    private static void CallStaff(Simulation simulation, Room room)
    {
        if (!Rules.TryGetValue(room.RoomType, out var rules) || simulation.FindEntity(room.DoorId) is not RoomDoor door)
        {
            return;
        }

        var owner = Owner(simulation, room);
        var target = door.InsideTile(room.Area);
        Humanoid? best = null;
        var bestScore = 0m;
        foreach (var entity in simulation.Entities)
        {
            if (entity is not Humanoid { Behaviour: { } behaviour } staff || !IsFreeFor(staff, rules.Staff, owner) || !IsQualifiedFor(room.RoomType, staff))
            {
                continue;
            }

            if (CallScore(simulation, room, target, staff) is { } score && (best is null || score < bestScore))
            {
                best = staff;
                bestScore = score;
            }
        }

        if (best is null || PrefersAnotherRoom(simulation, room, rules.Staff, owner, best, bestScore))
        {
            return;
        }

        room.StaffId = best.Id;
        ((IRoomStaff)best.Behaviour!).GoToRoom(simulation, best, room, target);
    }

    /// <summary><c>CallsDispatcher.verifyStaffForRoom</c>: idle staff of the kind, of the room's hospital, not in a room.</summary>
    private static bool IsFreeFor(Humanoid staff, BehaviourKind kind, int owner) =>
        staff.Behaviour is IRoomStaff { RoomId: 0 } and not NurseBehaviour { IsFree: false }
        && staff.Behaviour.Kind == kind && staff.Employer == owner;

    /// <summary>
    /// <c>CallsDispatcher.getPriorityForRoom</c>, smaller is more urgent: the path to the room, less five for each patient
    /// waiting and a great deal for an emergency patient among them, less forty times the staff's fatigue (the tired go
    /// first, so that they do not all rest at once) and fifty for staff not in a room; null if they cannot get there.
    /// The specialists' bonus is left out until rooms need them.
    /// </summary>
    internal static decimal? CallScore(Simulation simulation, Room room, TilePosition entrance, Humanoid staff)
    {
        if (simulation.Pathfinder.FindDistance(simulation.Map, staff.StepTarget, entrance) is not { } distance)
        {
            return null;
        }

        decimal score = distance;
        if (room.DoorQueue is { } queue)
        {
            score -= queue.Count * 5;
            if (queue.Members.Any(id => simulation.FindEntity(id) is Humanoid { Behaviour: PatientBehaviour { EmergencyIndex: > 0 } }))
            {
                score -= 200_000;
            }
        }

        score -= staff.Fatigue * 40;
        if (staff.Behaviour is IRoomStaff { RoomId: 0 })
        {
            score -= 50;
        }

        return score;
    }

    /// <summary>Whether another active room of the hospital, still without staff, has a better score for <paramref name="staff"/>.</summary>
    private static bool PrefersAnotherRoom(Simulation simulation, Room room, BehaviourKind kind, int owner, Humanoid staff, decimal score)
    {
        foreach (var entity in simulation.Entities)
        {
            if (entity is not Room { IsActive: true, StaffId: 0 } other || other.Id == room.Id
                || !Rules.TryGetValue(other.RoomType, out var rules) || rules.Staff != kind || !IsQualifiedFor(other.RoomType, staff) || Owner(simulation, other) != owner
                || simulation.FindEntity(other.DoorId) is not RoomDoor door)
            {
                continue;
            }

            if (CallScore(simulation, other, door.InsideTile(other.Area), staff) is { } otherScore && otherScore < score)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Lets the first in line in when the room may take them: it is active, its doctor is in and nobody
    /// else is being seen (<c>Room:tryAdvanceQueue</c>, <c>Room:canHumanoidEnter</c>).
    /// </summary>
    private static void TryAdvanceQueue(Simulation simulation, Room room)
    {
        if (room.DoorQueue is not { Count: > 0 } queue || room.PatientId != 0 || !room.StaffPresent
            || HandymanBehaviour.IsRepairingIn(simulation, room) || simulation.FindEntity(room.DoorId) is not RoomDoor door)
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
