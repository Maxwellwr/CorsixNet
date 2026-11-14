// Announcements the hospital's events make, ported from CorsixTH CorsixTH/Lua/calls_dispatcher.lua (callForStaff's
// room call_sound, once until staff come: room.sound_played), epidemic.lua (announceStartOfEpidemic,
// announceEndOfEpidemic), cheats.lua (announceCheat) and hospital.lua (Hospital:tick's background sounds).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim;
using CorsixNet.Sim.Disasters;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Rooms;

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
                    sounds.Add(CallSounds[room.RoomType]);
                }
            }
        }

        // Epidemic:revealEpidemic and sendResultFax.
        var epidemic = hospital.Epidemics.Current;
        if (_watching && epidemic is { IsRevealed: true } && (!ReferenceEquals(epidemic, _epidemic) || !_revealed))
        {
            sounds.Add($"EPID00{random.Next(1, 5)}.wav");
        }

        if (_watching && _epidemic is { Outcome: EpidemicOutcome.CoveredUp or EpidemicOutcome.Fined or EpidemicOutcome.Evacuated } ended && !ReferenceEquals(epidemic, ended))
        {
            sounds.Add($"EPID00{random.Next(5, 9)}.wav");
        }

        // Cheats:announceCheat.
        if (_watching && hospital.Cheated && !_cheated)
        {
            sounds.Add($"cheat00{random.Next(1, 4)}.wav");
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
        return sounds;
    }

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
