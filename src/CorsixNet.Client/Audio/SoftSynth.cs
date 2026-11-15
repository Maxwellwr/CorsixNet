// The software synthesiser the music plays on by default, in place of CorsixTH's SDL_mixer with FluidSynth and a
// SoundFont (CorsixTH/Lua/audio.lua's initMidiPlayer with no midi_api, CorsixTH/Lua/app.lua's findSoundFont): the MIDI
// player's messages go to MeltySynth (MIT, managed code) and its sound is streamed out through MonoGame, on every
// platform.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using MeltySynth;
using Microsoft.Xna.Framework.Audio;

namespace CorsixNet.Client.Audio;

/// <summary>Where the music's SoundFont is (<c>App:findSoundFont</c>).</summary>
internal static class SoundFonts
{
    /// <summary>
    /// The first that exists of: the <c>soundfont</c> setting, GeneralUser-GS.sf2, FluidR3_GM.sf2 or FluidR3.sf3 beside the
    /// game, and the Linux distributions' usual places; null if none does.
    /// </summary>
    public static string? Find(string? configured, string gameFolder, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        string?[] candidates =
        [
            configured is { Length: > 0 } ? configured : null,
            Path.Combine(gameFolder, "GeneralUser-GS.sf2"),
            Path.Combine(gameFolder, "FluidR3_GM.sf2"),
            Path.Combine(gameFolder, "FluidR3.sf3"),
            "/usr/share/soundfonts/default.sf2",
            "/usr/share/sounds/sf2/FluidR3_GM.sf2",
            "/usr/share/soundfonts/FluidR3_GM.sf2",
        ];
        return candidates.FirstOrDefault(path => path is not null && exists(path));
    }
}

/// <summary>
/// A MIDI output that synthesises the music itself with a SoundFont: channel messages go to the synthesiser (system
/// exclusive ones are left out, as it has none), and <see cref="Render"/> gives its sound.
/// </summary>
internal sealed class SoftSynthMidiOutput : IMidiOutput
{
    /// <summary>The rate the synthesiser renders at.</summary>
    public const int SampleRate = 44100;

    /// <summary>Frames rendered each time the sound stream asks for more: about 46 ms.</summary>
    private const int BufferFrames = 2048;

    private readonly Synthesizer _synthesizer;
    private readonly Lock _lock = new();
    private readonly float[] _left = new float[BufferFrames];
    private readonly float[] _right = new float[BufferFrames];
    private DynamicSoundEffectInstance? _stream;

    public SoftSynthMidiOutput(SoundFont soundFont) => _synthesizer = new Synthesizer(soundFont, SampleRate);

    /// <summary>Loads the SoundFont at <paramref name="path"/> and starts the sound streaming; null if it cannot be read or played.</summary>
    public static SoftSynthMidiOutput? Open(string path)
    {
        try
        {
            var output = new SoftSynthMidiOutput(new SoundFont(path));
            output.StartStream();
            return output;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or NoAudioHardwareException or InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"Failed to create midi player: {e.Message}");
            return null;
        }
    }

    public void Send(ReadOnlySpan<byte> message)
    {
        if (message.IsEmpty || message[0] >= 0xF0)
        {
            return;
        }

        var status = message[0];
        var data1 = message.Length > 1 ? message[1] : 0;
        var data2 = message.Length > 2 ? message[2] : 0;
        lock (_lock)
        {
            _synthesizer.ProcessMidiMessage(status & 0x0F, status & 0xF0, data1, data2);
        }
    }

    /// <summary>The next frames of sound as 16-bit stereo samples, left and right in turn.</summary>
    public void Render(Span<short> interleaved)
    {
        var frames = interleaved.Length / 2;
        var done = 0;
        while (done < frames)
        {
            var count = Math.Min(BufferFrames, frames - done);
            lock (_lock)
            {
                _synthesizer.Render(_left.AsSpan(0, count), _right.AsSpan(0, count));
            }

            for (var i = 0; i < count; i++)
            {
                interleaved[(done + i) * 2] = ToSample(_left[i]);
                interleaved[((done + i) * 2) + 1] = ToSample(_right[i]);
            }

            done += count;
        }
    }

    public void Dispose()
    {
        _stream?.Stop();
        _stream?.Dispose();
        _stream = null;
    }

    private static short ToSample(float value) => (short)Math.Clamp((int)(value * 32767f), short.MinValue, short.MaxValue);

    /// <summary>Streams the sound out, a buffer at a time as the stream asks, three kept ahead.</summary>
    private void StartStream()
    {
        _stream = new DynamicSoundEffectInstance(SampleRate, AudioChannels.Stereo);
        var samples = new short[BufferFrames * 2];
        var bytes = new byte[samples.Length * 2];
        void Submit()
        {
            Render(samples);
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            _stream?.SubmitBuffer(bytes);
        }

        _stream.BufferNeeded += (_, _) =>
        {
            while (_stream is { PendingBufferCount: < 3 })
            {
                Submit();
            }
        };
        for (var i = 0; i < 3; i++)
        {
            Submit();
        }

        _stream.Play();
    }
}
