namespace CorsixNet.Formats;

/// <summary>A 32-bit colour with straight (non-premultiplied) alpha.</summary>
public readonly record struct PaletteColor(byte R, byte G, byte B, byte A)
{
    public static readonly PaletteColor Transparent = new(0, 0, 0, 0);

    public bool IsTransparent => A == 0;
}
