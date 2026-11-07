// MMPX pixel art magnification, ported from the reference implementation:
// Copyright 2020 Morgan McGuire & Mara Gagiu. Available under the MIT license.
// https://casual-effects.com/research/McGuire2021PixelArt/index.html
// See THIRD-PARTY-NOTICES.md.

namespace CorsixNet.Formats;

/// <summary>
/// Enlarges palette images with MMPX, a filter for pixel art that smooths diagonal edges
/// while keeping straight edges, corners and the original colours.
/// </summary>
/// <remarks>
/// MMPX only ever copies existing pixels, so it works on palette indices and keeps palette
/// effects possible. Indices with identical colours are treated as one (the lowest such index).
/// Pixels beyond the image edge repeat the nearest edge pixel.
/// </remarks>
public static class PixelArtScaler
{
    /// <summary>Doubles <paramref name="image"/> <paramref name="passes"/> times (1 → 2x, 2 → 4x).</summary>
    public static IndexedImage Scale(IndexedImage image, Palette palette, int passes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(passes);
        var canonical = CanonicalIndices(palette);
        var luma = LumaTable(palette);
        var result = image;
        for (var i = 0; i < passes; i++)
        {
            result = Scale2x(result, canonical, luma);
        }

        return result;
    }

    /// <summary>Doubles an image with MMPX.</summary>
    public static IndexedImage Scale2x(IndexedImage image, Palette palette)
    {
        return Scale2x(image, CanonicalIndices(palette), LumaTable(palette));
    }

    /// <summary>
    /// Enlarges a floor tile that tiles the plane with the isometric lattice (32, 16) and (32, −16),
    /// without opening gaps between neighbouring tiles.
    /// </summary>
    /// <remarks>
    /// Scaled on its own, a tile's edge borders transparency and MMPX rounds it off, leaving
    /// seams once tiles are laid side by side. Instead the tile is surrounded by copies of itself
    /// along the lattice, so the filter sees the texture continue, and the result is cut back to the
    /// tile's original outline enlarged pixel for pixel. That outline fits its neighbours exactly,
    /// as the original tiles do.
    /// </remarks>
    public static IndexedImage ScaleTile(IndexedImage tile, Palette palette, int passes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(passes);
        if (passes == 0 || tile.IsEmpty)
        {
            return tile;
        }

        // MMPX looks up to 3 pixels away; 8 source pixels of margin cover two passes.
        const int margin = 8;
        var padded = new byte[(tile.Width + (2 * margin)) * (tile.Height + (2 * margin))];
        var paddedWidth = tile.Width + (2 * margin);
        for (var py = 0; py < tile.Height + (2 * margin); py++)
        {
            for (var px = 0; px < paddedWidth; px++)
            {
                padded[(py * paddedWidth) + px] = LatticeSample(tile, palette, px - margin, py - margin);
            }
        }

        var scaled = Scale(new IndexedImage(paddedWidth, tile.Height + (2 * margin), padded), palette, passes);

        var factor = 1 << passes;
        var width = tile.Width * factor;
        var height = tile.Height * factor;
        var output = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var original = tile[x / factor, y / factor];
                output[(y * width) + x] = palette[original].IsTransparent
                    ? original
                    : scaled[x + (margin * factor), y + (margin * factor)];
            }
        }

        return new IndexedImage(width, height, output);
    }

    /// <summary>The tile's pixel at (x, y), or the opaque pixel of a copy of the tile shifted along the lattice.</summary>
    private static byte LatticeSample(IndexedImage tile, Palette palette, int x, int y)
    {
        for (var a = -2; a <= 2; a++)
        {
            for (var b = -2; b <= 2; b++)
            {
                var sx = x - (32 * a) - (32 * b);
                var sy = y - (16 * a) + (16 * b);
                if (sx >= 0 && sx < tile.Width && sy >= 0 && sy < tile.Height && !palette[tile[sx, sy]].IsTransparent)
                {
                    return tile[sx, sy];
                }
            }
        }

        return x >= 0 && x < tile.Width && y >= 0 && y < tile.Height ? tile[x, y] : Palette.TransparentIndex;
    }

    private static IndexedImage Scale2x(IndexedImage image, byte[] canonical, int[] lumaTable)
    {
        if (image.IsEmpty)
        {
            return image;
        }

        var width = image.Width;
        var height = image.Height;
        var source = new byte[image.Pixels.Length];
        for (var i = 0; i < source.Length; i++)
        {
            source[i] = canonical[image.Pixels[i]];
        }

        var output = new byte[width * height * 4];
        var outputWidth = width * 2;

        byte Src(int x, int y)
        {
            x = Math.Clamp(x, 0, width - 1);
            y = Math.Clamp(y, 0, height - 1);
            return source[(y * width) + x];
        }

        int Luma(byte index) => lumaTable[index];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var a = Src(x - 1, y - 1);
                var b = Src(x, y - 1);
                var c = Src(x + 1, y - 1);
                var d = Src(x - 1, y);
                var e = Src(x, y);
                var f = Src(x + 1, y);
                var g = Src(x - 1, y + 1);
                var h = Src(x, y + 1);
                var i = Src(x + 1, y + 1);
                var q = Src(x - 2, y);
                var r = Src(x + 2, y);

                // Outputs: J K on the top row, L M on the bottom row.
                byte j = e, k = e, l = e, m = e;

                if (a != e || b != e || c != e || d != e || f != e || g != e || h != e || i != e)
                {
                    var p = Src(x, y - 2);
                    var s = Src(x, y + 2);
                    int bl = Luma(b), dl = Luma(d), el = Luma(e), fl = Luma(f), hl = Luma(h);

                    // 1:1 slope rules
                    if (d == b && d != h && d != f && (el >= dl || e == a) && AnyEq(e, a, c, g) && (el < dl || a != d || e != p || e != q))
                    {
                        j = d;
                    }

                    if (b == f && b != d && b != h && (el >= bl || e == c) && AnyEq(e, a, c, i) && (el < bl || c != b || e != p || e != r))
                    {
                        k = b;
                    }

                    if (h == d && h != f && h != b && (el >= hl || e == g) && AnyEq(e, a, g, i) && (el < hl || g != h || e != s || e != q))
                    {
                        l = h;
                    }

                    if (f == h && f != b && f != d && (el >= fl || e == i) && AnyEq(e, c, g, i) && (el < fl || i != h || e != r || e != s))
                    {
                        m = f;
                    }

                    // Intersection rules
                    if (e != f && AllEq(e, c, i, d, q) && AllEq(f, b, h) && f != Src(x + 3, y))
                    {
                        k = m = f;
                    }

                    if (e != d && AllEq(e, a, g, f, r) && AllEq(d, b, h) && d != Src(x - 3, y))
                    {
                        j = l = d;
                    }

                    if (e != h && AllEq(e, g, i, b, p) && AllEq(h, d, f) && h != Src(x, y + 3))
                    {
                        l = m = h;
                    }

                    if (e != b && AllEq(e, a, c, h, s) && AllEq(b, d, f) && b != Src(x, y - 3))
                    {
                        j = k = b;
                    }

                    if (bl < el && AllEq(e, g, h, i, s) && NoneEq(e, a, d, c, f))
                    {
                        j = k = b;
                    }

                    if (hl < el && AllEq(e, a, b, c, p) && NoneEq(e, d, g, i, f))
                    {
                        l = m = h;
                    }

                    if (fl < el && AllEq(e, a, d, g, q) && NoneEq(e, b, c, i, h))
                    {
                        k = m = f;
                    }

                    if (dl < el && AllEq(e, c, f, i, r) && NoneEq(e, b, a, g, h))
                    {
                        j = l = d;
                    }

                    // 2:1 slope rules
                    if (h != b)
                    {
                        if (h != a && h != e && h != c)
                        {
                            if (AllEq(h, g, f, r) && h != d && h != Src(x + 2, y - 1))
                            {
                                l = m;
                            }

                            if (AllEq(h, i, d, q) && h != f && h != Src(x - 2, y - 1))
                            {
                                m = l;
                            }
                        }

                        if (b != i && b != g && b != e)
                        {
                            if (AllEq(b, a, f, r) && b != d && b != Src(x + 2, y + 1))
                            {
                                j = k;
                            }

                            if (AllEq(b, c, d, q) && b != f && b != Src(x - 2, y + 1))
                            {
                                k = j;
                            }
                        }
                    }

                    if (f != d)
                    {
                        if (d != i && d != e && d != c)
                        {
                            if (AllEq(d, a, h, s) && d != b && d != Src(x + 1, y + 2))
                            {
                                j = l;
                            }

                            if (AllEq(d, g, b, p) && d != h && d != Src(x + 1, y - 2))
                            {
                                l = j;
                            }
                        }

                        if (f != e && f != a && f != g)
                        {
                            if (AllEq(f, c, h, s) && f != b && f != Src(x - 1, y + 2))
                            {
                                k = m;
                            }

                            if (AllEq(f, i, b, p) && f != h && f != Src(x - 1, y - 2))
                            {
                                m = k;
                            }
                        }
                    }
                }

                var top = ((y * 2) * outputWidth) + (x * 2);
                output[top] = j;
                output[top + 1] = k;
                output[top + outputWidth] = l;
                output[top + outputWidth + 1] = m;
            }
        }

        return new IndexedImage(outputWidth, height * 2, output);
    }

    private static bool AnyEq(byte value, byte a, byte b, byte c) => value == a || value == b || value == c;

    private static bool AllEq(byte value, byte a, byte b) => value == a && value == b;

    private static bool AllEq(byte value, byte a, byte b, byte c) => value == a && value == b && value == c;

    private static bool AllEq(byte value, byte a, byte b, byte c, byte d) => value == a && value == b && value == c && value == d;

    private static bool NoneEq(byte value, byte a, byte b, byte c, byte d) => value != a && value != b && value != c && value != d;

    /// <summary>Maps each index to the lowest index with the same colour.</summary>
    private static byte[] CanonicalIndices(Palette palette)
    {
        var canonical = new byte[Palette.Size];
        for (var i = 0; i < Palette.Size; i++)
        {
            canonical[i] = (byte)i;
            for (var j = 0; j < i; j++)
            {
                if (palette[j] == palette[i])
                {
                    canonical[i] = (byte)j;
                    break;
                }
            }
        }

        return canonical;
    }

    /// <summary>MMPX's brightness measure: the sum of the channels, weighted up as transparency grows.</summary>
    private static int[] LumaTable(Palette palette)
    {
        var luma = new int[Palette.Size];
        for (var i = 0; i < Palette.Size; i++)
        {
            var c = palette[i];
            luma[i] = (c.R + c.G + c.B + 1) * (256 - c.A);
        }

        return luma;
    }
}
