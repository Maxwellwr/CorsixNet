namespace CorsixNet.Formats;

/// <summary>An image of palette indices, stored row by row.</summary>
public sealed class IndexedImage
{
    public static readonly IndexedImage Empty = new(0, 0, []);

    public IndexedImage(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if (pixels.Length != width * height)
        {
            throw new ArgumentException($"Expected {width * height} pixels, got {pixels.Length}.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Palette indices, <see cref="Width"/> × <see cref="Height"/> bytes.</summary>
    public byte[] Pixels { get; }

    public bool IsEmpty => Width == 0 || Height == 0;

    public byte this[int x, int y] => Pixels[(y * Width) + x];

    /// <summary>
    /// Reads an uncompressed full-screen bitmap such as the QData backgrounds (Load01V and others).
    /// Extra trailing bytes are ignored.
    /// </summary>
    /// <exception cref="InvalidDataException">The data is shorter than <paramref name="width"/> × <paramref name="height"/>.</exception>
    public static IndexedImage FromRaw(ReadOnlySpan<byte> data, int width = 640, int height = 480)
    {
        var size = width * height;
        if (data.Length < size)
        {
            throw new InvalidDataException($"Bitmap data has {data.Length} bytes, {size} needed for {width}x{height}.");
        }

        return new IndexedImage(width, height, data[..size].ToArray());
    }

    /// <summary>
    /// A copy with each palette index replaced through <paramref name="table"/> (see <see cref="PaletteRemaps"/>).
    /// Transparent pixels stay transparent.
    /// </summary>
    public IndexedImage Remap(ReadOnlySpan<byte> table)
    {
        if (table.Length != Palette.Size)
        {
            throw new ArgumentException($"A remap table has {Palette.Size} entries, got {table.Length}.", nameof(table));
        }

        var pixels = new byte[Pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            var index = Pixels[i];
            pixels[i] = index == Palette.TransparentIndex ? index : table[index];
        }

        return new IndexedImage(Width, Height, pixels);
    }

    /// <summary>Converts to 32-bit colours, row by row.</summary>
    public PaletteColor[] ToColors(Palette palette)
    {
        var colors = new PaletteColor[Pixels.Length];
        for (var i = 0; i < Pixels.Length; i++)
        {
            colors[i] = palette[Pixels[i]];
        }

        return colors;
    }
}
