using System.Text;

namespace CorsixNet.Formats;

/// <summary>
/// A Theme Hospital bitmap font: a sprite sheet whose sprite <c>i</c> draws character code <c>31 + i</c>.
/// </summary>
public sealed class BitmapFont
{
    /// <summary>Character code drawn by the first sprite.</summary>
    public const int FirstCharacter = 31;

    public BitmapFont(SpriteSheet sheet, int letterSpacing = 0, int lineSpacing = 0)
    {
        Sheet = sheet;
        LetterSpacing = letterSpacing;
        LineSpacing = lineSpacing;
    }

    public SpriteSheet Sheet { get; }

    /// <summary>Extra horizontal pixels between characters; may be negative.</summary>
    public int LetterSpacing { get; }

    /// <summary>Extra vertical pixels between lines; may be negative.</summary>
    public int LineSpacing { get; }

    /// <summary>
    /// Finds the sprite for a Unicode character. Returns false for characters the
    /// font cannot draw; such characters take no space.
    /// </summary>
    public bool TryGetGlyph(Rune character, out int spriteIndex)
    {
        spriteIndex = Cp437.FromUnicode(character.Value) - FirstCharacter;
        return spriteIndex >= 0 && spriteIndex < Sheet.Count;
    }

    /// <summary>Lays out one line of text, yielding each glyph's sprite and horizontal offset.</summary>
    public IEnumerable<(int SpriteIndex, int X)> Layout(string text)
    {
        var x = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!TryGetGlyph(rune, out var sprite))
            {
                continue;
            }

            yield return (sprite, x);
            x += Sheet[sprite].Width + LetterSpacing;
        }
    }

    /// <summary>Measures one line of text: total advance width and the tallest glyph.</summary>
    public (int Width, int Height) MeasureLine(string text)
    {
        var width = 0;
        var height = 0;
        foreach (var (sprite, _) in Layout(text))
        {
            var glyph = Sheet[sprite];
            width += glyph.Width + LetterSpacing;
            height = Math.Max(height, glyph.Height);
        }

        return (width, height);
    }
}
