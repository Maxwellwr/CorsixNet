// A TrueType face standing in for the game's bitmap font, ported from CorsixTH CorsixTH/Src/th_gfx_font.cpp
// (freetype_font::set_face, set_ideal_character_size, draw_text_wrapped), with FreeType 2 drawing the glyphs where its
// library is there and stb_truetype otherwise.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Text;
using StbTrueTypeSharp;

namespace CorsixNet.Client.Rendering;

/// <summary>
/// The first face of a TrueType or OpenType font file (or collection), sized to stand in for a bitmap font, laying out
/// and rasterising single lines of text.
/// </summary>
/// <remarks>
/// stb_truetype reads the file (which characters it has, kerning, the heights of its letters); FreeType, when its
/// library loads, draws the glyphs hinted to the pixel grid as CorsixTH's do. Without it the outlines are scaled
/// only, which blurs text this small.
/// </remarks>
internal sealed unsafe class TrueTypeFace : IDisposable
{
    private readonly StbTrueType.stbtt_fontinfo _info;
    private readonly Dictionary<int, int> _glyphIndices = [];
    private readonly Dictionary<(int Size, bool Monochrome, int Index), GlyphMetrics> _metrics = [];
    private readonly FreeTypeGlyphs? _hinted;
    private float _scale;

    private TrueTypeFace(StbTrueType.stbtt_fontinfo info, FreeTypeGlyphs? hinted)
    {
        _info = info;
        _hinted = hinted;
        SetPixelSize(14);
    }

    /// <summary>The height and width of the em square in pixels.</summary>
    public int PixelSize { get; private set; }

    /// <summary>Whether FreeType draws the glyphs, hinted to the pixel grid.</summary>
    public bool IsHinted => _hinted is not null;

    /// <summary>
    /// Whether glyphs are drawn one bit a pixel with full hinting, as crisp as the game's own fonts, rather than
    /// anti-aliased; only when <see cref="IsHinted"/>.
    /// </summary>
    public bool Monochrome
    {
        get => _hinted?.Monochrome ?? false;
        set
        {
            if (_hinted is not null && _hinted.Monochrome != value)
            {
                _hinted.Monochrome = value;
            }
        }
    }

    /// <summary>Reads a font file's first face (FreeType's <c>FT_New_Memory_Face</c> with index 0); null if it is not a font.</summary>
    public static TrueTypeFace? Load(byte[] data)
    {
        if (data.Length < 12)
        {
            return null;
        }

        int offset;
        fixed (byte* start = data)
        {
            offset = StbTrueType.stbtt_GetFontOffsetForIndex(start, 0);
        }

        if (offset < 0)
        {
            return null;
        }

        try
        {
            return StbTrueType.CreateFont(data, offset) is { } info ? new TrueTypeFace(info, FreeTypeGlyphs.Open(data)) : null;
        }
        catch (Exception exception) when (exception is IndexOutOfRangeException or ArgumentException or NullReferenceException)
        {
            // stb_truetype trusts the file's tables; a damaged file reads out of bounds.
            return null;
        }
    }

    /// <summary>Whether the face has a glyph for the character.</summary>
    public bool HasGlyph(int codepoint) => GlyphIndex(codepoint) != 0;

    /// <summary>The number of the text's distinct characters, white space aside, the face has no glyph for.</summary>
    public int MissingGlyphs(string text)
    {
        var missing = 0;
        var seen = new HashSet<int>();
        foreach (var rune in text.EnumerateRunes())
        {
            if (!Rune.IsWhiteSpace(rune) && !Rune.IsControl(rune) && seen.Add(rune.Value) && !HasGlyph(rune.Value))
            {
                missing++;
            }
        }

        return missing;
    }

    /// <summary>
    /// The em size at which the face's capitals and small letters are as tall as a bitmap font's (taller of the two,
    /// so that neither shrinks), and at least <paramref name="minimum"/> pixels.
    /// </summary>
    /// <remarks>
    /// CorsixTH's <c>set_ideal_character_size</c> sets the em square to the bitmap font's 'M' sprite, 9 by 12 pixels for
    /// the game's usual font, and then to at least 9 by 14, so that every face is squeezed to some 70% of its width and
    /// drawn larger than the game's letters. Keeping the face's proportions and matching the letters' heights instead
    /// draws text as tall as the game's own, which hinting keeps legible.
    /// </remarks>
    public int PixelSizeFor(int capitalHeight, int smallHeight, int minimum = 8)
    {
        var unitsPerEm = 1 / StbTrueType.stbtt_ScaleForMappingEmToPixels(_info, 1);
        var size = Math.Max(minimum, 1);
        if (capitalHeight > 0 && Top('H') is > 0 and var capitals)
        {
            size = Math.Max(size, (int)MathF.Round(capitalHeight * unitsPerEm / capitals));
        }

        if (smallHeight > 0 && Top('x') is > 0 and var small)
        {
            size = Math.Max(size, (int)MathF.Round(smallHeight * unitsPerEm / small));
        }

        return size;
    }

    /// <summary>
    /// Sizes the face's em square to so many pixels, high and wide (<c>FT_Set_Pixel_Sizes</c>), for what is laid out and
    /// rasterised from now on; glyphs of other sizes stay cached.
    /// </summary>
    public void SetPixelSize(int size)
    {
        size = Math.Max(size, 1);
        if (size == PixelSize)
        {
            return;
        }

        PixelSize = size;
        _scale = StbTrueType.stbtt_ScaleForMappingEmToPixels(_info, size);
        _hinted?.SetPixelSize(size);
    }

    /// <summary>Lays out one line of text as <c>draw_text_wrapped</c> does with no width limit.</summary>
    public TextLine Layout(string text)
    {
        List<PlacedGlyph> glyphs = [];
        var pen = 0;
        var previous = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            // A single line break is drawn as a space; this draws single lines only.
            var code = rune.Value == '\n' ? ' ' : rune.Value;
            var index = GlyphIndex(code);
            if (previous != 0 && index != 0)
            {
                pen += (int)MathF.Round(StbTrueType.stbtt_GetGlyphKernAdvance(_info, previous, index) * _scale);
            }

            var metrics = Metrics(index);
            glyphs.Add(new PlacedGlyph(index, pen, metrics));
            previous = index;
            pen += metrics.Advance;
        }

        // Baseline and height as CorsixTH's: the tallest bearing and the deepest descent, and one pixel more (the two
        // pixels of line spacing are given back after the last line). The bearing of 'l' is counted too, so that texts without tall
        // letters stand on the same baseline as others (CorsixTH uses it for empty lines only).
        var normal = Metrics(GlyphIndex('l'));
        var baseline = normal.BearingY;
        var descent = normal.Height - normal.BearingY;
        var width = 0;
        foreach (var glyph in glyphs)
        {
            baseline = Math.Max(baseline, glyph.Metrics.BearingY);
            descent = Math.Max(descent, glyph.Metrics.Height - glyph.Metrics.BearingY);
            width = Math.Max(width, glyph.X + glyph.Metrics.BearingX + glyph.Metrics.Width);
        }

        var height = glyphs.Count == 0 ? 0 : 1 + baseline + descent;
        return new TextLine(glyphs, glyphs.Count == 0 ? 0 : 1 + width, height, baseline);
    }

    /// <summary>A glyph's grey-scale coverage (0 to 255), row by row; empty for a blank glyph.</summary>
    public GlyphBitmap Rasterize(int glyphIndex)
    {
        if (_hinted is not null)
        {
            var (hinted, coverage) = _hinted.Glyph(glyphIndex);
            return new GlyphBitmap(hinted.Width, hinted.Height, coverage);
        }

        var metrics = Metrics(glyphIndex);
        if (metrics.Width <= 0 || metrics.Height <= 0)
        {
            return new GlyphBitmap(0, 0, []);
        }

        var pixels = new byte[metrics.Width * metrics.Height];
        fixed (byte* output = pixels)
        {
            StbTrueType.stbtt_MakeGlyphBitmapSubpixel(_info, output, metrics.Width, metrics.Height, metrics.Width, _scale, _scale, 0, 0, glyphIndex);
        }

        return new GlyphBitmap(metrics.Width, metrics.Height, pixels);
    }

    public void Dispose()
    {
        _hinted?.Dispose();
        _info.Dispose();
    }

    /// <summary>The top of the character's outline above the baseline, in font units; 0 if the face has no such glyph.</summary>
    private int Top(char character)
    {
        var index = GlyphIndex(character);
        int x0, y0, x1, y1;
        return index != 0 && StbTrueType.stbtt_GetGlyphBox(_info, index, &x0, &y0, &x1, &y1) != 0 ? y1 : 0;
    }

    private int GlyphIndex(int codepoint)
    {
        if (!_glyphIndices.TryGetValue(codepoint, out var index))
        {
            index = StbTrueType.stbtt_FindGlyphIndex(_info, codepoint);
            _glyphIndices[codepoint] = index;
        }

        return index;
    }

    private GlyphMetrics Metrics(int glyphIndex)
    {
        var key = (PixelSize, Monochrome, glyphIndex);
        if (_metrics.TryGetValue(key, out var metrics))
        {
            return metrics;
        }

        if (_hinted is not null)
        {
            metrics = _hinted.Glyph(glyphIndex).Metrics;
            _metrics[key] = metrics;
            return metrics;
        }

        int advance, bearing, x0, y0, x1, y1;
        StbTrueType.stbtt_GetGlyphHMetrics(_info, glyphIndex, &advance, &bearing);
        StbTrueType.stbtt_GetGlyphBitmapBoxSubpixel(_info, glyphIndex, _scale, _scale, 0, 0, &x0, &y0, &x1, &y1);

        // FreeType's hinted advances are whole pixels; so are these.
        metrics = new GlyphMetrics((int)MathF.Round(advance * _scale), x0, -y0, x1 - x0, y1 - y0);
        _metrics[key] = metrics;
        return metrics;
    }
}

/// <summary>A glyph's size in pixels: the advance, the bitmap's offset from the pen (up from the baseline) and size.</summary>
internal readonly record struct GlyphMetrics(int Advance, int BearingX, int BearingY, int Width, int Height);

/// <summary>A glyph of a laid out line: its index in the face, the pen's position and its metrics.</summary>
internal readonly record struct PlacedGlyph(int Index, int X, GlyphMetrics Metrics);

/// <summary>A laid out line: its glyphs, the size of its bitmap and the baseline's distance from the top.</summary>
internal sealed record TextLine(IReadOnlyList<PlacedGlyph> Glyphs, int Width, int Height, int Baseline);

/// <summary>A glyph's coverage, 0 to 255, row by row.</summary>
internal sealed record GlyphBitmap(int Width, int Height, byte[] Pixels);
