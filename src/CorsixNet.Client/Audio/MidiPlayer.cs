// The MIDI player of the background music, ported from CorsixTH CorsixTH/Src/midi_player.cpp (midi_player::play_xmi,
// stop, pause, resume, set_volume, init_playback, playback_loop, send_stop_playback, send_all_notes_off,
// send_gsm_enable, send_control_change_volume, player_command_queue).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Diagnostics;
using CorsixNet.Formats;

namespace CorsixNet.Client.Audio;

/// <summary>
/// Plays an XMI song on a MIDI output from a thread of its own, as CorsixTH's <c>midi_player</c>: commands (stop, pause,
/// resume, a new volume) go to the thread through a queue, the events are sent when their time comes, and the end of
/// the song is reported (<c>midi_music_over_callback</c>) for <see cref="TakeMusicOver"/>.
/// </summary>
/// <remarks>
/// The volume scales the channels' volume controllers, CorsixTH's way without <c>use_master_volume_sysex</c>.
/// </remarks>
internal sealed class MidiPlayer(IMidiOutput output) : IDisposable
{
    private const int Channels = 16;
    private const byte MaxChannelVolume = 127;
    private const byte ControlChange = 0xB0;
    private const byte ControlVolume = 0x07;
    private const byte AllSoundOff = 0x78;
    private const byte ResetControllers = 0x79;
    private const byte AllNotesOff = 0x7B;
    private const byte Meta = 0xFF;
    private const byte EndOfTrack = 0x2F;

    private readonly Lock _outputLock = new();
    private readonly Queue<Command> _commands = new();
    private readonly object _commandsLock = new();
    private Thread? _thread;
    private double _volume = 1.0;
    private int _musicOver;

    private enum Command
    {
        Noop,
        Stop,
        Pause,
        Resume,
        SetVolume,
    }

    /// <summary>The volume, from 0 to 1.</summary>
    public double Volume => Volatile.Read(ref _volume);

    /// <summary><c>play_xmi</c>: stops what is playing and plays the song's events from the start.</summary>
    public void PlayXmi(ReadOnlySpan<byte> xmi)
    {
        var events = Xmi.Events(xmi);
        Stop();
        _thread = new Thread(() => PlaybackLoop(events)) { IsBackground = true, Name = "MIDI playback" };
        _thread.Start();
    }

    /// <summary><c>set_volume</c>.</summary>
    public void SetVolume(double volume)
    {
        Volatile.Write(ref _volume, Math.Clamp(volume, 0, 1));
        Push(Command.SetVolume);
    }

    /// <summary><c>stop</c>: the song stops for good and every channel is silenced and reset.</summary>
    public void Stop()
    {
        if (_thread is { } thread)
        {
            Push(Command.Stop);
            thread.Join();
            _thread = null;
        }

        lock (_commandsLock)
        {
            _commands.Clear();
        }

        lock (_outputLock)
        {
            SendStopPlayback();
        }
    }

    /// <summary><c>pause</c>: the notes stop and the song waits.</summary>
    public void Pause() => Push(Command.Pause);

    /// <summary><c>resume</c>: the song goes on from where it was paused.</summary>
    public void Resume() => Push(Command.Resume);

    /// <summary>Whether a song has come to its end since the last call (<c>SDL_USEREVENT_MUSIC_OVER</c>).</summary>
    public bool TakeMusicOver() => Interlocked.Exchange(ref _musicOver, 0) != 0;

    public void Dispose()
    {
        Stop();
        output.Dispose();
    }

    private void Push(Command command)
    {
        lock (_commandsLock)
        {
            _commands.Enqueue(command);
            Monitor.PulseAll(_commandsLock);
        }
    }

    /// <summary><c>player_command_queue::pop</c>: the next command, waiting for one when <paramref name="blocking"/>.</summary>
    private Command Pop(bool blocking)
    {
        lock (_commandsLock)
        {
            while (blocking && _commands.Count == 0)
            {
                Monitor.Wait(_commandsLock);
            }

            return _commands.Count == 0 ? Command.Noop : _commands.Dequeue();
        }
    }

    /// <summary>Waits until <paramref name="due"/> on the clock, or until a command comes in.</summary>
    private void WaitUntil(Stopwatch clock, TimeSpan due)
    {
        lock (_commandsLock)
        {
            var wait = due - clock.Elapsed;
            if (_commands.Count == 0 && wait > TimeSpan.Zero)
            {
                Monitor.Wait(_commandsLock, wait);
            }
        }
    }

    private void PlaybackLoop(IReadOnlyList<XmiEvent> events)
    {
        // init_playback: General MIDI on, and every channel's volume at the player's volume.
        lock (_outputLock)
        {
            output.Send([0xF0, 0x7E, 0x7F, 0x09, 0x01, 0xF7]);
            SendControlChangeVolume(Enumerable.Repeat(MaxChannelVolume, Channels).ToArray(), Volume);
        }

        var clock = Stopwatch.StartNew();
        var start = TimeSpan.Zero;
        var paused = false;
        var pauseTime = TimeSpan.Zero;
        var channelVolumes = Enumerable.Repeat(MaxChannelVolume, Channels).ToArray();
        var index = 0;
        var message = new List<byte>(8);
        while (index < events.Count)
        {
            switch (Pop(paused))
            {
                case Command.Stop:
                    return;
                case Command.Pause:
                    paused = true;
                    pauseTime = clock.Elapsed;
                    lock (_outputLock)
                    {
                        SendToAllChannels(AllNotesOff);
                    }

                    break;
                case Command.Resume:
                    if (paused)
                    {
                        paused = false;
                        start += clock.Elapsed - pauseTime;
                    }

                    break;
                case Command.SetVolume:
                    lock (_outputLock)
                    {
                        SendControlChangeVolume(channelVolumes, Volume);
                    }

                    break;
            }

            if (paused)
            {
                continue;
            }

            var current = events[index];
            if (current.Type == Meta && current.Data == EndOfTrack)
            {
                break;
            }

            var due = start + TimeSpan.FromMicroseconds(Xmi.ToMicroseconds(current.Time));
            if (due > clock.Elapsed)
            {
                // Commands are seen while waiting, as CorsixTH's loop sees them after its sleep.
                WaitUntil(clock, due);
                if (due > clock.Elapsed)
                {
                    continue;
                }
            }

            index++;
            if (current.Type == Meta)
            {
                // Meta events are not sent, except that the end of the track ends the song.
                continue;
            }

            message.Clear();
            message.Add(current.Type);
            if (current.Type is not (0xF0 or 0xF7))
            {
                // CorsixTH sends the first data byte of a system exclusive message too, which XMI leaves as 0 there.
                message.Add(current.Data);
            }

            message.AddRange(current.Buffer);
            if ((current.Type & 0xF0) == ControlChange && current.Data == ControlVolume && message.Count == 3)
            {
                // The track's channel volume, kept for a later change of volume, scaled by the player's.
                channelVolumes[current.Type & 0x0F] = message[2];
                message[2] = ScaledVolume(message[2], Volume);
            }

            lock (_outputLock)
            {
                output.Send(message.ToArray());
            }
        }

        Interlocked.Exchange(ref _musicOver, 1);
    }

    private static byte ScaledVolume(byte volume, double scale) => (byte)Math.Clamp((int)(scale * volume), 0, MaxChannelVolume);

    /// <summary><c>send_control_change_volume</c>: each channel's volume controller at its track volume times the player's.</summary>
    private void SendControlChangeVolume(byte[] channelVolumes, double volume)
    {
        for (var channel = 0; channel < Channels; channel++)
        {
            output.Send([(byte)(ControlChange | channel), ControlVolume, ScaledVolume(channelVolumes[channel], volume)]);
        }
    }

    /// <summary><c>send_stop_playback</c>: all notes off, all sound off and the controllers reset on every channel.</summary>
    private void SendStopPlayback()
    {
        for (var channel = 0; channel < Channels; channel++)
        {
            output.Send([(byte)(ControlChange | channel), AllNotesOff, 0]);
            output.Send([(byte)(ControlChange | channel), AllSoundOff, 0]);
            output.Send([(byte)(ControlChange | channel), ResetControllers, 0]);
        }
    }

    private void SendToAllChannels(byte code)
    {
        for (var channel = 0; channel < Channels; channel++)
        {
            output.Send([(byte)(ControlChange | channel), code, 0]);
        }
    }
}
