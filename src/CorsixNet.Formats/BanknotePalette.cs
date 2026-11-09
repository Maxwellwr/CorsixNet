// Ported from CorsixTH CorsixTH/Lua/graphics.lua (Graphics:_makeBluePaletteForFloatingBanknote).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

namespace CorsixNet.Formats;

/// <summary>The blue palette CorsixTH draws an insurance company's floating banknote with.</summary>
public static class BanknotePalette
{
    /// <summary>How far green has to lead the other channels, in six-bit units, for an entry to count as green.</summary>
    private const int GreenLead = 4;

    /// <summary>
    /// Turns the clearly green entries of six-bit RGB palette data (<c>MPalette.dat</c>) blue, keeping their brightness;
    /// greys and every other colour are left as they are.
    /// </summary>
    /// <param name="data">768 bytes of six-bit RGB.</param>
    /// <returns>The changed copy, in the same format.</returns>
    /// <exception cref="InvalidDataException">The data is not 768 bytes long.</exception>
    public static byte[] MakeBlue(ReadOnlySpan<byte> data)
    {
        if (data.Length != Palette.Size * 3)
        {
            throw new InvalidDataException($"Unexpected palette length {data.Length}.");
        }

        var shifted = data.ToArray();
        for (var i = 0; i < shifted.Length; i += 3)
        {
            int r = shifted[i], g = shifted[i + 1], b = shifted[i + 2];
            if (g >= r + GreenLead && g >= b + GreenLead)
            {
                // math.floor(g * 0.35) and math.floor(g * 0.55), at most 63.
                shifted[i] = (byte)Math.Min(63, g * 35 / 100);
                shifted[i + 1] = (byte)Math.Min(63, g * 55 / 100);
                shifted[i + 2] = (byte)g;
            }
        }

        return shifted;
    }
}
