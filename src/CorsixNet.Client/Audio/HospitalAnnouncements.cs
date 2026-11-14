// Announcements the hospital's events make, ported from CorsixTH CorsixTH/Lua/calls_dispatcher.lua (callForStaff's
// room call_sound, once until staff come: room.sound_played), epidemic.lua (announceStartOfEpidemic,
// announceEndOfEpidemic, markForVaccination), cheats.lua (announceCheat), hospital.lua (Hospital:tick's background sounds),
// player_hospital.lua (msgCured's cheer, msgKilled's boo, announceStaffLeave, onEndDay's VIP announcement), the staff's
// leave_sounds and inspector.lua (Inspector:announce, from Epidemic:tryAnnounceInspector).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim;
using CorsixNet.Sim.Disasters;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Client.Audio;

/// <summary>
/// Watches the local player's hospital for what CorsixTH announces: a room calling for staff it lacks, an epidemic
/// revealed and its cover-up's result, a cheat, and now and then a background sound (a phone, a cough). What was so
/// when the watching starts (a game loaded) is not announced. They play as sound effects; the announcer's queue and
/// priorities are not ported.
/// </summary>
internal sealed class HospitalAnnouncements(Random random)
{
    /// <summary>The rooms' <c>call_sound</c>; the toilets, staff room and training room have none.</summary>
    private static readonly Dictionary<string, string> CallSounds = new(StringComparer.Ordinal)
    {
        ["blood_machine"] = "reqd006.wav",
        ["cardiogram"] = "reqd001.wav",
        ["decontamination"] = "reqd024.wav",
        ["dna_fixer"] = "reqd015.wav",
        ["electrolysis"] = "reqd019.wav",
        ["fracture_clinic"] = "reqd004.wav",
        ["general_diag"] = "reqd021.wav",
        ["gp"] = "reqd008.wav",
        ["hair_restoration"] = "reqd016.wav",
        ["inflation"] = "reqd014.wav",
        ["jelly_vat"] = "reqd020.wav",
        ["operating_theatre"] = "reqd010.wav",
        ["pharmacy"] = "reqd012.wav",
        ["psych"] = "reqd003.wav",
        ["research"] = "reqd023.wav",
        ["scanner"] = "reqd002.wav",
        ["slack_tongue"] = "reqd005.wav",
        ["ultrascan"] = "reqd007.wav",
        ["ward"] = "reqd009.wav",
        ["x_ray"] = "reqd013.wav",
    };

    private bool _watching;
    private HashSet<int> _calling = [];
    private Epidemic? _epidemic;
    private bool _revealed;
    private bool _cheated;
    private long _hours;
    private int _cured;
    private int _deaths;
    private HashSet<int> _marked = [];
    private HashSet<int> _fired = [];
    private readonly HashSet<int> _announcedVisitors = [];
    private readonly List<(string Name, AnnouncementPriority Priority)> _announcements = [];
    private bool _heatingBroken;

    /// <summary>The announcements for the announcer found by the last <see cref="Update"/>; the list returned holds the plain sounds.</summary>
    public List<(string Name, AnnouncementPriority Priority)> TakeAnnouncements()
    {
        var taken = _announcements.ToList();
        _announcements.Clear();
        return taken;
    }

    /// <summary>Forgets what was seen, for a new or loaded game.</summary>
    public void Reset() => _watching = false;

    /// <summary>The sounds to play for what has happened since the last call.</summary>
    public List<string> Update(Simulation simulation, int player)
    {
        var sounds = new List<string>();
        var hospital = simulation.Hospitals[player];

        // callForStaff: an active room short of its staff calls, once until they come.
        var calling = new HashSet<int>();
        foreach (var room in simulation.Entities.OfType<Room>())
        {
            if (room.IsActive && !room.IsCrashed && CallSounds.ContainsKey(room.RoomType) && RoomBuilder.IsOwnedBy(simulation.Map, room, player) && LacksStaff(simulation, room))
            {
                calling.Add(room.Id);
                if (_watching && !_calling.Contains(room.Id))
                {
                    _announcements.Add((CallSounds[room.RoomType], AnnouncementPriority.Normal));
                }
            }
        }

        // Epidemic:revealEpidemic and sendResultFax.
        var epidemic = hospital.Epidemics.Current;
        if (_watching && epidemic is { IsRevealed: true } && (!ReferenceEquals(epidemic, _epidemic) || !_revealed))
        {
            _announcements.Add(($"EPID00{random.Next(1, 5)}.wav", AnnouncementPriority.Critical));
        }

        if (_watching && _epidemic is { Outcome: EpidemicOutcome.CoveredUp or EpidemicOutcome.Fined or EpidemicOutcome.Evacuated } ended && !ReferenceEquals(epidemic, ended))
        {
            _announcements.Add(($"EPID00{random.Next(5, 9)}.wav", AnnouncementPriority.Critical));
        }

        // Cheats:announceCheat.
        if (_watching && hospital.Cheated && !_cheated)
        {
            _announcements.Add(($"cheat00{random.Next(1, 4)}.wav", AnnouncementPriority.Critical));
        }

        // msgCured and msgKilled: the cheer and the boo, always heard.
        if (_watching && hospital.Cured > _cured)
        {
            sounds.Add("cheer.wav");
        }

        if (_watching && hospital.Deaths > _deaths)
        {
            sounds.Add("boo.wav");
        }

        // Epidemic:markForVaccination.
        var marked = simulation.Entities.OfType<Humanoid>()
            .Where(h => h.HospitalPlayer == player && h.Behaviour is PatientBehaviour { IsMarkedForVaccination: true })
            .Select(h => h.Id)
            .ToHashSet();
        if (_watching && marked.Except(_marked).Any())
        {
            sounds.Add("vaccin.wav");
        }

        // Staff:fire's announceStaffLeave: one of the leaver's leave_sounds.
        var fired = new HashSet<int>();
        foreach (var leaver in simulation.Entities.OfType<Humanoid>().Where(h => h is { IsFired: true, Profile: not null } && h.Employer == player))
        {
            fired.Add(leaver.Id);
            if (_watching && !_fired.Contains(leaver.Id) && LeaveSounds(leaver.Profile!.Class) is { Length: > 0 } leave)
            {
                // leave_priority: High, Critical for a receptionist (heard even with nobody at the desk).
                _announcements.Add((leave[random.Next(leave.Length)], leaver.Profile.Class == StaffClass.Receptionist ? AnnouncementPriority.Critical : AnnouncementPriority.High));
            }
        }

        // onEndDay: a VIP walking into the hospital is announced (vip001 to vip005), once; tryAnnounceInspector: the
        // Health Inspector (vip008).
        foreach (var visitor in simulation.Entities.OfType<Humanoid>().Where(h => h.Behaviour is VipBehaviour or InspectorBehaviour))
        {
            if (!_announcedVisitors.Contains(visitor.Id) && InHospital(simulation, player, visitor.Tile))
            {
                _announcedVisitors.Add(visitor.Id);
                if (_watching)
                {
                    _announcements.Add((visitor.Behaviour is VipBehaviour ? $"vip00{random.Next(1, 6)}.wav" : "vip008.wav", AnnouncementPriority.High));
                }
            }
        }

        // adviseBoilerBreakdown: an apology, sorry002 or sorry004 with the heating off, sorry003 or sorry004 at full.
        if (_watching && hospital.IsHeatingBroken && !_heatingBroken)
        {
            var full = hospital.RadiatorHeat >= Sim.Economy.Hospital.MaxRadiatorHeat;
            _announcements.Add((random.Next(2) == 0 ? "sorry004.wav" : full ? "sorry003.wav" : "sorry002.wav", AnnouncementPriority.Normal));
        }

        // Hospital:tick: one hour in a hundred, with a GP's office and more than two patients, a background sound.
        if (_watching && simulation.Hours > _hours && random.Next(1, 101) == 3
            && simulation.Entities.OfType<Room>().Any(room => room.RoomType == "gp" && RoomBuilder.IsOwnedBy(simulation.Map, room, player))
            && simulation.Entities.Count(e => e is Humanoid { Behaviour: PatientBehaviour } patient && patient.HospitalPlayer == player) > 2)
        {
            sounds.Add($"ispot{random.Next(1, 26):000}.wav");
        }

        _watching = true;
        _calling = calling;
        _epidemic = epidemic;
        _revealed = epidemic?.IsRevealed ?? false;
        _cheated = hospital.Cheated;
        _hours = simulation.Hours;
        _heatingBroken = hospital.IsHeatingBroken;
        _cured = hospital.Cured;
        _deaths = hospital.Deaths;
        _marked = marked;
        _fired = fired;
        return sounds;
    }

    private static bool InHospital(Simulation simulation, int player, Sim.Maps.TilePosition tile)
    {
        var map = simulation.Map;
        return map.Contains(tile.X, tile.Y) && map[tile.X, tile.Y].Has(Sim.Maps.TileFlags.Hospital) && map.GetParcelOwner(map[tile.X, tile.Y].Parcel) == player + 1;
    }

    /// <summary>The staff classes' leave_sounds (doctor.lua, nurse.lua, handyman.lua, receptionist.lua).</summary>
    private static string[] LeaveSounds(StaffClass staffClass) => staffClass switch
    {
        StaffClass.Doctor => ["sack001.wav", "sack002.wav", "sack003.wav"],
        StaffClass.Nurse => ["sack004.wav", "sack005.wav"],
        StaffClass.Handyman => ["sack006.wav"],
        StaffClass.Receptionist => ["sack007.wav", "sack008.wav"],
        _ => [],
    };

    /// <summary>
    /// Whether the room is short of the staff it needs (Room:getMissingStaff): two surgeons for the operating theatre,
    /// a nurse for the ward, otherwise its member of staff.
    /// </summary>
    private static bool LacksStaff(Simulation simulation, Room room) => room.RoomType switch
    {
        "operating_theatre" => simulation.Entities.Count(e => e is Humanoid { Behaviour: DoctorBehaviour doctor } && doctor.RoomId == room.Id) < 2,
        "ward" => !simulation.Entities.Any(e => e is Humanoid { Behaviour: NurseBehaviour nurse } && nurse.RoomId == room.Id),
        _ => room.StaffId == 0,
    };
}
