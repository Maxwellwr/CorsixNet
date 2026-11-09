using CorsixNet.Formats;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Rendering;

/// <summary>Draws single lines of text with a Theme Hospital bitmap font.</summary>
internal sealed class TextRenderer : IDisposable
{
    private readonly BitmapFont _font;
    private readonly SheetTextures _glyphs;

    public TextRenderer(GraphicsDevice device, BitmapFont font, Palette palette)
    {
        _font = font;
        _glyphs = new SheetTextures(device, font.Sheet, palette);
    }

    public int LineHeight => _font.MeasureLine("Mg").Height + _font.LineSpacing;

    public (int Width, int Height) Measure(string text) => _font.MeasureLine(text);

    public void Draw(SpriteBatch batch, string text, Vector2 position, int scale = 1, Color? tint = null)
    {
        foreach (var (sprite, x) in _font.Layout(text))
        {
            if (_glyphs[sprite] is { } glyph)
            {
                batch.Draw(glyph, position + new Vector2(x * scale, 0), null, tint ?? Color.White, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
            }
        }
    }

    public void Dispose() => _glyphs.Dispose();
}
