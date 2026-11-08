namespace CorsixNet.Formats;

/// <summary>
/// Colour remap tables from a ghost file such as <c>QData/Ghost1.dat</c>: tables of 256 bytes, each
/// mapping every palette index to another. CorsixTH's <c>Graphics:loadGhost</c> reads one table by
/// index; table 6 of Ghost1.dat turns the blue room blueprint red.
/// </summary>
public sealed class PaletteRemaps
{
    /// <summary>Size of one table in bytes.</summary>
    public const int TableSize = Palette.Size;

    private readonly byte[] _data;

    private PaletteRemaps(byte[] data)
    {
        _data = data;
    }

    /// <summary>Number of tables.</summary>
    public int Count => _data.Length / TableSize;

    /// <summary>Table <paramref name="index"/>: the new palette index for each old one.</summary>
    public ReadOnlySpan<byte> this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return _data.AsSpan(index * TableSize, TableSize);
        }
    }

    /// <summary>Reads the tables of a decompressed ghost file. A trailing partial table is ignored.</summary>
    public static PaletteRemaps Parse(ReadOnlySpan<byte> data)
    {
        return new PaletteRemaps(data[..(data.Length / TableSize * TableSize)].ToArray());
    }
}
