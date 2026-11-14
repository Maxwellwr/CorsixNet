// The fullscreen dialogs' graphics, after CorsixTH CorsixTH/Lua/graphics.lua (Graphics:loadRaw, loadSpriteTable and
// loadFontAndSpriteTable with a dialog's own palette).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Client.Rendering;
using CorsixNet.Formats;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Ui;

/// <summary>
/// Loads, once each, the pictures, sprite sheets and fonts of the original's fullscreen dialogs from <c>QData</c>, each
/// with its dialog's palette (such as <c>Bank01V.pal</c>). What cannot be read is null, so a dialog falls back to its
/// table of text.
/// </summary>
internal sealed class OriginalGraphics(GraphicsDevice device, ThemeHospitalInstall install) : IDisposable
{
    private readonly Dictionary<string, Texture2D?> _pictures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SpriteAtlas?> _sheets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextRenderer?> _fonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Palette?> _palettes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A 640 by 480 picture (<c>loadRaw</c>) with its palette, both named <paramref name="name"/>.</summary>
    public Texture2D? Picture(string name)
    {
        if (!_pictures.TryGetValue(name, out var picture))
        {
            picture = Palette(name) is { } palette ? Try(() => TextureFactory.Create(device, install.LoadRawBitmap("QDATA", name), palette)) : null;
            _pictures[name] = picture;
        }

        return picture;
    }

    /// <summary>A sheet of sprites (<c>loadSpriteTable</c>, complex) with the palette named <paramref name="palette"/>.</summary>
    public SpriteAtlas? Sheet(string name, string palette)
    {
        var key = name + "/" + palette;
        if (!_sheets.TryGetValue(key, out var sheet))
        {
            sheet = Palette(palette) is { } colours ? Try(() => new SpriteAtlas(device, install.LoadSpriteSheet("QDATA", name, complex: true), colours)) : null;
            _sheets[key] = sheet;
        }

        return sheet;
    }

    /// <summary>A bitmap font (<c>loadFontAndSpriteTable</c>) with the palette named <paramref name="palette"/>.</summary>
    public TextRenderer? Font(string name, string palette)
    {
        var key = name + "/" + palette;
        if (!_fonts.TryGetValue(key, out var font))
        {
            font = Palette(palette) is { } colours ? Try(() => new TextRenderer(device, new BitmapFont(install.LoadSpriteSheet("QDATA", name, complex: false)), colours)) : null;
            _fonts[key] = font;
        }

        return font;
    }

    public void Dispose()
    {
        foreach (var picture in _pictures.Values)
        {
            picture?.Dispose();
        }

        foreach (var sheet in _sheets.Values)
        {
            sheet?.Dispose();
        }

        foreach (var font in _fonts.Values)
        {
            font?.Dispose();
        }
    }

    private Palette? Palette(string name)
    {
        if (!_palettes.TryGetValue(name, out var palette))
        {
            palette = Try(() => install.LoadPalette("QDATA", name + ".PAL"));
            _palettes[name] = palette;
        }

        return palette;
    }

    private static T? Try<T>(Func<T?> load)
        where T : class
    {
        try
        {
            return load();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }
}
