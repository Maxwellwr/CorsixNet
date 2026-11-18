// The main menu's background, ported from CorsixTH CorsixTH/Lua/ui.lua (UI:setMenuBackground, UI:draw's background)
// and graphics.lua (the mainmenu palettes). The pictures are CorsixTH's own (CorsixTH/Bitmap/mainmenu480, 720 and 1080
// .dat with their .pal), carried in the client compressed.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.IO.Compression;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenWards.Formats;

namespace OpenWards.Client.Ui;

/// <summary>
/// The picture behind the main menu: the largest of CorsixTH's three sizes no higher than the window (the smallest if
/// none is), scaled to cover the screen and centred.
/// </summary>
internal sealed class MenuBackground : IDisposable
{
    /// <summary>The sizes there are (<c>menu_bg_sizes</c>).</summary>
    public static readonly (int Width, int Height)[] Sizes = [(640, 480), (1280, 720), (1920, 1080)];

    private readonly Texture2D _texture;

    private MenuBackground(Texture2D texture) => _texture = texture;

    /// <summary>The size <c>setMenuBackground</c> picks for a window of <paramref name="screenHeight"/> pixels.</summary>
    public static (int Width, int Height) SizeFor(int screenHeight)
    {
        var chosen = Sizes[0];
        foreach (var size in Sizes)
        {
            if (screenHeight >= size.Height)
            {
                chosen = size;
            }
            else
            {
                break;
            }
        }

        return chosen;
    }

    /// <summary>Loads the picture for the window's height; null if it is missing from the client.</summary>
    public static MenuBackground? Load(GraphicsDevice device, int screenHeight)
    {
        var (width, height) = SizeFor(screenHeight);
        var name = "mainmenu" + height;
        using var pixelsStream = typeof(MenuBackground).Assembly.GetManifestResourceStream("OpenWards.Client.Assets." + name + ".dat.gz");
        using var paletteStream = typeof(MenuBackground).Assembly.GetManifestResourceStream("OpenWards.Client.Assets." + name + ".pal");
        if (pixelsStream is null || paletteStream is null)
        {
            return null;
        }

        using var gzip = new GZipStream(pixelsStream, CompressionMode.Decompress);
        using var pixels = new MemoryStream();
        gzip.CopyTo(pixels);
        using var paletteBytes = new MemoryStream();
        paletteStream.CopyTo(paletteBytes);

        // _loadPalette with neither transparency nor 8-bit colours: six bits a component, every entry drawn.
        var palette = Palette.Parse(paletteBytes.ToArray(), eightBit: false, transparentLastEntry: false);
        var indices = pixels.ToArray();
        if (indices.Length < width * height)
        {
            return null;
        }

        var colours = new Color[width * height];
        for (var i = 0; i < colours.Length; i++)
        {
            var colour = palette[indices[i]];
            colours[i] = new Color(colour.R, colour.G, colour.B);
        }

        var texture = new Texture2D(device, width, height);
        texture.SetData(colours);
        return new MenuBackground(texture);
    }

    /// <summary><c>UI:draw</c>: scaled by the larger of the screen's two ratios to it, so it covers the screen, centred.</summary>
    public static Rectangle Cover(int width, int height, Rectangle screen)
    {
        var factor = Math.Max((double)screen.Width / width, (double)screen.Height / height);
        var w = (int)Math.Ceiling(width * factor);
        var h = (int)Math.Ceiling(height * factor);
        return new Rectangle(screen.X + ((screen.Width - w) / 2), screen.Y + ((screen.Height - h) / 2), w, h);
    }

    public void Draw(SpriteBatch batch, Rectangle screen) => batch.Draw(_texture, Cover(_texture.Width, _texture.Height, screen), Color.White);

    public void Dispose() => _texture.Dispose();
}
