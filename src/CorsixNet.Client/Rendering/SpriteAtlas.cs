using CorsixNet.Formats;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Rendering;

/// <summary>
/// All sprites of a sheet packed into one texture, so that drawing them does not switch textures.
/// </summary>
internal sealed class SpriteAtlas : IDisposable
{
    private const int AtlasWidth = 2048;

    /// <summary>Transparent border around each sprite so that filtering never samples a neighbour.</summary>
    private const int Padding = 1;

    private readonly Rectangle[] _sources;

    public SpriteAtlas(GraphicsDevice device, SpriteSheet sheet, Palette palette)
    {
        Sheet = sheet;
        _sources = new Rectangle[sheet.Count];

        // Shelf packing in sheet order.
        int x = 0, y = 0, rowHeight = 0;
        for (var i = 0; i < sheet.Count; i++)
        {
            var sprite = sheet[i];
            if (sprite.IsEmpty)
            {
                continue;
            }

            var cellWidth = sprite.Width + (2 * Padding);
            var cellHeight = sprite.Height + (2 * Padding);
            if (x + cellWidth > AtlasWidth)
            {
                x = 0;
                y += rowHeight;
                rowHeight = 0;
            }

            _sources[i] = new Rectangle(x + Padding, y + Padding, sprite.Width, sprite.Height);
            x += cellWidth;
            rowHeight = Math.Max(rowHeight, cellHeight);
        }

        var height = Math.Max(1, y + rowHeight);
        var pixels = new Color[AtlasWidth * height];
        for (var i = 0; i < sheet.Count; i++)
        {
            var sprite = sheet[i];
            var source = _sources[i];
            for (var row = 0; row < sprite.Height; row++)
            {
                for (var column = 0; column < sprite.Width; column++)
                {
                    var c = palette[sprite[column, row]];
                    pixels[((source.Y + row) * AtlasWidth) + source.X + column] =
                        new Color(c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255, (int)c.A);
                }
            }
        }

        Texture = new Texture2D(device, AtlasWidth, height);
        Texture.SetData(pixels);
    }

    public SpriteSheet Sheet { get; }

    public Texture2D Texture { get; }

    public int Count => _sources.Length;

    /// <summary>Area of sprite <paramref name="index"/> in <see cref="Texture"/>; empty for empty sprites.</summary>
    public Rectangle Source(int index) => index >= 0 && index < _sources.Length ? _sources[index] : Rectangle.Empty;

    /// <summary>Height of sprite <paramref name="index"/>, or 0 if it is empty or out of range.</summary>
    public int HeightOf(int index) => Source(index).Height;

    public void Draw(SpriteBatch batch, int index, int x, int y, Color tint)
    {
        var source = Source(index);
        if (!source.IsEmpty)
        {
            batch.Draw(Texture, new Vector2(x, y), source, tint);
        }
    }

    /// <summary>Draws the part of a sprite placed at (<paramref name="x"/>, <paramref name="y"/>) that lies inside <paramref name="clip"/>.</summary>
    public void DrawClipped(SpriteBatch batch, int index, int x, int y, Rectangle clip, Color tint)
    {
        var source = Source(index);
        var visible = Rectangle.Intersect(new Rectangle(x, y, source.Width, source.Height), clip);
        if (source.IsEmpty || visible.IsEmpty)
        {
            return;
        }

        var part = new Rectangle(source.X + visible.X - x, source.Y + visible.Y - y, visible.Width, visible.Height);
        batch.Draw(Texture, new Vector2(visible.X, visible.Y), part, tint);
    }

    public void Dispose() => Texture.Dispose();
}
