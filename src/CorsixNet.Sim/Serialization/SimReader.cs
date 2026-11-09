using System.Buffers.Binary;
using System.Text;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Sim.Serialization;

/// <summary>Reads data written by <see cref="SimWriter"/>.</summary>
/// <exception cref="InvalidDataException">Thrown by every method when the data ends early.</exception>
public sealed class SimReader
{
    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[8];

    public SimReader(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>The format version of the state being read, for readers of parts that changed since an older version.</summary>
    public int Version { get; set; }

    public byte ReadByte()
    {
        var value = _stream.ReadByte();
        return value < 0 ? throw new InvalidDataException("Unexpected end of simulation data.") : (byte)value;
    }

    public bool ReadBool()
    {
        return ReadByte() switch
        {
            0 => false,
            1 => true,
            var other => throw new InvalidDataException($"Invalid boolean value {other} in simulation data."),
        };
    }

    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Fill(2));

    public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Fill(4));

    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Fill(4));

    public long ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian(Fill(8));

    public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Fill(8));

    /// <summary>Reads a decimal written by <see cref="SimWriter.WriteDecimal"/>.</summary>
    public decimal ReadDecimal()
    {
        int[] bits = [ReadInt32(), ReadInt32(), ReadInt32(), ReadInt32()];
        try
        {
            return new decimal(bits);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("A decimal is malformed.", exception);
        }
    }

    public TilePosition ReadTile() => new(ReadInt32(), ReadInt32());

    /// <summary>Reads a string written by <see cref="SimWriter.WriteString"/>, rejecting ones longer than <paramref name="maxBytes"/>.</summary>
    public string ReadString(int maxBytes = 256)
    {
        var bytes = ReadBytes(ReadCount(maxBytes));
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException e)
        {
            throw new InvalidDataException("Invalid UTF-8 string in simulation data.", e);
        }
    }

    public byte[] ReadBytes(int count)
    {
        var bytes = new byte[count];
        try
        {
            _stream.ReadExactly(bytes);
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException("Unexpected end of simulation data.", e);
        }

        return bytes;
    }

    /// <summary>Reads a non-negative count, rejecting values above <paramref name="max"/>.</summary>
    public int ReadCount(int max)
    {
        var count = ReadInt32();
        if (count < 0 || count > max)
        {
            throw new InvalidDataException($"Count {count} in simulation data is outside 0..{max}.");
        }

        return count;
    }

    private ReadOnlySpan<byte> Fill(int count)
    {
        try
        {
            _stream.ReadExactly(_buffer, 0, count);
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException("Unexpected end of simulation data.", e);
        }

        return _buffer.AsSpan(0, count);
    }
}
