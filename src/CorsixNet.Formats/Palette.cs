namespace CorsixNet.Formats;

/// <summary>A 256-colour palette.</summary>
/// <remarks>
/// Theme Hospital palettes store 6-bit VGA components (0–63). Pure magenta
/// marks transparency, as in CorsixTH.
/// </remarks>
public sealed class Palette
{
    /// <summary>Number of entries in a palette.</summary>
    public const int Size = 256;

    /// <summary>Index that sprite decoding uses for transparent pixels.</summary>
    public const byte TransparentIndex = 0xFF;

    private readonly PaletteColor[] _colors;

    private Palette(PaletteColor[] colors)
    {
        _colors = colors;
    }

    public PaletteColor this[int index] => _colors[index];

    /// <summary>The entries in index order.</summary>
    public ReadOnlySpan<PaletteColor> Colors => _colors;

    /// <summary>
    /// Parses palette data: 768 bytes (RGB) or 1024 bytes (RGBA).
    /// </summary>
    /// <param name="data">Palette data, already decompressed.</param>
    /// <param name="eightBit">True if components are 0–255 rather than 6-bit VGA values.</param>
    /// <param name="transparentLastEntry">
    /// True to make entry 255 transparent. CorsixTH does this for every game palette,
    /// because sprite decoding fills transparent pixels with index 255.
    /// </param>
    /// <exception cref="InvalidDataException">The data has an unexpected length.</exception>
    public static Palette Parse(ReadOnlySpan<byte> data, bool eightBit = false, bool transparentLastEntry = true)
    {
        var stride = data.Length switch
        {
            Size * 3 => 3,
            Size * 4 => 4,
            _ => throw new InvalidDataException($"Palette data must be {Size * 3} or {Size * 4} bytes, not {data.Length}."),
        };

        var colors = new PaletteColor[Size];
        for (var i = 0; i < Size; i++)
        {
            var entry = data.Slice(i * stride, stride);
            var color = new PaletteColor(
                eightBit ? entry[0] : Expand6Bit(entry[0]),
                eightBit ? entry[1] : Expand6Bit(entry[1]),
                eightBit ? entry[2] : Expand6Bit(entry[2]),
                stride == 4 ? entry[3] : (byte)0xFF);
            colors[i] = IsMagenta(color) ? PaletteColor.Transparent : color;
        }

        if (transparentLastEntry)
        {
            colors[TransparentIndex] = PaletteColor.Transparent;
        }

        return new Palette(colors);
    }

    /// <summary>Scales a 6-bit VGA component to 8 bits, rounding to nearest.</summary>
    internal static byte Expand6Bit(byte component)
    {
        // 255 * c / 63 never has a fractional part of exactly one half, so this matches round().
        return (byte)((((component & 0x3F) * 255) + 31) / 63);
    }

    private static bool IsMagenta(PaletteColor color)
    {
        return color is { R: 0xFF, G: 0x00, B: 0xFF, A: 0xFF };
    }
}
