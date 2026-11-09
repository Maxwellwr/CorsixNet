using System.Buffers.Binary;
using System.Text;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Sim.Serialization;

/// <summary>
/// Writes simulation state in a fixed little-endian binary layout.
/// </summary>
/// <remarks>
/// The same bytes serve saves, network joins and state hashing, so every field
/// must be written in a deterministic order.
/// </remarks>
public sealed class SimWriter
{
    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[8];

    public SimWriter(Stream stream)
    {
        _stream = stream;
    }

    public void WriteByte(byte value) => _stream.WriteByte(value);

    public void WriteBool(bool value) => _stream.WriteByte(value ? (byte)1 : (byte)0);

    public void WriteUInt16(ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer, value);
        _stream.Write(_buffer, 0, 2);
    }

    public void WriteInt32(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_buffer, value);
        _stream.Write(_buffer, 0, 4);
    }

    public void WriteUInt32(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer, value);
        _stream.Write(_buffer, 0, 4);
    }

    public void WriteInt64(long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(_buffer, value);
        _stream.Write(_buffer, 0, 8);
    }

    public void WriteUInt64(ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(_buffer, value);
        _stream.Write(_buffer, 0, 8);
    }

    /// <summary>Writes a decimal as its four 32-bit parts (<see cref="decimal.GetBits(decimal)"/>).</summary>
    public void WriteDecimal(decimal value)
    {
        foreach (var bits in decimal.GetBits(value))
        {
            WriteInt32(bits);
        }
    }

    public void WriteTile(TilePosition tile)
    {
        WriteInt32(tile.X);
        WriteInt32(tile.Y);
    }

    public void WriteBytes(ReadOnlySpan<byte> bytes) => _stream.Write(bytes);

    /// <summary>Writes a UTF-8 string preceded by its byte length.</summary>
    public void WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteInt32(bytes.Length);
        WriteBytes(bytes);
    }
}
