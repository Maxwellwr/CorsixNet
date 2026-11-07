// Ported from CorsixTH libs/rnc/rnc.cpp.
// Copyright (c) 1997 Simon Tatham; modifications by the CorsixTH contributors.
// Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Buffers.Binary;

namespace CorsixNet.Formats;

/// <summary>
/// Decompressor for Rob Northen Compression (RNC method 1), used by most Theme Hospital data files.
/// </summary>
public static class Rnc
{
    /// <summary>Size of the header that precedes the packed data.</summary>
    public const int HeaderSize = 18;

    private const uint Signature = 0x524E4301; // "RNC\x01"

    private static readonly ushort[] CrcTable = BuildCrcTable();

    /// <summary>Whether <paramref name="data"/> starts with an RNC method 1 header.</summary>
    public static bool IsCompressed(ReadOnlySpan<byte> data)
    {
        return data.Length >= HeaderSize && BinaryPrimitives.ReadUInt32BigEndian(data) == Signature;
    }

    /// <summary>Returns the data itself if it is not compressed, otherwise its decompressed form.</summary>
    public static byte[] DecompressIfNeeded(byte[] data)
    {
        return IsCompressed(data) ? Decompress(data) : data;
    }

    /// <summary>Decompresses RNC data, verifying both checksums.</summary>
    /// <exception cref="InvalidDataException">The data is not RNC compressed or is corrupt.</exception>
    public static byte[] Decompress(ReadOnlySpan<byte> input)
    {
        if (!IsCompressed(input))
        {
            throw new InvalidDataException("Data does not start with an RNC header.");
        }

        var unpackedSize = BinaryPrimitives.ReadUInt32BigEndian(input[4..]);
        var packedSize = BinaryPrimitives.ReadUInt32BigEndian(input[8..]);
        var unpackedCrc = BinaryPrimitives.ReadUInt16BigEndian(input[12..]);
        var packedCrc = BinaryPrimitives.ReadUInt16BigEndian(input[14..]);

        if (packedSize > (uint)(input.Length - HeaderSize))
        {
            throw new InvalidDataException($"RNC data is truncated: header declares {packedSize} packed bytes, {input.Length - HeaderSize} present.");
        }

        if (unpackedSize > Array.MaxLength)
        {
            throw new InvalidDataException($"RNC unpacked size {unpackedSize} is too large.");
        }

        var packed = input.Slice(HeaderSize, (int)packedSize);
        if (Crc(packed) != packedCrc)
        {
            throw new InvalidDataException("RNC packed data checksum mismatch.");
        }

        var output = new byte[unpackedSize];
        var bits = new BitReader(packed);
        var raw = new HuffmanTable();
        var distance = new HuffmanTable();
        var length = new HuffmanTable();
        var outPos = 0;

        // The first two bits have no known meaning.
        bits.Advance(2);

        while (outPos < output.Length)
        {
            raw.Read(ref bits);
            distance.Read(ref bits);
            length.Read(ref bits);
            var remainingPairs = (int)bits.Read(0xFFFF, 16);

            while (true)
            {
                var literalCount = raw.Decode(ref bits);
                if (literalCount > 0)
                {
                    if (literalCount > output.Length - outPos || literalCount > packed.Length - bits.Position)
                    {
                        throw Corrupt("literal run exceeds the data bounds");
                    }

                    // Literal bytes are stored inline at the bit reader's position.
                    packed.Slice(bits.Position, literalCount).CopyTo(output.AsSpan(outPos));
                    outPos += literalCount;
                    bits.Position += literalCount;
                    bits.Resync();
                }

                if (--remainingPairs <= 0)
                {
                    break;
                }

                var offset = distance.Decode(ref bits) + 1;
                var copyLength = length.Decode(ref bits) + 2;
                if (offset > outPos || copyLength > output.Length - outPos)
                {
                    throw Corrupt("back-reference exceeds the data bounds");
                }

                // Byte by byte: source and destination may overlap.
                for (var i = 0; i < copyLength; i++, outPos++)
                {
                    output[outPos] = output[outPos - offset];
                }
            }
        }

        if (Crc(output) != unpackedCrc)
        {
            throw new InvalidDataException("RNC unpacked data checksum mismatch.");
        }

        return output;
    }

    internal static ushort Crc(ReadOnlySpan<byte> data)
    {
        ushort value = 0;
        foreach (var b in data)
        {
            value ^= b;
            value = (ushort)((value >> 8) ^ CrcTable[value & 0xFF]);
        }

        return value;
    }

    private static ushort[] BuildCrcTable()
    {
        // CRC-16 with the reflected polynomial 0xA001.
        var table = new ushort[256];
        for (var i = 0; i < table.Length; i++)
        {
            var value = (uint)i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? (value >> 1) ^ 0xA001 : value >> 1;
            }

            table[i] = (ushort)value;
        }

        return table;
    }

    private static InvalidDataException Corrupt(string detail) => new($"RNC data is corrupt: {detail}.");

    /// <summary>Reads bits least-significant first from little-endian 16-bit words.</summary>
    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private uint _buffer;
        private int _count;

        public BitReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _buffer = data.Length switch
            {
                0 => 0,
                1 => data[0],
                _ => BinaryPrimitives.ReadUInt16LittleEndian(data),
            };
            _count = 16;
            Position = 0;
        }

        /// <summary>Offset of the most recently loaded word; literal bytes are read from here.</summary>
        public int Position { get; set; }

        public readonly uint Peek(uint mask) => _buffer & mask;

        public void Advance(int count)
        {
            if (count is < 0 or > 16)
            {
                throw Corrupt("bit field wider than 16 bits");
            }

            _buffer >>= count;
            _count -= count;
            if (_count < 16)
            {
                Position += 2;
                Load(Position + 1 < _data.Length);
            }
        }

        public uint Read(uint mask, int count)
        {
            var result = Peek(mask);
            Advance(count);
            return result;
        }

        /// <summary>Re-fills the buffer after literal bytes have moved <see cref="Position"/>.</summary>
        public void Resync()
        {
            // Drop the word at the old position: it held literal bytes, not bits.
            _count = Math.Max(_count - 16, 0);
            _buffer &= (1u << _count) - 1;
            Load(Position + 1 < _data.Length);
        }

        private void Load(bool wholeWord)
        {
            if (wholeWord)
            {
                _buffer |= (uint)BinaryPrimitives.ReadUInt16LittleEndian(_data[Position..]) << _count;
                _count += 16;
            }
            else if (Position >= 0 && Position < _data.Length)
            {
                _buffer |= (uint)_data[Position] << _count;
                _count += 16;
            }
        }
    }

    private sealed class HuffmanTable
    {
        private readonly uint[] _codes = new uint[32];
        private readonly int[] _lengths = new int[32];
        private readonly int[] _values = new int[32];
        private int _count;

        /// <summary>Reads a table definition. An empty definition keeps the previous table.</summary>
        public void Read(ref BitReader bits)
        {
            var leafCount = (int)bits.Read(0x1F, 5);
            if (leafCount == 0)
            {
                return;
            }

            Span<int> leafLengths = stackalloc int[leafCount];
            var maxLength = 1;
            for (var i = 0; i < leafCount; i++)
            {
                leafLengths[i] = (int)bits.Read(0x0F, 4);
                maxLength = Math.Max(maxLength, leafLengths[i]);
            }

            uint code = 0;
            var entry = 0;
            for (var codeLength = 1; codeLength <= maxLength; codeLength++)
            {
                for (var leaf = 0; leaf < leafCount; leaf++)
                {
                    if (leafLengths[leaf] == codeLength)
                    {
                        _codes[entry] = Mirror(code, codeLength);
                        _lengths[entry] = codeLength;
                        _values[entry] = leaf;
                        code++;
                        entry++;
                    }
                }

                code <<= 1;
            }

            _count = entry;
        }

        public int Decode(ref BitReader bits)
        {
            for (var i = 0; i < _count; i++)
            {
                if (bits.Peek((1u << _lengths[i]) - 1) != _codes[i])
                {
                    continue;
                }

                bits.Advance(_lengths[i]);
                var value = _values[i];
                if (value < 2)
                {
                    return value;
                }

                var high = 1u << (value - 1);
                return (int)(high | bits.Read(high - 1, value - 1));
            }

            throw Corrupt("invalid Huffman code");
        }

        /// <summary>Reverses the order of the lowest <paramref name="bitCount"/> bits.</summary>
        private static uint Mirror(uint value, int bitCount)
        {
            var top = 1u << (bitCount - 1);
            var bottom = 1u;
            while (top > bottom)
            {
                var mask = top | bottom;
                var masked = value & mask;
                if (masked != 0 && masked != mask)
                {
                    value ^= mask;
                }

                top >>= 1;
                bottom <<= 1;
            }

            return value;
        }
    }
}
