// The size and colour a TrueType font takes from the bitmap font it replaces, ported from CorsixTH
// CorsixTH/Src/th_gfx_font.cpp (freetype_font::match_bitmap_font) and CorsixTH/Src/th_gfx_sdl.cpp
// (sprite_sheet::get_sprite_average_colour).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Formats;

namespace CorsixNet.Client.Rendering;

/// <summary>A bitmap font's standard character size and its most telling colour.</summary>
internal readonly record struct BitmapFontMatch(int Width, int Height, PaletteColor Color)
{
    /// <summary>
    /// <c>match_bitmap_font</c>: the size and colour of 'M', or of '0' for fonts of digits only; failing both, the
    /// average size of all characters with the colour of the last one that has any.
    /// </summary>
    /// <remarks>
    /// CorsixTH's fallback sums the output width and height (still those of the failed '0') instead of each
    /// character's; this sums each character's, as the comment there says it means to.
    /// </remarks>
    public static BitmapFontMatch? Find(SpriteSheet sheet, Palette palette)
    {
        foreach (var character in "M0")
        {
            var sprite = character - BitmapFont.FirstCharacter;
            if (sprite < sheet.Count && sheet[sprite] is { Width: > 1, Height: > 1 } image && AverageColour(image, palette) is { } color)
            {
                return new BitmapFontMatch(image.Width, image.Height, color);
            }
        }

        int widths = 0, heights = 0, count = 0;
        PaletteColor? last = null;
        for (var i = 0; i < sheet.Count; i++)
        {
            var image = sheet[i];
            if (image.Width <= 1 || image.Height <= 1 || AverageColour(image, palette) is not { } color)
            {
                continue;
            }

            widths += image.Width;
            heights += image.Height;
            count++;
            last = color;
        }

        return count == 0 ? null : new BitmapFontMatch((widths + (count / 2)) / count, (heights + (count / 2)) / count, last!.Value);
    }

    /// <summary>
    /// <c>get_sprite_average_colour</c>: the palette colour used most, each opaque pixel scoring more the nearer its
    /// lightness is to black or white, so that the anti-aliasing greys of a font do not win.
    /// </summary>
    public static PaletteColor? AverageColour(IndexedImage image, Palette palette)
    {
        Span<int> usage = stackalloc int[Palette.Size];
        var total = 0;
        foreach (var index in image.Pixels)
        {
            var color = palette[index];
            if (color.IsTransparent)
            {
                continue;
            }

            const double gamma = 2.2;
            var y = (0.2126 * Math.Pow(color.R / 255.0, gamma)) + (0.7152 * Math.Pow(color.G / 255.0, gamma)) + (0.0722 * Math.Pow(color.B / 255.0, gamma));
            var lightness = Math.Clamp((116 * Math.Cbrt(y)) - 16, 0, 100);
            var intensity = (int)(byte)(lightness / 100 * 255.0);
            var score = 1 + Math.Max(0, 3 - ((255 - intensity) / 32)) + Math.Max(0, 3 - (intensity / 32));
            usage[index] += score;
            total += score;
        }

        if (total == 0)
        {
            return null;
        }

        var highest = 0;
        for (var i = 0; i < Palette.Size; i++)
        {
            if (usage[i] > usage[highest])
            {
                highest = i;
            }
        }

        return palette[highest];
    }
}
