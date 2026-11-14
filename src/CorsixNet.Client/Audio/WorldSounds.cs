// Sounds of the world: the entrance doors (objects/doors/entrance_right_door.lua's onOccupantChange) and earthquakes
// (hospitals/player_hospital.lua's tickEarthquake: the announcements at the warning's and the main quake's start, and
// the rumble while it shakes).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim;
using CorsixNet.Sim.Disasters;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Client.Audio;

/// <summary>
/// The entrance doors' whoosh when they open or close (eledoor2.wav, at the door), an earthquake's announcement
/// (quake001 to quake004) when the shaking starts, and its rumble (quake2.wav, or quake.wav) again whenever the last has
/// ended while it shakes.
/// </summary>
internal sealed class WorldSounds(Random random)
{
    private Dictionary<int, bool> _doors = [];
    private bool _watching;
    private bool _shaking;
    private long _rumbleUntil;

    /// <summary>Forgets what was seen, for a new or loaded game.</summary>
    public void Reset() => _watching = false;

    /// <summary>
    /// Plays what is due: <paramref name="playAt"/> a sound at a tile, <paramref name="play"/> one heard everywhere alike
    /// that returns how long it lasts (zero when it is not in the archive); <paramref name="now"/> in milliseconds.
    /// </summary>
    /// <param name="paused">The game is paused: the shaking stops (tickEarthquake's "pause").</param>
    /// <param name="announce">Hands an announcement to the announcer.</param>
    public void Update(Simulation simulation, long now, bool paused, Action<string, TilePosition> playAt, Func<string, TimeSpan> play, Action<string>? announce = null)
    {
        var doors = new Dictionary<int, bool>();
        foreach (var door in simulation.Entities.OfType<EntranceDoor>().Where(door => door.IsRight))
        {
            doors[door.Id] = door.IsOpen;
            if (_watching && _doors.TryGetValue(door.Id, out var wasOpen) && wasOpen != door.IsOpen)
            {
                playAt("eledoor2.wav", door.Tile);
            }
        }

        var shaking = !paused && simulation.Earthquakes.Shake != EarthquakeShake.None;
        if (_watching && shaking && !_shaking)
        {
            var quake = $"quake00{random.Next(1, 5)}.wav";
            if (announce is not null)
            {
                announce(quake);
            }
            else
            {
                play(quake);
            }
        }

        if (shaking && now >= _rumbleUntil)
        {
            // The sound has another name in some languages' archives.
            var length = play("quake2.wav");
            if (length == TimeSpan.Zero)
            {
                length = play("quake.wav");
            }

            _rumbleUntil = now + Math.Max(250, (long)length.TotalMilliseconds);
        }

        _watching = true;
        _doors = doors;
        _shaking = shaking;
    }
}
