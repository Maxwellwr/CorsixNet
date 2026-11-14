// A TrueType face standing in for the game's bitmap font, ported from CorsixTH CorsixTH/Src/th_gfx_font.cpp
// (freetype_font::set_face, set_ideal_character_size, draw_text_wrapped) with stb_truetype in place of FreeType 2.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Text;
using StbTrueTypeSharp;

namespace CorsixNet.Client.Rendering;

/// <summary>
/// The first face of a TrueType or OpenType font file (or collection), sized as CorsixTH's <c>freetype_font</c> sizes
/// it to replace a bitmap font, laying out and rasterising single lines of text.
/// </summary>
/// <remarks>
/// FreeType's bitmap strikes are not looked for: stb_truetype draws outlines only, so a small font is always scaled up
/// as CorsixTH does when a face has no strike of the size.
/// </remarks>
internal sealed unsafe class TrueTypeFace : IDisposable
{
    private readonly StbTrueType.stbtt_fontinfo _info;
    private readonly Dictionary<int, int> _glyphIndices = [];
    private readonly Dictionary<int, GlyphMetrics> _metrics = [];
    private float _scaleX;
    private float _scaleY;

    private TrueTypeFace(StbTrueType.stbtt_fontinfo info)
    {
        _info = info;
        SetIdealCharacterSize(9, 14);
    }

    /// <summary>The pixel size asked of <see cref="SetIdealCharacterSize"/> after CorsixTH's adjustments.</summary>
    public (int Width, int Height) PixelSize { get; private set; }

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
            return StbTrueType.CreateFont(data, offset) is { } info ? new TrueTypeFace(info) : null;
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
    /// <c>set_ideal_character_size</c> without bitmap strikes: the size of the bitmap font's standard character,
    /// scaled up to at least 14 pixels high and 9 wide, as vector fonts look poor smaller (<c>FT_Set_Pixel_Sizes</c>).
    /// </summary>
    public void SetIdealCharacterSize(int width, int height)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        if (height < 14)
        {
            width = width * 14 / height;
            height = 14;
        }

        if (width < 9)
        {
            height = height * 9 / width;
            width = 9;
        }

        PixelSize = (width, height);
        _scaleX = StbTrueType.stbtt_ScaleForMappingEmToPixels(_info, width);
        _scaleY = StbTrueType.stbtt_ScaleForMappingEmToPixels(_info, height);
        _metrics.Clear();
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
                pen += (int)MathF.Round(StbTrueType.stbtt_GetGlyphKernAdvance(_info, previous, index) * _scaleX);
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
        var metrics = Metrics(glyphIndex);
        if (metrics.Width <= 0 || metrics.Height <= 0)
        {
            return new GlyphBitmap(0, 0, []);
        }

        var pixels = new byte[metrics.Width * metrics.Height];
        fixed (byte* output = pixels)
        {
            StbTrueType.stbtt_MakeGlyphBitmapSubpixel(_info, output, metrics.Width, metrics.Height, metrics.Width, _scaleX, _scaleY, 0, 0, glyphIndex);
        }

        return new GlyphBitmap(metrics.Width, metrics.Height, pixels);
    }

    public void Dispose() => _info.Dispose();

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
        if (_metrics.TryGetValue(glyphIndex, out var metrics))
        {
            return metrics;
        }

        int advance, bearing, x0, y0, x1, y1;
        StbTrueType.stbtt_GetGlyphHMetrics(_info, glyphIndex, &advance, &bearing);
        StbTrueType.stbtt_GetGlyphBitmapBoxSubpixel(_info, glyphIndex, _scaleX, _scaleY, 0, 0, &x0, &y0, &x1, &y1);

        // FreeType's hinted advances are whole pixels; so are these.
        metrics = new GlyphMetrics((int)MathF.Round(advance * _scaleX), x0, -y0, x1 - x0, y1 - y0);
        _metrics[glyphIndex] = metrics;
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
