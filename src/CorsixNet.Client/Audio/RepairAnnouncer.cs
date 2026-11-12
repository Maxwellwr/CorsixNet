// The repair announcement, ported from CorsixTH CorsixTH/Lua/hospitals/player_hospital.lua (PlayerHospital:announceRepair),
// entities/machine.lua (callHandymanForRepairIfNecessary) and the rooms' handyman_call_sound (rooms/*.lua).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Client.Audio;

/// <summary>
/// Announces a machine of the player's that has just started to need a repair urgently (fewer than four uses left, when
/// CorsixTH queues or upgrades the handyman's task and calls <c>announceRepair</c>): the machine warning, then the room's
/// call for a handyman, unless an earthquake is shaking or the hospital has no handyman.
/// </summary>
/// <remarks>
/// The simulation's handymen look for worn machines themselves rather than through a task list, so the moment is found
/// here by watching each machine's <c>isBreaking</c> go from false to true. Machines already breaking when the watching
/// starts (a game loaded) are not announced. The announcements play as sound effects; CorsixTH's announcer queue and
/// priorities are not ported.
/// </remarks>
internal sealed class RepairAnnouncer
{
    /// <summary>The rooms' <c>handyman_call_sound</c>; the operating theatre and scanner have none in CorsixTH.</summary>
    private static readonly Dictionary<string, string> CallSounds = new(StringComparer.Ordinal)
    {
        ["blood_machine"] = "maint015.wav",
        ["cardiogram"] = "maint010.wav",
        ["decontamination"] = "maint012.wav",
        ["dna_fixer"] = "maint006.wav",
        ["electrolysis"] = "maint008.wav",
        ["fracture_clinic"] = "maint014.wav",
        ["hair_restoration"] = "maint007.wav",
        ["inflation"] = "maint013.wav",
        ["jelly_vat"] = "maint009.wav",
        ["slack_tongue"] = "maint004.wav",
        ["ultrascan"] = "maint016.wav",
        ["x_ray"] = "maint005.wav",
    };

    private HashSet<int>? _breaking;

    /// <summary>Forgets the machines seen, for a new or loaded game.</summary>
    public void Reset() => _breaking = null;

    /// <summary>The announcements to play for machines that have started breaking since the last call.</summary>
    public List<string> Update(Simulation simulation, int player)
    {
        var sounds = new List<string>();
        var breaking = new HashSet<int>();
        foreach (var entity in simulation.Entities)
        {
            if (entity is not RoomObject { Machine.IsBreaking: true, IsDestroyed: false } machine
                || simulation.FindEntity(machine.RoomId) is not Room { IsCrashed: false } room
                || RoomVisits.Owner(simulation, room) != player)
            {
                continue;
            }

            breaking.Add(machine.Id);
            if (_breaking is null || _breaking.Contains(machine.Id))
            {
                continue;
            }

            sounds.Add("machwarn.wav");

            // During an earthquake's shaking the call is left out, so as not to repeat it for every machine.
            var quake = simulation.Earthquakes;
            if ((quake.IsActive && quake.WarningTimer == 0) || !HasHandyman(simulation, player))
            {
                continue;
            }

            if (CallSounds.TryGetValue(room.RoomType, out var call))
            {
                sounds.Add(call);
            }
        }

        _breaking = breaking;
        return sounds;
    }

    private static bool HasHandyman(Simulation simulation, int player) =>
        simulation.Entities.Any(e => e is Humanoid { Profile.Class: StaffClass.Handyman, IsFired: false } h && h.Employer == player);
}
