// Hinted glyphs from FreeType, as CorsixTH CorsixTH/Src/th_gfx_font.cpp (freetype_font) renders them.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Runtime.InteropServices;
using FreeTypeSharp;
using static FreeTypeSharp.FT;

namespace CorsixNet.Client.Rendering;

/// <summary>
/// A font face opened with FreeType, giving glyphs hinted to the pixel grid: whole-pixel advances, stems and heights
/// that land on pixels, and the font's own bitmap strikes where it has them. stb_truetype, which <see cref="TrueTypeFace"/>
/// falls back on when FreeType's library cannot be loaded, scales the outlines only, which blurs small text.
/// </summary>
internal sealed unsafe class FreeTypeGlyphs : IDisposable
{
    private static readonly Lazy<nint> Library = new(OpenLibrary);

    private readonly Dictionary<(int Size, bool Monochrome, int Index), (GlyphMetrics Metrics, byte[] Pixels)> _glyphs = [];
    private int _size = 14;
    private int _faceSize;
    private GCHandle _data;
    private FT_FaceRec_* _face;

    private FreeTypeGlyphs(GCHandle data, FT_FaceRec_* face)
    {
        _data = data;
        _face = face;
    }

    /// <summary>
    /// Whether glyphs are drawn without anti-aliasing, one bit a pixel as the game's own fonts are, with the font's full
    /// hinting; otherwise they are anti-aliased and hinted vertically only, as CorsixTH draws them.
    /// </summary>
    public bool Monochrome { get; set; }

    /// <summary>Opens the file's first face; null if FreeType is not there or cannot read it.</summary>
    public static FreeTypeGlyphs? Open(byte[] data)
    {
        var library = Library.Value;
        if (library == 0)
        {
            return null;
        }

        // FreeType reads the file where it is for as long as the face is open.
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        FT_FaceRec_* face;
        if (FT_New_Memory_Face((FT_LibraryRec_*)library, (byte*)handle.AddrOfPinnedObject(), data.Length, 0, &face) != FT_Error.FT_Err_Ok)
        {
            handle.Free();
            return null;
        }

        return new FreeTypeGlyphs(handle, face);
    }

    /// <summary>
    /// The size of glyphs from now on: <c>FT_Set_Pixel_Sizes</c> with the same width and height, so that glyphs keep their
    /// proportions. Glyphs of each size are kept, so that text of several sizes can share the face.
    /// </summary>
    public void SetPixelSize(int size) => _size = Math.Max(size, 1);

    /// <summary>The glyph's hinted metrics and its coverage, 0 or 255 in monochrome, row by row.</summary>
    public (GlyphMetrics Metrics, byte[] Pixels) Glyph(int index)
    {
        if (_glyphs.TryGetValue((_size, Monochrome, index), out var glyph))
        {
            return glyph;
        }

        if (_faceSize != _size)
        {
            _ = FT_Set_Pixel_Sizes(_face, 0, (uint)_size);
            _faceSize = _size;
        }

        glyph = (new GlyphMetrics(0, 0, 0, 0, 0), []);
        var load = Monochrome ? (FT_LOAD)FT_LOAD_TARGET_MONO : FT_LOAD.FT_LOAD_DEFAULT;
        var mode = Monochrome ? FT_Render_Mode_.FT_RENDER_MODE_MONO : FT_Render_Mode_.FT_RENDER_MODE_NORMAL;
        if (FT_Load_Glyph(_face, (uint)index, load) == FT_Error.FT_Err_Ok && FT_Render_Glyph(_face->glyph, mode) == FT_Error.FT_Err_Ok)
        {
            var slot = _face->glyph;
            var bitmap = slot->bitmap;
            var width = (int)bitmap.width;
            var height = (int)bitmap.rows;
            var pixels = new byte[width * height];
            var mono = bitmap.pixel_mode == FT_Pixel_Mode_.FT_PIXEL_MODE_MONO;
            for (var y = 0; y < height; y++)
            {
                var row = bitmap.buffer + (y * bitmap.pitch);
                for (var x = 0; x < width; x++)
                {
                    pixels[(y * width) + x] = mono ? (byte)(((row[x >> 3] >> (7 - (x & 7))) & 1) * 255) : row[x];
                }
            }

            var advance = (int)((slot->advance.x + 32) >> 6);
            glyph = (new GlyphMetrics(advance, slot->bitmap_left, slot->bitmap_top, width, height), pixels);
        }

        _glyphs[(_size, Monochrome, index)] = glyph;
        return glyph;
    }

    public void Dispose()
    {
        if (_face is not null)
        {
            _ = FT_Done_Face(_face);
            _face = null;
            _data.Free();
        }
    }

    private static nint OpenLibrary()
    {
        try
        {
            FT_LibraryRec_* library;
            return FT_Init_FreeType(&library) == FT_Error.FT_Err_Ok ? (nint)library : 0;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return 0;
        }
    }
}
