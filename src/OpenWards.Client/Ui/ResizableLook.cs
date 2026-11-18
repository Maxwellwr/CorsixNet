// The look of CorsixTH's own windows, ported from CorsixTH CorsixTH/Lua/dialogs/resizable.lua (UIResizable: the border of
// aux_ui sprites round a window of one colour) and window.lua (addBevelPanel and panel_bevel_draw: raised and lowered
// panels with a highlight and a shadow, Panel:drawLabel).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenWards.Client.Rendering;

namespace OpenWards.Client.Ui;

/// <summary>
/// Draws windows as CorsixTH draws the ones it added to the game (<c>UIResizable</c>): a background of one colour with a
/// frame of CorsixTH's aux_ui sprites, and bevelled panels and buttons in shades of that colour.
/// </summary>
internal sealed class ResizableLook : IDisposable
{
    private const int BorderOffset = 9;
    private const int BorderSize = 40;

    private readonly SpriteAtlas? _borders;
    private readonly SpriteAtlas? _tree;
    private readonly Texture2D _pixel;

    public ResizableLook(GraphicsDevice device, Formats.Palette palette)
    {
        _borders = AuxSprites.Load() is { } sheet ? new SpriteAtlas(device, sheet, palette) : null;
        _tree = AuxSprites.LoadTree() is var (treeSheet, treePalette) ? new SpriteAtlas(device, treeSheet, treePalette) : null;
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData([Color.White]);
    }

    /// <summary>Draws one of the tree control's sprites (<see cref="AuxSprites.LoadTree"/>) with its top left at x, y.</summary>
    public void DrawTreeIcon(SpriteBatch batch, int sprite, int x, int y) => _tree?.Draw(batch, sprite, x, y, Color.White);

    /// <summary><c>canvas:drawRect</c>: a rectangle of one colour.</summary>
    public void Fill(SpriteBatch batch, Rectangle area, Color colour) => batch.Draw(_pixel, area, colour);

    /// <summary>Whether CorsixTH's border sprites could be read.</summary>
    public bool IsAvailable => _borders is not null;

    /// <summary>
    /// <c>UIResizable:draw</c>: the background in its colour, then the frame: the edges every 40 pixels, 9 pixels out
    /// from the window, and the corners.
    /// </summary>
    public void DrawWindow(SpriteBatch batch, Rectangle area, Color colour)
    {
        batch.Draw(_pixel, area, colour);
        if (_borders is not { } sprites)
        {
            return;
        }

        int left = area.X - BorderOffset, upper = area.Y - BorderOffset;
        int right = area.X + area.Width, lower = area.Y + area.Height;
        int cornerRight = area.X + area.Width - BorderSize, cornerLower = area.Y + area.Height - BorderSize;
        for (var x = area.X + BorderSize; x <= cornerRight - 1; x += BorderSize)
        {
            sprites.Draw(batch, 11, x, upper, Color.White);
            sprites.Draw(batch, 16, x, lower, Color.White);
        }

        for (var y = area.Y + BorderSize; y <= cornerLower - 1; y += BorderSize)
        {
            sprites.Draw(batch, 13, left, y, Color.White);
            sprites.Draw(batch, 14, right, y, Color.White);
        }

        sprites.Draw(batch, 10, left, upper, Color.White);
        sprites.Draw(batch, 12, cornerRight, upper, Color.White);
        sprites.Draw(batch, 15, left, cornerLower, Color.White);
        sprites.Draw(batch, 17, cornerRight, cornerLower, Color.White);
    }

    /// <summary>
    /// <c>panel_bevel_draw</c>: raised, the shadow one pixel down and right, the highlight, then the colour inside; lowered
    /// (a button held down), the highlight, the shadow, then the colour inside. The label goes in the middle.
    /// </summary>
    public void DrawBevel(SpriteBatch batch, Rectangle area, Color colour, bool lowered, string? label = null, TextRenderer? font = null, Color? tint = null, Color? highlight = null, Color? shadow = null)
    {
        var (light, dark) = (highlight ?? Shade(colour, 60), shadow ?? Shade(colour, -60));
        if (lowered)
        {
            batch.Draw(_pixel, area, light);
            batch.Draw(_pixel, new Rectangle(area.X, area.Y, area.Width - 1, area.Height - 1), dark);
        }
        else
        {
            batch.Draw(_pixel, new Rectangle(area.X + 1, area.Y + 1, area.Width - 1, area.Height - 1), dark);
            batch.Draw(_pixel, new Rectangle(area.X, area.Y, area.Width - 1, area.Height - 1), light);
        }

        batch.Draw(_pixel, new Rectangle(area.X + 1, area.Y + 1, area.Width - 2, area.Height - 2), colour);
        if (label is not null && font is not null)
        {
            // A label too wide goes on two lines where the panel is high enough (a big button), else it is clipped.
            int Width(string text) => font.Measure(text).Width;
            var lines = Width(label) > area.Width - 4 && area.Height >= (2 * font.LineHeight) + 2
                ? Maps.MapViewScreen.WrapToWidth(label, area.Width - 4, Width).ToList()
                : [label];
            if (lines.Count > 2)
            {
                lines = [lines[0], string.Join(' ', lines.Skip(1))];
            }

            var top = area.Y + 1 + ((area.Height - (lines.Count * font.LineHeight)) / 2);
            foreach (var line in lines)
            {
                var text = ClipLine(line, area.Width - 4, Width);
                font.Draw(batch, text, new Vector2(area.X + 2 + ((area.Width - 4 - Width(text)) / 2), top), tint: tint);
                top += font.LineHeight;
            }
        }
    }

    /// <summary>
    /// <c>Panel:clipLine</c>: a label wider than its panel loses letters from its end until it fits with "..." after it.
    /// </summary>
    public static string ClipLine(string line, int limit, Func<string, int> width)
    {
        if (width(line) <= limit)
        {
            return line;
        }

        var room = limit - width("..._");
        while (line.Length > 0 && width(line) > room)
        {
            line = line[..^1];
        }

        return line + "...";
    }

    /// <summary>CorsixTH's panel colours (utility.lua's <c>Colours</c>): the default pale purple of its windows and buttons.</summary>
    public static readonly Color PanelDefault = new(154, 146, 198);

    /// <summary>A button under the mouse (<c>Colours.SettingHover</c>).</summary>
    public static readonly Color SettingHover = new(133, 128, 183);

    /// <summary>A disabled button (<c>Colours.Disabled</c>).</summary>
    public static readonly Color Disabled = new(127, 123, 149);

    /// <summary>A dialog's title (<c>Colours.Title</c>).</summary>
    public static readonly Color Title = new(174, 166, 218);

    /// <summary><c>addBevelPanel</c>'s highlight and shadow: each component 60 lighter or darker, kept within 0 to 255.</summary>
    public static Color Shade(Color colour, int by) =>
        new(Math.Clamp(colour.R + by, 0, 255), Math.Clamp(colour.G + by, 0, 255), Math.Clamp(colour.B + by, 0, 255));

    public void Dispose()
    {
        _pixel.Dispose();
        _borders?.Dispose();
        _tree?.Dispose();
    }
}
