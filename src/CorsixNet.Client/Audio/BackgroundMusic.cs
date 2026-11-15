// The background music's playlist, ported from CorsixTH CorsixTH/Lua/audio.lua (background_playlist,
// playRandomBackgroundTrack, playNextOrPreviousBackgroundTrack, pauseBackgroundTrack, stopBackgroundTrack,
// playBackgroundTrack, onMusicOver, setBackgroundVolume, readPlaylistPreferences, updatePlaylistPreferences),
// game_ui.lua (UI:togglePlayMusic) and dialogs/jukebox.lua (UIJukebox:toggleTrack, loopTrack).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Formats;

namespace CorsixNet.Client.Audio;

/// <summary>A piece of the playlist: the track and whether the jukebox lets it play.</summary>
internal sealed class PlaylistEntry(MusicTrack track)
{
    public MusicTrack Track { get; } = track;

    public bool Enabled { get; set; } = true;

    /// <summary>Whether the jukebox loops this track (<c>loop</c>).</summary>
    public bool Loop { get; set; }

    /// <summary>Whether looping another track turned this one off (<c>enabled_before_loop</c>).</summary>
    public bool EnabledBeforeLoop { get; set; }
}

/// <summary>
/// The game's background music: the tracks of <c>SOUND/MIDI</c> played on the MIDI player, one picked at random to
/// start, then the next enabled one each time one ends. Nothing plays while <c>play_music</c> is off.
/// </summary>
/// <param name="tracks">The playlist, in the list file's order.</param>
/// <param name="player">The MIDI player, or null when the system has no MIDI output (then nothing plays).</param>
/// <param name="read">Reads a track's file (RNC compressed or not).</param>
/// <param name="recorded">Plays the player's own recorded music (OGG, WAV, MP3), or null for none.</param>
internal sealed class BackgroundMusic(IReadOnlyList<MusicTrack> tracks, MidiPlayer? player, Func<string, byte[]> read, Random random, WaveformPlayer? recorded = null) : IDisposable
{
    private MidiPlayer? _player = player;

    public IReadOnlyList<PlaylistEntry> Playlist { get; } = [.. tracks.Select(track => new PlaylistEntry(track))];

    /// <summary>The instructional formats played on the MIDI player: the game's XMI and standard MIDI files.</summary>
    public static readonly string[] InstructionalExtensions = ["XMI", "MID", "MIDI", "KAR"];

    /// <summary>Whether there is music to play (<c>has_bg_music</c>): tracks, and something to play them on.</summary>
    public bool HasMusic => Playlist.Any(entry => CanPlay(entry.Track));

    private static bool IsRecorded(MusicTrack track) => PcmSources.Extensions.Contains(Path.GetExtension(track.Path).TrimStart('.').ToUpperInvariant());

    private bool CanPlay(MusicTrack track) => IsRecorded(track) ? recorded is not null : _player is not null;

    /// <summary>The track playing or paused (<c>background_music</c>), or null when stopped.</summary>
    public int? Current { get; private set; }

    /// <summary>Whether the track is paused (<c>background_paused</c>).</summary>
    public bool IsPaused { get; private set; }

    /// <summary>Whether music may play (<c>config.play_music</c>).</summary>
    public bool PlayMusic { get; set; } = true;

    /// <summary><c>playRandomBackgroundTrack</c>: one of the enabled tracks, at random.</summary>
    public void PlayRandom()
    {
        if (!HasMusic)
        {
            return;
        }

        var enabled = Enumerable.Range(0, Playlist.Count).Where(i => Playlist[i].Enabled).ToList();
        if (enabled.Count > 0)
        {
            Play(enabled[random.Next(enabled.Count)]);
        }
    }

    /// <summary><c>playNextBackgroundTrack</c>.</summary>
    public void PlayNext() => PlayNextOrPrevious(1);

    /// <summary><c>playPreviousBackgroundTrack</c>.</summary>
    public void PlayPrevious() => PlayNextOrPrevious(-1);

    /// <summary>
    /// <c>playBackgroundTrack</c>: plays a track if <c>play_music</c> allows it; a track that cannot be read or played is
    /// disabled.
    /// </summary>
    public void Play(int index)
    {
        if (index < 0 || index >= Playlist.Count || !PlayMusic || !CanPlay(Playlist[index].Track))
        {
            return;
        }

        var track = Playlist[index].Track;
        Stop();
        try
        {
            if (IsRecorded(track))
            {
                if (!recorded!.Play(track.Path))
                {
                    // loadMusicAsync's failure: the file is left out of the playlist.
                    Playlist[index].Enabled = false;
                    return;
                }
            }
            else if (Path.GetExtension(track.Path).ToUpperInvariant() is ".MID" or ".MIDI" or ".KAR")
            {
                _player!.PlayMidiFile(read(track.Path));
            }
            else
            {
                // The game's own music, XMI converted as CorsixTH's MIDI player does.
                _player!.PlayXmi(read(track.Path));
            }

            Current = index;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Failed to play midi: {e.Message}");
            Playlist[index].Enabled = false;
        }
    }

    /// <summary>Whether the track playing is recorded music rather than MIDI.</summary>
    private bool PlayingRecorded => Current is { } index && IsRecorded(Playlist[index].Track);

    /// <summary><c>pauseBackgroundTrack</c>: pauses the track, or goes on with it; whether it is paused now.</summary>
    public bool TogglePause()
    {
        if (Current is null)
        {
            return false;
        }

        IsPaused = !IsPaused;
        if (PlayingRecorded)
        {
            if (IsPaused)
            {
                recorded?.Pause();
            }
            else
            {
                recorded?.Resume();
            }
        }
        else if (IsPaused)
        {
            _player?.Pause();
        }
        else
        {
            _player?.Resume();
        }

        return IsPaused;
    }

    /// <summary><c>stopBackgroundTrack</c>: stops the music for now, leaving <c>play_music</c> as it is.</summary>
    public void Stop()
    {
        if (IsPaused && !PlayingRecorded)
        {
            // Resume first, as stopBackgroundTrack does, so that the player is not left waiting.
            _player?.Resume();
        }

        IsPaused = false;
        if (Current is not null)
        {
            _player?.Stop();
            recorded?.Stop();
        }

        Current = null;
    }

    /// <summary><c>setBackgroundVolume</c>.</summary>
    public void SetVolume(decimal volume)
    {
        var level = (double)Math.Clamp(volume, 0m, 1m);
        _player?.SetVolume(level);
        recorded?.SetVolume(level);
    }

    /// <summary>
    /// <c>UI:togglePlayMusic</c>: with nothing playing, music is allowed and a random track starts; otherwise it is
    /// turned off and stops. Returns the new <c>play_music</c>.
    /// </summary>
    public bool TogglePlayMusic()
    {
        if (Current is null)
        {
            PlayMusic = true;
            PlayRandom();
        }
        else
        {
            PlayMusic = false;
            Stop();
        }

        return PlayMusic;
    }

    /// <summary><c>onMusicOver</c>: once a track has ended, the next one plays.</summary>
    public void Update()
    {
        var over = (_player?.TakeMusicOver() ?? false) | (recorded?.TakeMusicOver() ?? false);
        if (over && Current is not null)
        {
            PlayNext();
        }
    }

    public void Dispose()
    {
        _player?.Dispose();
        recorded?.Dispose();
    }

    /// <summary>
    /// <c>Audio:initMidiPlayer</c> after the MIDI API or port changes: the new player takes over at the volume set, and
    /// the track playing starts again on it.
    /// </summary>
    public void SetPlayer(MidiPlayer? next, decimal volume)
    {
        var playing = Current;
        Stop();
        _player?.Dispose();
        _player = next;
        SetVolume(volume);
        if (playing is { } index)
        {
            Play(index);
        }
    }

    /// <summary>
    /// <c>UIJukebox:toggleTrack</c>: a track on or off; turning off the one playing stops it and plays another at random.
    /// </summary>
    public void ToggleTrack(int index)
    {
        var entry = Playlist[index];
        entry.Enabled = !entry.Enabled;
        if (!entry.Enabled && Current == index)
        {
            Stop();
            PlayRandom();
        }
    }

    /// <summary>
    /// <c>UIJukebox:loopTrack</c>: the track playing loops, every other enabled one turned off meanwhile; again, the loop
    /// ends and those come back on.
    /// </summary>
    public void ToggleLoop()
    {
        var index = Current ?? 0;
        if (index >= Playlist.Count)
        {
            return;
        }

        var looping = !Playlist[index].Loop;
        Playlist[index].Loop = looping;
        for (var i = 0; i < Playlist.Count; i++)
        {
            if (i == index)
            {
                continue;
            }

            if (looping && Playlist[i].Enabled)
            {
                Playlist[i].EnabledBeforeLoop = true;
                ToggleTrack(i);
            }
            else if (!looping && Playlist[i].EnabledBeforeLoop)
            {
                Playlist[i].EnabledBeforeLoop = false;
                ToggleTrack(i);
            }
        }
    }

    /// <summary><c>readPlaylistPreferences</c>: the tracks named in the file, one a line, start disabled.</summary>
    public void ReadDisabledTracks(IEnumerable<string> lines)
    {
        var disabled = lines.ToHashSet(StringComparer.Ordinal);
        foreach (var entry in Playlist)
        {
            entry.Enabled = !disabled.Contains(PreferenceName(entry.Track));
        }
    }

    /// <summary><c>updatePlaylistPreferences</c>: the disabled tracks' names, for the file.</summary>
    public IEnumerable<string> DisabledTracks() => Playlist.Where(entry => !entry.Enabled).Select(entry => PreferenceName(entry.Track));

    /// <summary>A track's name in the file: as CorsixTH's <c>filename</c>, Sound, Midi and the file's name joined by the path separator.</summary>
    private static string PreferenceName(MusicTrack track) => string.Join(Path.DirectorySeparatorChar, "Sound", "Midi", Path.GetFileName(track.Path));

    /// <summary><c>playNextOrPreviousBackgroundTrack</c>: the next enabled track after the current one, or before it.</summary>
    private void PlayNextOrPrevious(int direction)
    {
        if (!HasMusic)
        {
            return;
        }

        if (Current is not { } index)
        {
            PlayRandom();
            return;
        }

        for (var i = 1; i <= Playlist.Count; i++)
        {
            var next = (((index + (direction * i)) % Playlist.Count) + Playlist.Count) % Playlist.Count;
            if (Playlist[next].Enabled)
            {
                Play(next);
                return;
            }
        }
    }
}
