// The player's own recorded music, which CorsixTH plays with SDL_mixer (CorsixTH/Lua/audio.lua: allowed_waveform_formats,
// playBackgroundTrack's loadMusicAsync and playMusic, pauseBackgroundTrack, stopBackgroundTrack, setBackgroundVolume):
// Ogg Vorbis through NVorbis (which MonoGame brings), MP3 through NLayer, Opus through Concentus (BSD) and
// Concentus.Oggfile (MIT), FLAC through FlacDecoder and plain WAV, decoded a little at a time and streamed out through
// MonoGame. SDL_mixer's other formats (WavPack, AIFF, the tracker modules) are not played.

using System.Buffers.Binary;
using Microsoft.Xna.Framework.Audio;
using NLayer;
using NVorbis;

namespace CorsixNet.Client.Audio;

/// <summary>Decoded sound, read a buffer at a time as interleaved floats from -1 to 1.</summary>
internal interface IPcmSource : IDisposable
{
    int SampleRate { get; }

    int Channels { get; }

    /// <summary>Fills as much of <paramref name="buffer"/> as there is left; 0 at the end.</summary>
    int Read(float[] buffer);
}

/// <summary>The recorded formats CorsixNet plays of SDL_mixer's.</summary>
internal static class PcmSources
{
    /// <summary>The file extensions of the recorded music played (upper case, no dot).</summary>
    public static readonly string[] Extensions = ["OGG", "OPUS", "FLAC", "WAV", "WAVE", "MP3"];

    /// <summary>Opens a file by its extension; null for a format not played.</summary>
    /// <exception cref="InvalidDataException">The file is broken or of a kind not handled.</exception>
    public static IPcmSource? Open(string path, Stream stream) =>
        Path.GetExtension(path).TrimStart('.').ToUpperInvariant() switch
        {
            "OGG" => new VorbisSource(stream),
            "MP3" => new Mp3Source(stream),
            "OPUS" => new OpusSource(stream),
            "FLAC" => new FlacDecoder(stream),
            "WAV" or "WAVE" => WavSource.Read(stream),
            _ => null,
        };

    private sealed class VorbisSource(Stream stream) : IPcmSource
    {
        private readonly VorbisReader _reader = new(stream, closeOnDispose: true);

        public int SampleRate => _reader.SampleRate;

        public int Channels => _reader.Channels;

        public int Read(float[] buffer) => _reader.ReadSamples(buffer, 0, buffer.Length);

        public void Dispose() => _reader.Dispose();
    }

    /// <summary>Ogg Opus, decoded to stereo at 48 kHz (Opus's own rate) by Concentus in managed code.</summary>
    private sealed class OpusSource : IPcmSource
    {
        private readonly Stream _stream;
        private readonly Concentus.Oggfile.OpusOggReadStream _reader;
        private short[] _packet = [];
        private int _at;

        public OpusSource(Stream stream)
        {
            _stream = stream;
            Concentus.OpusCodecFactory.AttemptToUseNativeLibrary = false;
            _reader = new Concentus.Oggfile.OpusOggReadStream(Concentus.OpusCodecFactory.CreateDecoder(SampleRate, Channels), stream);
        }

        public int SampleRate => 48000;

        public int Channels => 2;

        public int Read(float[] buffer)
        {
            var written = 0;
            while (written < buffer.Length)
            {
                if (_at >= _packet.Length)
                {
                    if (!_reader.HasNextPacket)
                    {
                        break;
                    }

                    _packet = _reader.DecodeNextPacket() ?? [];
                    _at = 0;
                    if (_packet.Length == 0)
                    {
                        if (_reader.LastError is { Length: > 0 } error)
                        {
                            throw new InvalidDataException(error);
                        }

                        continue;
                    }
                }

                while (written < buffer.Length && _at < _packet.Length)
                {
                    buffer[written++] = _packet[_at++] / 32768f;
                }
            }

            return written;
        }

        public void Dispose() => _stream.Dispose();
    }

    private sealed class Mp3Source(Stream stream) : IPcmSource
    {
        private readonly MpegFile _file = new(stream);

        public int SampleRate => _file.SampleRate;

        public int Channels => _file.Channels;

        public int Read(float[] buffer) => _file.ReadSamples(buffer, 0, buffer.Length);

        public void Dispose()
        {
            _file.Dispose();
            stream.Dispose();
        }
    }
}

/// <summary>A WAV file of 8- or 16-bit PCM.</summary>
internal sealed class WavSource : IPcmSource
{
    private readonly byte[] _data;
    private readonly int _bits;
    private int _at;

    private WavSource(byte[] data, int sampleRate, int channels, int bits)
    {
        _data = data;
        SampleRate = sampleRate;
        Channels = channels;
        _bits = bits;
    }

    public int SampleRate { get; }

    public int Channels { get; }

    /// <summary>Reads the RIFF file's <c>fmt</c> and <c>data</c> chunks.</summary>
    /// <exception cref="InvalidDataException">It is not a WAV file of plain 8- or 16-bit PCM.</exception>
    public static WavSource Read(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        stream.Dispose();
        var bytes = copy.ToArray();
        if (bytes.Length < 12 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("Not a WAV file.");
        }

        int sampleRate = 0, channels = 0, bits = 0;
        var at = 12;
        while (at + 8 <= bytes.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 4));
            var body = at + 8;
            if (bytes.AsSpan(at, 4).SequenceEqual("fmt "u8) && length >= 16 && body + 16 <= bytes.Length)
            {
                if (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body)) != 1)
                {
                    throw new InvalidDataException("Only plain PCM WAV files are played.");
                }

                channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body + 2));
                sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(body + 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body + 14));
            }
            else if (bytes.AsSpan(at, 4).SequenceEqual("data"u8) && channels is 1 or 2 && bits is 8 or 16 && sampleRate > 0)
            {
                return new WavSource(bytes.AsSpan(body, Math.Min(length, bytes.Length - body)).ToArray(), sampleRate, channels, bits);
            }

            at = body + length + (length % 2);
        }

        throw new InvalidDataException("WAV file without plain 8- or 16-bit PCM data.");
    }

    public int Read(float[] buffer)
    {
        var count = 0;
        var step = _bits / 8;
        while (count < buffer.Length && _at + step <= _data.Length)
        {
            buffer[count++] = _bits == 16 ? BinaryPrimitives.ReadInt16LittleEndian(_data.AsSpan(_at)) / 32768f : (_data[_at] - 128) / 128f;
            _at += step;
        }

        return count;
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Plays one recorded track at a time, decoding as the sound stream asks for more; reports when a track has ended, as
/// SDL_mixer's music finished callback does.
/// </summary>
internal sealed class WaveformPlayer : IDisposable
{
    private const int BufferFrames = 4096;

    private IPcmSource? _source;
    private DynamicSoundEffectInstance? _stream;
    private float[] _decoded = [];
    private byte[] _bytes = [];
    private bool _finished;
    private int _musicOver;
    private float _volume = 1f;

    /// <summary>Whether a track is loaded (playing or paused).</summary>
    public bool IsPlaying => _source is not null;

    /// <summary>Starts a track; false if it cannot be decoded or played.</summary>
    public bool Play(string path)
    {
        Stop();
        try
        {
            _source = PcmSources.Open(path, File.OpenRead(path));
            if (_source is null || _source.Channels is not (1 or 2))
            {
                Stop();
                return false;
            }

            _stream = new DynamicSoundEffectInstance(_source.SampleRate, _source.Channels == 2 ? AudioChannels.Stereo : AudioChannels.Mono) { Volume = _volume };
            _decoded = new float[BufferFrames * _source.Channels];
            _bytes = new byte[_decoded.Length * 2];
            _finished = false;
            _stream.BufferNeeded += (_, _) => Fill();
            Fill();
            _stream.Play();
            return true;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or NoAudioHardwareException or InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"Could not load music file {path}. Error: {e.Message}");
            Stop();
            return false;
        }
    }

    public void Pause() => _stream?.Pause();

    public void Resume() => _stream?.Resume();

    public void Stop()
    {
        _stream?.Stop();
        _stream?.Dispose();
        _stream = null;
        _source?.Dispose();
        _source = null;
    }

    /// <summary><c>SDL.audio.setMusicVolume</c>.</summary>
    public void SetVolume(double volume)
    {
        _volume = (float)Math.Clamp(volume, 0, 1);
        if (_stream is not null)
        {
            _stream.Volume = _volume;
        }
    }

    /// <summary>Whether a track has come to its end since the last call.</summary>
    public bool TakeMusicOver()
    {
        if (_finished && _stream is { PendingBufferCount: 0 })
        {
            _finished = false;
            Stop();
            Interlocked.Exchange(ref _musicOver, 1);
        }

        return Interlocked.Exchange(ref _musicOver, 0) != 0;
    }

    public void Dispose() => Stop();

    /// <summary>Keeps three buffers of decoded sound queued, until the track runs out.</summary>
    private void Fill()
    {
        while (_source is not null && _stream is { PendingBufferCount: < 3 } && !_finished)
        {
            var count = _source.Read(_decoded);
            if (count <= 0)
            {
                _finished = true;
                return;
            }

            for (var i = 0; i < count; i++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(_bytes.AsSpan(i * 2), (short)Math.Clamp((int)(_decoded[i] * 32767f), short.MinValue, short.MaxValue));
            }

            // Whole frames only, as the stream wants them.
            var frameBytes = 2 * _source.Channels;
            _stream.SubmitBuffer(_bytes, 0, count * 2 / frameBytes * frameBytes);
        }
    }
}
