// The sound archive reader, ported from CorsixTH CorsixTH/Src/th_sound.cpp (sound_archive::load_from_th_file,
// get_sound_duration, load_sound), the name lookup of CorsixTH/Src/th_lua_sound.cpp (l_soundarc_checkidx) and the
// wildcard names of CorsixTH/Lua/audio.lua (Audio:cacheSoundFilenamesAssociatedWithName).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace CorsixNet.Formats;

/// <summary>A sound in a <see cref="SoundArchive"/>: its name, such as <c>SELECTX.WAV</c>, and where its WAV file lies.</summary>
public sealed record SoundEntry(string Name, int Position, int Length);

/// <summary>
/// The original's sound archive, <c>SOUND/DATA/SOUND-n.DAT</c>: WAV files with a table of their names, the sound
/// effects and the announcer's speech of a language.
/// </summary>
/// <remarks>
/// The last 4 bytes of the file give where a header of 234 bytes lies; the header gives where the table lies (offset 50)
/// and its length (offset 58); each table entry is 32 bytes: an 18-byte name, then the WAV file's position (offset 18)
/// and length (offset 26). The first entry has no name and spans the whole file; CorsixTH numbers the sounds from 1
/// because of it, and here it is left out.
/// </remarks>
public sealed class SoundArchive
{
    private const int HeaderSize = 234;
    private const int TablePositionOffset = 50;
    private const int TableLengthOffset = 58;
    private const int EntrySize = 32;
    private const int NameLength = 18;
    private const int EntryPositionOffset = 18;
    private const int EntryLengthOffset = 26;

    private readonly byte[] _data;
    private readonly Dictionary<string, SoundEntry> _byName;

    private SoundArchive(byte[] data, IReadOnlyList<SoundEntry> sounds)
    {
        _data = data;
        Sounds = sounds;
        _byName = new Dictionary<string, SoundEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var sound in sounds)
        {
            // l_soundarc_checkidx takes the first sound of the name.
            _byName.TryAdd(sound.Name, sound);
        }
    }

    /// <summary>The sounds in the archive's order (CorsixTH's sounds 1 onwards).</summary>
    public IReadOnlyList<SoundEntry> Sounds { get; }

    /// <summary>Reads an archive (<c>sound_archive::load_from_th_file</c>); the data is kept, not copied.</summary>
    /// <exception cref="InvalidDataException">The data is too short, or its header or table lies outside it.</exception>
    public static SoundArchive Parse(byte[] data)
    {
        if (data.Length < sizeof(uint) + HeaderSize)
        {
            throw new InvalidDataException("A sound archive must be at least 238 bytes long.");
        }

        var header = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(data.Length - sizeof(uint)));
        if (header >= (uint)(data.Length - HeaderSize))
        {
            throw new InvalidDataException($"The sound archive's header position {header} is outside the file.");
        }

        var tablePosition = (long)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)header + TablePositionOffset));
        var tableLength = (long)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)header + TableLengthOffset));
        var count = tableLength / EntrySize;
        var sounds = new List<SoundEntry>();
        for (long i = 0; i < count; i++)
        {
            var offset = tablePosition + (i * EntrySize);
            if (offset + EntrySize > data.Length)
            {
                throw new InvalidDataException($"The sound archive's table entry {i} is outside the file.");
            }

            if (i == 0)
            {
                continue;
            }

            var entry = data.AsSpan((int)offset, EntrySize);
            var name = entry[..NameLength];
            var end = name.IndexOf((byte)0);
            sounds.Add(new SoundEntry(
                Encoding.Latin1.GetString(end < 0 ? name : name[..end]),
                (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(entry[EntryPositionOffset..]), int.MaxValue),
                (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(entry[EntryLengthOffset..]), int.MaxValue)));
        }

        return new SoundArchive(data, sounds);
    }

    /// <summary>The first sound of the name, in any case of letters, or null.</summary>
    public SoundEntry? Find(string name) => _byName.GetValueOrDefault(name);

    /// <summary>
    /// The names of the sounds a name with <c>*</c> wildcards matches, in the archive's order, upper case
    /// (<c>Audio:cacheSoundFilenamesAssociatedWithName</c>); CorsixTH plays a random one of them.
    /// </summary>
    public IReadOnlyList<string> Matching(string pattern)
    {
        var regex = new Regex("^" + string.Join(".*", pattern.Split('*').Select(Regex.Escape)) + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return [.. Sounds.Select(sound => sound.Name.ToUpperInvariant()).Where(name => regex.IsMatch(name))];
    }

    /// <summary>The sound's WAV file (<c>load_sound</c>).</summary>
    /// <exception cref="InvalidDataException">The sound lies outside the archive.</exception>
    public ReadOnlyMemory<byte> Read(SoundEntry sound)
    {
        // CorsixTH does not check this.
        if ((long)sound.Position + sound.Length > _data.Length)
        {
            throw new InvalidDataException($"The sound {sound.Name} lies outside the archive.");
        }

        return _data.AsMemory(sound.Position, sound.Length);
    }

    /// <summary>
    /// How long the sound plays, in milliseconds (<c>get_sound_duration</c>): its PCM data's length from the WAV file's
    /// <c>fmt </c> and <c>data</c> chunks; 0 when the file is not plain PCM or cannot be read.
    /// </summary>
    public int Duration(SoundEntry sound)
    {
        ReadOnlySpan<byte> wave;
        try
        {
            wave = Read(sound).Span;
        }
        catch (InvalidDataException)
        {
            return 0;
        }

        var format = WaveFormat.Find(wave);
        if (format is not { AudioFormat: 1, Channels: > 0, SampleRate: > 0, BitsPerSample: > 0, DataLength: > 0 })
        {
            return 0;
        }

        return (int)Math.Min((ulong)format.DataLength * 8000 / ((ulong)format.BitsPerSample * (ulong)format.Channels * format.SampleRate), int.MaxValue);
    }
}

/// <summary>What a WAV file holds: the <c>fmt </c> chunk's fields and the <c>data</c> chunk's position and length.</summary>
public sealed record WaveFormat(int AudioFormat, int Channels, uint SampleRate, int BitsPerSample, int DataPosition, uint DataLength)
{
    /// <summary>
    /// CorsixTH's "very crude RIFF parser" (<c>get_sound_duration</c>): RIFF and LIST chunks are stepped into, the
    /// <c>fmt </c> chunk read, and the walk ends at the <c>data</c> chunk; other chunks are skipped, padded to even
    /// length. Null when there is no <c>data</c> chunk.
    /// </summary>
    public static WaveFormat? Find(ReadOnlySpan<byte> wave)
    {
        int audioFormat = 0, channels = 0, bitsPerSample = 0;
        uint sampleRate = 0;
        long position = 0;
        while (position + 8 <= wave.Length)
        {
            var fourCc = wave.Slice((int)position, 4);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(wave[((int)position + 4)..]);
            position += 8;
            if ((fourCc.SequenceEqual("RIFF"u8) || fourCc.SequenceEqual("LIST"u8)) && length >= 4)
            {
                position += 4;
                continue;
            }

            var body = length;
            if (fourCc.SequenceEqual("fmt "u8) && length >= 16)
            {
                if (position + 16 > wave.Length)
                {
                    return null;
                }

                var fmt = wave.Slice((int)position, 16);
                audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
                sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]);
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);
                position += 16;
                body -= 16;
            }

            if (fourCc.SequenceEqual("data"u8))
            {
                return new WaveFormat(audioFormat, channels, sampleRate, bitsPerSample, (int)position, length);
            }

            position += body + (length & 1);
        }

        return null;
    }
}
