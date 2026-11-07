using System.Buffers.Binary;

namespace CorsixNet.Formats.Tests;

public class RncTests
{
    private const int HeaderUnpackedCrcOffset = 12;
    private const int HeaderPackedCrcOffset = 14;

    [Fact]
    public void CrcMatchesCrc16Arc()
    {
        // Standard check value for CRC-16/ARC.
        Assert.Equal(0xBB3D, Rnc.Crc("123456789"u8));
    }

    [Fact]
    public void DetectsSignature()
    {
        Assert.True(Rnc.IsCompressed(Header(packed: [])));
        Assert.False(Rnc.IsCompressed("RNC\x02 not method one"u8));
        Assert.False(Rnc.IsCompressed("RNC\x01"u8));
    }

    [Fact]
    public void PassesUncompressedDataThrough()
    {
        byte[] data = [1, 2, 3];
        Assert.Same(data, Rnc.DecompressIfNeeded(data));
    }

    [Fact]
    public void RejectsDataWithoutHeader()
    {
        var error = Assert.Throws<InvalidDataException>(() => Rnc.Decompress([1, 2, 3]));
        Assert.Contains("header", error.Message);
    }

    [Fact]
    public void RejectsTruncatedData()
    {
        var data = Header(packed: new byte[10]);
        var error = Assert.Throws<InvalidDataException>(() => Rnc.Decompress(data.AsSpan(0, data.Length - 1)));
        Assert.Contains("truncated", error.Message);
    }

    [Fact]
    public void RejectsPackedChecksumMismatch()
    {
        var data = Header(packed: new byte[10], unpackedSize: 4);
        data[HeaderPackedCrcOffset] ^= 0xFF;

        var error = Assert.Throws<InvalidDataException>(() => Rnc.Decompress(data));
        Assert.Contains("packed data checksum", error.Message);
    }

    [Fact]
    public void RejectsInvalidHuffmanCode()
    {
        // All-zero packed data defines empty tables, so the first literal length cannot be decoded.
        var data = Header(packed: new byte[10], unpackedSize: 4);

        var error = Assert.Throws<InvalidDataException>(() => Rnc.Decompress(data));
        Assert.Contains("Huffman", error.Message);
    }

    [Fact]
    public void EmptyPayloadDecompressesToNothing()
    {
        Assert.Empty(Rnc.Decompress(Header(packed: [], unpackedSize: 0)));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("repeats")]
    [InlineData("mixed")]
    public void DecompressesFixture(string name)
    {
        var (packed, original) = Fixture(name);

        Assert.True(Rnc.IsCompressed(packed));
        Assert.Equal(original, Rnc.Decompress(packed));
    }

    [Fact]
    public void RejectsUnpackedChecksumMismatch()
    {
        var (packed, _) = Fixture("repeats");
        packed[HeaderUnpackedCrcOffset] ^= 0xFF;

        var error = Assert.Throws<InvalidDataException>(() => Rnc.Decompress(packed));
        Assert.Contains("unpacked data checksum", error.Message);
    }

    [Fact]
    public void DecompressesEveryCompressedGameFile()
    {
        var install = RealGameData.Require();
        var files = Directory.EnumerateFiles(install.RootPath, "*", SearchOption.AllDirectories)
            .Select(File.ReadAllBytes)
            .Where(data => Rnc.IsCompressed(data))
            .ToList();

        Assert.NotEmpty(files);
        foreach (var data in files)
        {
            // Decompress verifies both checksums.
            var unpacked = Rnc.Decompress(data);
            Assert.Equal(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)), (uint)unpacked.Length);
        }
    }

    /// <summary>Reads a packed fixture and its original bytes; see Fixtures/Rnc/generate.py.</summary>
    private static (byte[] Packed, byte[] Original) Fixture(string name)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Rnc");
        return (File.ReadAllBytes(Path.Combine(folder, name + ".rnc")), File.ReadAllBytes(Path.Combine(folder, name + ".bin")));
    }

    /// <summary>Builds RNC data with correct sizes and packed checksum around <paramref name="packed"/>.</summary>
    private static byte[] Header(byte[] packed, uint unpackedSize = 0)
    {
        var data = new byte[Rnc.HeaderSize + packed.Length];
        "RNC\x01"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), unpackedSize);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), (uint)packed.Length);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12), 0);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(HeaderPackedCrcOffset), Rnc.Crc(packed));
        packed.CopyTo(data, Rnc.HeaderSize);
        return data;
    }
}
