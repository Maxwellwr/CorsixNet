// The gates to hell's lava sounds, ported from CorsixTH CorsixTH/Lua/hospitals/player_hospital.lua
// (PlayerHospital:showGatesToHell's playEntitySounds), audio.lua (Audio:playEntitySounds, getSilenceLengths,
// entitySoundsHandler) and humanoid_actions/die.lua (the sounds stop as the grim reaper leaves).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Content.Interface;
using CorsixNet.Sim;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;

namespace CorsixNet.Client.Audio;

/// <summary>
/// While the gates to hell are open, one of the <c>LAVA00*.WAV</c> sounds after another at the hole, with a random
/// silence between them; they stop when the grim reaper leaves and the hole closes.
/// </summary>
/// <remarks>
/// showGatesToHell's silences ({0, 1350, 1150, 950, 750, 350} to {0, 1450, 1250, 1050, 850, 450} ms) are looked up by the
/// world's tick rate: 3 at Normal (1150 to 1250), 2 at Max speed (1350 to 1450), 1 at And then some more (none). The
/// slower speeds' tick rates (12, 56) fall outside the table, where CorsixTH gets no silence, and so none here either.
/// </remarks>
internal sealed class GatesToHellSounds
{
    /// <summary>The silences' bounds (getSilenceLengths' arrays), from tick rate 1.</summary>
    private static readonly (int Min, int Max)[] Silences = [(0, 0), (1350, 1450), (1150, 1250), (950, 1050), (750, 850), (350, 450)];

    private readonly Dictionary<int, long> _next = [];
    private readonly Random _random = new();

    /// <summary>
    /// The holes whose next sound is due at <paramref name="now"/> (milliseconds); <paramref name="play"/> plays one at a
    /// hole and returns how long it lasts.
    /// </summary>
    public void Update(Simulation simulation, GameSpeed speed, long now, Func<TilePosition, TimeSpan> play)
    {
        var open = new HashSet<int>();
        foreach (var entity in simulation.Entities)
        {
            if (entity is not Humanoid { Behaviour: GrimReaperBehaviour { Phase: not GrimReaperPhase.Closing } reaper } grim)
            {
                continue;
            }

            open.Add(grim.Id);

            // canSoundsBePlayed: nothing while the game is paused; the sequence goes on afterwards.
            if (speed == GameSpeed.Pause || (_next.TryGetValue(grim.Id, out var due) && now < due))
            {
                continue;
            }

            var length = play(reaper.HoleTile);
            var rate = TickRate(speed);
            var (min, max) = rate >= 1 && rate <= Silences.Length ? Silences[rate - 1] : (0, 0);
            _next[grim.Id] = now + (long)length.TotalMilliseconds + _random.Next(min, max + 1);
        }

        foreach (var gone in _next.Keys.Where(id => !open.Contains(id)).ToList())
        {
            _next.Remove(gone);
        }
    }

    /// <summary>World's tick_rate for the speed (world.lua's tick_rates; Speed Up's is 1).</summary>
    private static int TickRate(GameSpeed speed) => speed switch
    {
        GameSpeed.Slowest => 56,
        GameSpeed.Slower => 12,
        GameSpeed.Normal => 3,
        GameSpeed.MaxSpeed => 2,
        _ => 1,
    };
}
