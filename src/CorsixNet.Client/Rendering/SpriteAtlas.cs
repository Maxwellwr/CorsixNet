using CorsixNet.Formats;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Rendering;

/// <summary>
/// All sprites of a sheet packed into a few textures, so that drawing them rarely switches
/// textures. Sprites can be stored enlarged with <see cref="PixelArtScaler"/>; drawing still
/// takes world coordinates (original pixels), so callers draw with a matching transform.
/// </summary>
internal sealed class SpriteAtlas : IDisposable
{
    /// <summary>Texture size; 2048 is safe on every GPU MonoGame supports.</summary>
    private const int PageSize = 2048;

    /// <summary>Transparent border around each sprite so that filtering never samples a neighbour.</summary>
    private const int Padding = 1;

    private readonly GraphicsDevice _device;
    private readonly Palette _palette;
    private readonly Func<IndexedImage, bool> _isFloorTile;
    private readonly List<Texture2D> _pages = [];
    private (int Page, Rectangle Source)[] _entries = [];

    /// <param name="device">Graphics device.</param>
    /// <param name="sheet">Sprites to pack.</param>
    /// <param name="palette">Palette to draw them with.</param>
    /// <param name="scale">Initial scale: 1, 2 or 4.</param>
    /// <param name="isFloorTile">
    /// Picks the sprites that tile the floor; they are enlarged with <see cref="PixelArtScaler.ScaleTile"/>
    /// so that no seams open between them.
    /// </param>
    public SpriteAtlas(GraphicsDevice device, SpriteSheet sheet, Palette palette, int scale = 1, Func<IndexedImage, bool>? isFloorTile = null)
    {
        _device = device;
        _palette = palette;
        _isFloorTile = isFloorTile ?? (_ => false);
        Sheet = sheet;
        SetScale(scale);
    }

    public SpriteSheet Sheet { get; }

    /// <summary>How many texture pixels each original pixel has along each axis: 1, 2 or 4.</summary>
    public int Scale { get; private set; }

    public int Count => _entries.Length;

    /// <summary>Rebuilds the textures for another scale. Enlarging takes a moment for big sheets.</summary>
    public void SetScale(int scale)
    {
        if (scale is not (1 or 2 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "Scale must be 1, 2 or 4.");
        }

        Scale = scale;
        var passes = scale switch { 1 => 0, 2 => 1, _ => 2 };
        var images = new IndexedImage[Sheet.Count];
        Parallel.For(0, images.Length, i => images[i] = _isFloorTile(Sheet[i])
            ? PixelArtScaler.ScaleTile(Sheet[i], _palette, passes)
            : PixelArtScaler.Scale(Sheet[i], _palette, passes));

        DisposePages();
        _entries = new (int, Rectangle)[images.Length];
        var placements = Pack(images);
        foreach (var page in placements.GroupBy(p => p.Page))
        {
            var height = page.Max(p => p.Source.Bottom + Padding);
            var pixels = new Color[PageSize * height];
            foreach (var (index, _, source) in page)
            {
                var image = images[index];
                for (var row = 0; row < image.Height; row++)
                {
                    for (var column = 0; column < image.Width; column++)
                    {
                        var c = _palette[image[column, row]];
                        pixels[((source.Y + row) * PageSize) + source.X + column] =
                            new Color(c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255, (int)c.A);
                    }
                }
            }

            var texture = new Texture2D(_device, PageSize, height);
            texture.SetData(pixels);
            _pages.Add(texture);
        }

        foreach (var (index, page, source) in placements)
        {
            _entries[index] = (page, source);
        }
    }

    /// <summary>Width of sprite <paramref name="index"/> in original pixels, or 0 if it is empty or out of range.</summary>
    public int WidthOf(int index) => IsValid(index) ? Sheet[index].Width : 0;

    /// <summary>Height of sprite <paramref name="index"/> in original pixels, or 0 if it is empty or out of range.</summary>
    public int HeightOf(int index) => IsValid(index) ? Sheet[index].Height : 0;

    public void Draw(SpriteBatch batch, int index, int x, int y, Color tint) => Draw(batch, index, new Vector2(x, y), tint, SpriteEffects.None);

    /// <summary>Draws sprite <paramref name="index"/> with its top-left corner at <paramref name="position"/> (world coordinates).</summary>
    public void Draw(SpriteBatch batch, int index, Vector2 position, Color tint, SpriteEffects effects)
    {
        if (!IsValid(index) || Sheet[index].IsEmpty)
        {
            return;
        }

        var (page, source) = _entries[index];
        batch.Draw(_pages[page], position, source, tint, 0, Vector2.Zero, 1f / Scale, effects, 0);
    }

    /// <summary>
    /// Draws the rows of sprite <paramref name="index"/>, placed with its top-left corner at <paramref name="position"/>,
    /// that lie from <paramref name="top"/> down to before <paramref name="bottom"/> (world coordinates).
    /// </summary>
    public void DrawRows(SpriteBatch batch, int index, Vector2 position, Color tint, SpriteEffects effects, int top, int bottom)
    {
        if (!IsValid(index) || Sheet[index].IsEmpty)
        {
            return;
        }

        var height = Sheet[index].Height;
        var y = (int)position.Y;
        var first = Math.Max(0, top - y);
        var last = Math.Min(height, bottom - y);
        if (first >= last)
        {
            return;
        }

        // Rows of the source counted from its top; a vertically flipped sprite shows them the other way up.
        var sourceFirst = (effects & SpriteEffects.FlipVertically) != 0 ? height - last : first;
        var (page, source) = _entries[index];
        var part = new Rectangle(source.X, source.Y + (sourceFirst * Scale), source.Width, (last - first) * Scale);
        batch.Draw(_pages[page], new Vector2(position.X, y + first), part, tint, 0, Vector2.Zero, 1f / Scale, effects, 0);
    }

    /// <summary>
    /// Draws the part of sprite <paramref name="index"/>, placed with its top-left corner at <paramref name="position"/>,
    /// that lies inside <paramref name="clip"/> (world coordinates), mirrored as <paramref name="effects"/> say.
    /// </summary>
    public void DrawCropped(SpriteBatch batch, int index, Vector2 position, Color tint, SpriteEffects effects, Rectangle clip)
    {
        if (!IsValid(index) || Sheet[index].IsEmpty)
        {
            return;
        }

        var sprite = Sheet[index];
        int x = (int)position.X, y = (int)position.Y;
        var visible = Rectangle.Intersect(new Rectangle(x, y, sprite.Width, sprite.Height), clip);
        if (visible.IsEmpty)
        {
            return;
        }

        // Columns and rows of the source counted from its top-left; a flipped sprite shows them the other way round.
        var firstColumn = visible.X - x;
        var firstRow = visible.Y - y;
        var sourceColumn = (effects & SpriteEffects.FlipHorizontally) != 0 ? sprite.Width - firstColumn - visible.Width : firstColumn;
        var sourceRow = (effects & SpriteEffects.FlipVertically) != 0 ? sprite.Height - firstRow - visible.Height : firstRow;
        var (page, source) = _entries[index];
        var part = new Rectangle(source.X + (sourceColumn * Scale), source.Y + (sourceRow * Scale), visible.Width * Scale, visible.Height * Scale);
        batch.Draw(_pages[page], new Vector2(visible.X, visible.Y), part, tint, 0, Vector2.Zero, 1f / Scale, effects, 0);
    }

    /// <summary>Draws the part of a sprite placed at (<paramref name="x"/>, <paramref name="y"/>) that lies inside <paramref name="clip"/> (world coordinates).</summary>
    public void DrawClipped(SpriteBatch batch, int index, int x, int y, Rectangle clip, Color tint)
    {
        if (!IsValid(index) || Sheet[index].IsEmpty)
        {
            return;
        }

        var sprite = Sheet[index];
        var visible = Rectangle.Intersect(new Rectangle(x, y, sprite.Width, sprite.Height), clip);
        if (visible.IsEmpty)
        {
            return;
        }

        var (page, source) = _entries[index];
        var part = new Rectangle(
            source.X + ((visible.X - x) * Scale),
            source.Y + ((visible.Y - y) * Scale),
            visible.Width * Scale,
            visible.Height * Scale);
        batch.Draw(_pages[page], new Vector2(visible.X, visible.Y), part, tint, 0, Vector2.Zero, 1f / Scale, SpriteEffects.None, 0);
    }

    public void Dispose() => DisposePages();

    private bool IsValid(int index) => index >= 0 && index < _entries.Length;

    /// <summary>Shelf packing in sheet order, starting a new page when one fills up.</summary>
    private static List<(int Index, int Page, Rectangle Source)> Pack(IndexedImage[] images)
    {
        var placements = new List<(int, int, Rectangle)>();
        int page = 0, x = 0, y = 0, rowHeight = 0;
        for (var i = 0; i < images.Length; i++)
        {
            var image = images[i];
            if (image.IsEmpty)
            {
                continue;
            }

            var cellWidth = image.Width + (2 * Padding);
            var cellHeight = image.Height + (2 * Padding);
            if (cellWidth > PageSize || cellHeight > PageSize)
            {
                throw new InvalidOperationException($"Sprite {i} ({image.Width}x{image.Height}) does not fit in a {PageSize} texture.");
            }

            if (x + cellWidth > PageSize)
            {
                x = 0;
                y += rowHeight;
                rowHeight = 0;
            }

            if (y + cellHeight > PageSize)
            {
                page++;
                x = 0;
                y = 0;
                rowHeight = 0;
            }

            placements.Add((i, page, new Rectangle(x + Padding, y + Padding, image.Width, image.Height)));
            x += cellWidth;
            rowHeight = Math.Max(rowHeight, cellHeight);
        }

        return placements;
    }

    private void DisposePages()
    {
        foreach (var page in _pages)
        {
            page.Dispose();
        }

        _pages.Clear();
    }
}
