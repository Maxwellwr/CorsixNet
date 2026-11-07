using System.Buffers.Binary;
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

    public TilePosition ReadTile() => new(ReadInt32(), ReadInt32());

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
        _stream.ReadExactly(_buffer, 0, count);
        return _buffer.AsSpan(0, count);
    }
}
