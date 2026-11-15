// A reader of standard MIDI files (.MID, types 0 and 1), for the player's own music, which CorsixTH plays with
// SDL_mixer (CorsixTH/Lua/audio.lua's allowed_instructional_formats). Written from the Standard MIDI File 1.0
// specification.

using System.Buffers.Binary;

namespace CorsixNet.Formats;

/// <summary>A MIDI message to send at a time: microseconds from the start, and its bytes (status first).</summary>
public sealed record TimedMidiMessage(long Microseconds, byte[] Message);

/// <summary>Standard MIDI files: every track's channel and system exclusive messages in time order, tempo changes applied.</summary>
public static class StandardMidiFile
{
    private const uint DefaultTempo = 500_000;

    /// <summary>The file's messages in the order they play, with their times; meta events (but the tempo) are left out.</summary>
    /// <exception cref="InvalidDataException">The data is not a standard MIDI file, or is cut short.</exception>
    public static IReadOnlyList<TimedMidiMessage> Messages(ReadOnlySpan<byte> data)
    {
        if (data.Length < 14 || !data[..4].SequenceEqual("MThd"u8) || BinaryPrimitives.ReadUInt32BigEndian(data[4..]) < 6)
        {
            throw new InvalidDataException("Not a standard MIDI file.");
        }

        var headerLength = (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        var trackCount = BinaryPrimitives.ReadUInt16BigEndian(data[10..]);
        var division = BinaryPrimitives.ReadUInt16BigEndian(data[12..]);
        if ((division & 0x8000) != 0 || division == 0)
        {
            throw new InvalidDataException("SMPTE timed MIDI files are not supported.");
        }

        // Every track's events in ticks, then put in one order (ties keep the tracks' order) to apply the tempo.
        var events = new List<(long Tick, int Track, int Order, byte[] Message, uint? Tempo)>();
        var at = 8 + headerLength;
        for (var track = 0; track < trackCount && at + 8 <= data.Length; track++)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(at + 4)..]);
            if (!data.Slice(at, 4).SequenceEqual("MTrk"u8) || at + 8 + length > data.Length)
            {
                throw new InvalidDataException("MIDI track cut short.");
            }

            ReadTrack(data.Slice(at + 8, length), track, events);
            at += 8 + length;
        }

        var ordered = events.OrderBy(e => e.Tick).ThenBy(e => e.Track).ThenBy(e => e.Order).ToList();
        var result = new List<TimedMidiMessage>(ordered.Count);
        long lastTick = 0;
        long microseconds = 0;
        var tempo = DefaultTempo;
        foreach (var (tick, _, _, message, newTempo) in ordered)
        {
            microseconds += (tick - lastTick) * tempo / division;
            lastTick = tick;
            if (newTempo is { } changed)
            {
                tempo = changed;
                continue;
            }

            result.Add(new TimedMidiMessage(microseconds, message));
        }

        return result;
    }

    private static void ReadTrack(ReadOnlySpan<byte> track, int number, List<(long, int, int, byte[], uint?)> events)
    {
        var at = 0;
        long tick = 0;
        byte status = 0;
        var order = 0;
        while (at < track.Length)
        {
            tick += ReadVariable(track, ref at);
            if (at >= track.Length)
            {
                break;
            }

            var first = track[at];
            if (first >= 0x80)
            {
                status = first;
                at++;
            }
            else if (status < 0x80)
            {
                throw new InvalidDataException("MIDI data without a status byte.");
            }

            if (status == 0xFF)
            {
                if (at >= track.Length)
                {
                    break;
                }

                var type = track[at++];
                var length = (int)ReadVariable(track, ref at);
                if (at + length > track.Length)
                {
                    throw new InvalidDataException("MIDI meta event cut short.");
                }

                if (type == 0x51 && length == 3)
                {
                    events.Add((tick, number, order++, [], (uint)((track[at] << 16) | (track[at + 1] << 8) | track[at + 2])));
                }

                at += length;
                status = 0;
                if (type == 0x2F)
                {
                    break;
                }

                continue;
            }

            if (status is 0xF0 or 0xF7)
            {
                var length = (int)ReadVariable(track, ref at);
                if (at + length > track.Length)
                {
                    throw new InvalidDataException("MIDI system exclusive event cut short.");
                }

                byte[] message = status == 0xF0 ? [0xF0, .. track.Slice(at, length)] : [.. track.Slice(at, length)];
                events.Add((tick, number, order++, message, null));
                at += length;
                status = 0;
                continue;
            }

            var dataBytes = (status & 0xF0) is 0xC0 or 0xD0 ? 1 : 2;
            if (at + dataBytes > track.Length)
            {
                throw new InvalidDataException("MIDI event cut short.");
            }

            byte[] channelMessage = dataBytes == 1 ? [status, track[at]] : [status, track[at], track[at + 1]];
            events.Add((tick, number, order++, channelMessage, null));
            at += dataBytes;
        }
    }

    private static long ReadVariable(ReadOnlySpan<byte> data, ref int at)
    {
        long value = 0;
        for (var i = 0; i < 4 && at < data.Length; i++)
        {
            var b = data[at++];
            value = (value << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0)
            {
                break;
            }
        }

        return value;
    }
}
