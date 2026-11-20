// The main menu's background, ported from CorsixTH CorsixTH/Lua/ui.lua (UI:setMenuBackground, UI:draw's background)
// and graphics.lua (the mainmenu palettes). The pictures are OpenWards' own, drawn by tools/menu-art/draw.py in CorsixTH's
// format (mainmenu720 and 1080: .dat with its .pal), carried in the client compressed, with the OpenWards sign
// (mainmenu_sign.png) laid over them apart, so that it can be kept clear of the menu at any shape of window.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.IO.Compression;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenWards.Formats;

namespace OpenWards.Client.Ui;

/// <summary>
/// The picture behind the main menu, scaled to cover the screen without losing its middle part, and the OpenWards
/// sign over it, kept on the screen and clear of the menu.
/// </summary>
internal sealed class MenuBackground : IDisposable
{
    /// <summary>The sizes there are, all of the picture's own 16:9.</summary>
    public static readonly (int Width, int Height)[] Sizes = [(1280, 720), (1920, 1080)];

    /// <summary>The picture's own size, in which <see cref="Keep"/> and <see cref="SignAt"/> are given.</summary>
    public static readonly Point Design = new(1920, 1080);

    /// <summary>
    /// The part of the picture that is always on the screen (the ward's door, its patients and the doctor at the
    /// window): on a narrow window the picture is scaled down to it, rather than cut into it.
    /// </summary>
    public static readonly Rectangle Keep = new(200, 30, 1260, 1050);

    /// <summary>Where the sign stands in the picture (above the yard, right of the menu's middle), when there is room.</summary>
    public static readonly Point SignAt = new(976, 48);

    /// <summary>How many of the picture's pixels each of the sign's cells takes across and down.</summary>
    public const int SignCell = 4;

    private readonly Texture2D _texture;
    private readonly Texture2D? _sign;

    private MenuBackground(Texture2D texture, Texture2D? sign, (int Width, int Height) size)
    {
        _texture = texture;
        _sign = sign;
        Size = size;
    }

    /// <summary>The size of the picture loaded.</summary>
    public (int Width, int Height) Size { get; }

    /// <summary>
    /// The size to load for a screen: the smallest one that is not blown up to cover it (the largest if each one is).
    /// </summary>
    public static (int Width, int Height) SizeFor(Point screen)
    {
        foreach (var size in Sizes)
        {
            var area = Place(size.Width, size.Height, new Rectangle(0, 0, screen.X, screen.Y));
            if (area.Width <= size.Width)
            {
                return size;
            }
        }

        return Sizes[^1];
    }

    /// <summary>Loads the picture for the screen; null if it is missing from the client.</summary>
    public static MenuBackground? Load(GraphicsDevice device, Point screen)
    {
        var (width, height) = SizeFor(screen);
        var name = "mainmenu" + height;
        var assembly = typeof(MenuBackground).Assembly;
        using var pixelsStream = assembly.GetManifestResourceStream("OpenWards.Client.Assets." + name + ".dat.gz");
        using var paletteStream = assembly.GetManifestResourceStream("OpenWards.Client.Assets." + name + ".pal");
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
        return new MenuBackground(texture, LoadSign(device), (width, height));
    }

    private static Texture2D? LoadSign(GraphicsDevice device)
    {
        using var stream = typeof(MenuBackground).Assembly.GetManifestResourceStream("OpenWards.Client.Assets.mainmenu_sign.png");
        if (stream is null)
        {
            return null;
        }

        // SpriteBatch blends premultiplied colours; the PNG's are straight.
        var sign = Texture2D.FromStream(device, stream);
        var colours = new Color[sign.Width * sign.Height];
        sign.GetData(colours);
        for (var i = 0; i < colours.Length; i++)
        {
            colours[i] = Color.FromNonPremultiplied(colours[i].R, colours[i].G, colours[i].B, colours[i].A);
        }

        sign.SetData(colours);
        return sign;
    }

    /// <summary>
    /// <c>UI:draw</c>: scaled to cover the screen and centred, but on a screen narrower than the picture's part to
    /// keep (<see cref="Keep"/>) scaled down to that part's width instead (bars above and below), and moved sideways
    /// so that the part is on the screen.
    /// </summary>
    public static Rectangle Place(int width, int height, Rectangle screen)
    {
        var keepLeft = (double)Keep.Left / Design.X * width;
        var keepWidth = (double)Keep.Width / Design.X * width;
        var keepTop = (double)Keep.Top / Design.Y * height;
        var keepHeight = (double)Keep.Height / Design.Y * height;
        var cover = Math.Max((double)screen.Width / width, (double)screen.Height / height);
        var factor = Math.Min(cover, Math.Min(screen.Width / keepWidth, screen.Height / keepHeight));
        var w = (int)Math.Ceiling(width * factor);
        var h = (int)Math.Ceiling(height * factor);
        var x = BringIn((screen.Width - w) / 2, keepLeft * factor, keepWidth * factor, screen.Width);
        var y = BringIn((screen.Height - h) / 2, keepTop * factor, keepHeight * factor, screen.Height);
        return new Rectangle(screen.X + x, screen.Y + y, w, h);
    }

    /// <summary>Scaled by the larger of the screen's two ratios to it, so that it covers the screen, centred.</summary>
    public static Rectangle Cover(int width, int height, Rectangle screen)
    {
        var factor = Math.Max((double)screen.Width / width, (double)screen.Height / height);
        var w = (int)Math.Ceiling(width * factor);
        var h = (int)Math.Ceiling(height * factor);
        return new Rectangle(screen.X + ((screen.Width - w) / 2), screen.Y + ((screen.Height - h) / 2), w, h);
    }

    /// <summary>
    /// The picture's offset along one side moved so that the part to keep, at <paramref name="start"/> for
    /// <paramref name="length"/> in the scaled picture, lies within the screen's <paramref name="size"/>.
    /// </summary>
    private static int BringIn(int offset, double start, double length, int size)
    {
        var first = offset + (int)Math.Floor(start);
        var last = offset + (int)Math.Ceiling(start + length);
        return first < 0 ? offset - first : last > size ? offset - (last - size) : offset;
    }

    /// <summary>
    /// Where the sign of <paramref name="sign"/> cells is drawn, and how many pixels a cell takes: its place in the
    /// picture when that is on the screen and clear of the menu; else above the menu, made smaller if it has to be;
    /// else, with no room above the menu, beside it at the top.
    /// </summary>
    public static (Rectangle Area, int Cell) PlaceSign(Rectangle picture, Rectangle screen, Rectangle? menu, Point sign)
    {
        var factor = (double)picture.Width / Design.X;
        var cell = Math.Max(1, (int)Math.Round(SignCell * factor));
        var gap = Math.Max(4, cell * 2);

        // Never wider than the screen.
        cell = Math.Max(1, Math.Min(cell, (screen.Width - (2 * gap)) / sign.X));
        var x = picture.X + (int)Math.Round(SignAt.X * factor);
        var y = picture.Y + (int)Math.Round(SignAt.Y * factor);
        y = Math.Max(screen.Y + gap, y);

        if (menu is { } m && new Rectangle(x, y, sign.X * cell, sign.Y * cell).Intersects(Inflated(m, gap)))
        {
            var above = (m.Top - screen.Y - (2 * gap)) / sign.Y;
            if (above >= 1)
            {
                cell = Math.Min(cell, above);
                y = Math.Min(y, m.Top - gap - (sign.Y * cell));
            }
            else
            {
                // No room above: beside the menu, on the wider side.
                var rightRoom = screen.Right - m.Right - (2 * gap);
                var leftRoom = m.Left - screen.X - (2 * gap);
                cell = Math.Max(1, Math.Min(cell, Math.Max(rightRoom, leftRoom) / sign.X));
                x = rightRoom >= leftRoom ? m.Right + gap : m.Left - gap - (sign.X * cell);
                y = screen.Y + gap;
            }
        }

        var w = sign.X * cell;
        var h = sign.Y * cell;
        x = Math.Clamp(x, screen.X + gap, Math.Max(screen.X + gap, screen.Right - gap - w));
        y = Math.Clamp(y, screen.Y + gap, Math.Max(screen.Y + gap, screen.Bottom - gap - h));
        return (new Rectangle(x, y, w, h), cell);
    }

    private static Rectangle Inflated(Rectangle area, int by)
    {
        area.Inflate(by, by);
        return area;
    }

    /// <summary>
    /// Draws the picture and the sign on the screen, the menu standing at <paramref name="menu"/> (null when not laid
    /// out yet). Ends the batch, and begins it again as the screens draw (alpha blended, point sampled).
    /// </summary>
    public void Draw(SpriteBatch batch, Texture2D pixel, Rectangle screen, Rectangle? menu)
    {
        batch.End();

        // The picture is scaled by any amount, so it is sampled smoothly rather than with uneven pixels.
        batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp);
        var picture = Place(_texture.Width, _texture.Height, screen);
        if (!picture.Contains(screen))
        {
            // Where the picture leaves bars, the picture covering the screen shows through, dimmed.
            batch.Draw(pixel, screen, Color.Black);
            batch.Draw(_texture, Cover(_texture.Width, _texture.Height, screen), new Color(56, 56, 56));
        }

        batch.Draw(_texture, picture, Color.White);
        batch.End();

        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        if (_sign is not null)
        {
            var (area, _) = PlaceSign(picture, screen, menu, new Point(_sign.Width, _sign.Height));
            batch.Draw(_sign, area, Color.White);
        }
    }

    public void Dispose()
    {
        _texture.Dispose();
        _sign?.Dispose();
    }
}
