// The XMI to MIDI conversion, ported from CorsixTH CorsixTH/Src/xmi2mid.cpp (xmi_to_midi_token_list,
// transcode_xmi_to_midi) and CorsixTH/Src/xmi2mid.h.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Buffers.Binary;

namespace CorsixNet.Formats;

/// <summary>
/// The original's music, extended MIDI (<c>.XMI</c>) as in <c>SOUND/MIDI</c>, converted to a standard MIDI file that
/// players understand.
/// </summary>
/// <remarks>
/// As CorsixTH, only the simple XMI of Theme Hospital is handled: one song, its events in the <c>EVNT</c> chunk. XMI
/// runs at 120 ticks a second, gives a note's length with the note instead of a note-off event, and writes the time
/// between events as a sum of bytes below 0x80. The MIDI file is of type 0 with one track; times are multiplied by
/// 100 and the division chosen from the first tempo, so that the timing comes out nearly exact.
/// </remarks>
public static class Xmi
{
    /// <summary>XMI times and the tempo are multiplied by this to make rounding errors small (<c>time_multiplier</c>).</summary>
    private const int TimeMultiplier = 100;

    /// <summary>MIDI's default tempo, 120 beats a minute, in microseconds a quarter note.</summary>
    private const uint DefaultTempo = 500_000;

    private const byte NoteOn = 0x90;
    private const byte Meta = 0xFF;
    private const byte EndOfTrack = 0x2F;
    private const byte SetTempo = 0x51;

    /// <summary>
    /// <c>transcode_xmi_to_midi</c>: the standard MIDI file of an XMI file, or null when it has no events.
    /// </summary>
    /// <exception cref="InvalidDataException">There is no <c>EVNT</c> chunk, or the events end too early.</exception>
    public static byte[]? ToMidi(ReadOnlySpan<byte> xmi)
    {
        var tokens = ReadTokens(xmi, out var tempo);
        if (tokens.Count == 0)
        {
            return null;
        }

        var output = new List<byte>();
        output.AddRange([(byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0, 0, 0, 1]);

        // Ticks a quarter note: the tempo (microseconds a quarter note) times 120 × 100 ticks a second, that is tempo ×
        // 0.012, rounded; CorsixTH uses lround on a double, here it is done in whole numbers.
        var division = Math.Clamp(((long)tempo * 12 + 500) / 1000, 1, 0x7FFF);
        output.Add((byte)(division >> 8));
        output.Add((byte)division);

        output.AddRange([(byte)'M', (byte)'T', (byte)'r', (byte)'k', 0, 0, 0, 0]); // the length comes last
        long time = 0;
        byte status = 0;
        foreach (var token in tokens)
        {
            WriteVariableLength(output, (uint)(token.Time - time));
            time = token.Time;
            if (token.Type >= 0xF0)
            {
                status = token.Type;
                output.Add(status);
                if (status == Meta)
                {
                    output.Add(token.Data);
                }

                WriteVariableLength(output, (uint)token.Buffer.Length);
                output.AddRange(token.Buffer);
                if (status == Meta && token.Data == EndOfTrack)
                {
                    break;
                }
            }
            else
            {
                // Running status: the event type is left out when it repeats.
                if (token.Type != status)
                {
                    status = token.Type;
                    output.Add(status);
                }

                output.Add(token.Data);
                if (token.Buffer.Length > 0)
                {
                    output.Add(token.Buffer[0]);
                }
            }
        }

        var midi = output.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(midi.AsSpan(18), (uint)(midi.Length - 22));
        return midi;
    }

    /// <summary>
    /// <c>xmi_to_midi_token_list</c>: the events in time order, each note followed at its end by a note-on of velocity 0,
    /// and the first tempo (500,000 when there is none; later tempos are dropped).
    /// </summary>
    private static List<Token> ReadTokens(ReadOnlySpan<byte> xmi, out uint tempo)
    {
        var start = xmi.IndexOf("EVNT"u8);
        if (start < 0 || start + 8 > xmi.Length)
        {
            throw new InvalidDataException("XMI EVNT chunk not found.");
        }

        // The events follow the chunk name and its length.
        var reader = new Reader(xmi, start + 8);
        var tokens = new List<Token>();
        long time = 0;
        tempo = DefaultTempo;
        var tempoSet = false;
        var end = false;
        while (!reader.AtEnd && !end)
        {
            byte type;
            while (true)
            {
                // A byte without the high bit is part of the time since the previous event.
                type = reader.ReadByte();
                if ((type & 0x80) != 0)
                {
                    break;
                }

                time += type * TimeMultiplier;
            }

            switch (type & 0xF0)
            {
                case 0xC0 or 0xD0:
                    tokens.Add(new Token(time, type, reader.ReadByte(), []));
                    break;
                case 0x80 or 0xA0 or 0xB0 or 0xE0:
                    tokens.Add(new Token(time, type, reader.ReadByte(), [reader.ReadByte()]));
                    break;
                case NoteOn:
                    var note = reader.ReadByte();
                    tokens.Add(new Token(time, type, note, [reader.ReadByte()]));

                    // MIDI has no note lengths: the note ends with a note-on of velocity 0.
                    tokens.Add(new Token(time + (reader.ReadVariableLength() * (long)TimeMultiplier), type, note, [0]));
                    break;
                default:
                    byte extended = 0;
                    if (type == Meta)
                    {
                        extended = reader.ReadByte();
                        if (extended == EndOfTrack)
                        {
                            end = true;
                        }
                        else if (extended == SetTempo)
                        {
                            if (tempoSet)
                            {
                                reader.Skip(reader.ReadVariableLength());
                                break;
                            }

                            // CorsixTH skips the length byte, takes three bytes, and steps back to read the event.
                            tempo = reader.PeekUInt24(1);
                            tempoSet = true;
                        }
                    }

                    var length = reader.ReadVariableLength();
                    tokens.Add(new Token(time, type, extended, reader.ReadBytes(length)));
                    break;
            }
        }

        // A stable sort by time, as std::stable_sort.
        return [.. tokens.OrderBy(token => token.Time)];
    }

    private static void WriteVariableLength(List<byte> output, uint value)
    {
        var count = 1;
        var buffer = value & 0x7F;
        while ((value >>= 7) != 0)
        {
            buffer = (buffer << 8) | 0x80 | (value & 0x7F);
            count++;
        }

        for (var i = 0; i < count; i++)
        {
            output.Add((byte)buffer);
            buffer >>= 8;
        }
    }

    private sealed record Token(long Time, byte Type, byte Data, byte[] Buffer);

    private ref struct Reader(ReadOnlySpan<byte> data, int position)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position = position;

        public readonly bool AtEnd => _position >= _data.Length;

        public byte ReadByte() => _position < _data.Length ? _data[_position++] : throw EarlyEnd();

        public byte[] ReadBytes(uint count)
        {
            if (count > _data.Length - _position)
            {
                throw EarlyEnd();
            }

            var bytes = _data.Slice(_position, (int)count).ToArray();
            _position += (int)count;
            return bytes;
        }

        public void Skip(uint count)
        {
            if (count > _data.Length - _position)
            {
                throw EarlyEnd();
            }

            _position += (int)count;
        }

        /// <summary>Three big-endian bytes <paramref name="offset"/> bytes ahead, or 0 past the end, without moving.</summary>
        public readonly uint PeekUInt24(int offset)
        {
            var at = _position + offset;
            return at + 3 <= _data.Length ? (uint)((_data[at] << 16) | (_data[at + 1] << 8) | _data[at + 2]) : 0;
        }

        /// <summary>Up to four bytes of seven bits each, the high bit meaning more follow; 0 if the data ends.</summary>
        public uint ReadVariableLength()
        {
            uint value = 0;
            for (var i = 0; i < 4; i++)
            {
                if (AtEnd)
                {
                    // CorsixTH's read_variable_length_uint returns false, that is 0.
                    return 0;
                }

                var b = _data[_position++];
                value = (value << 7) | (uint)(b & 0x7F);
                if ((b & 0x80) == 0)
                {
                    break;
                }
            }

            return value;
        }

        private static InvalidDataException EarlyEnd() => new("Unexpected end of XMI data.");
    }
}
