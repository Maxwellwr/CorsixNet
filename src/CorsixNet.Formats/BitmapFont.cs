using System.Text;

namespace CorsixNet.Formats;

/// <summary>
/// A Theme Hospital bitmap font: a sprite sheet whose sprite <c>i</c> draws character code <c>31 + i</c>.
/// </summary>
public sealed class BitmapFont
{
    /// <summary>Character code drawn by the first sprite.</summary>
    public const int FirstCharacter = 31;

    private readonly IReadOnlyDictionary<int, int> _extra;

    public BitmapFont(SpriteSheet sheet, int letterSpacing = 0, int lineSpacing = 0)
        : this(sheet, letterSpacing, lineSpacing, new Dictionary<int, int>())
    {
    }

    private BitmapFont(SpriteSheet sheet, int letterSpacing, int lineSpacing, IReadOnlyDictionary<int, int> extra)
    {
        Sheet = sheet;
        LetterSpacing = letterSpacing;
        LineSpacing = lineSpacing;
        _extra = extra;
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
        if (_extra.TryGetValue(character.Value, out spriteIndex))
        {
            return true;
        }

        spriteIndex = Cp437.FromUnicode(character.Value) - FirstCharacter;
        return spriteIndex >= 0 && spriteIndex < Sheet.Count;
    }

    /// <summary>
    /// The font with glyphs for more characters than its code page has (such as <see cref="CyrillicGlyphs"/>), added
    /// after its own sprites; they win over characters the font already draws.
    /// </summary>
    public BitmapFont WithGlyphs(IReadOnlyDictionary<int, IndexedImage> glyphs)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        var sprites = Enumerable.Range(0, Sheet.Count).Select(i => Sheet[i]).ToList();
        var extra = new Dictionary<int, int>(_extra);
        foreach (var (code, image) in glyphs)
        {
            extra[code] = sprites.Count;
            sprites.Add(image);
        }

        return new BitmapFont(SpriteSheet.FromImages(sprites), LetterSpacing, LineSpacing, extra);
    }

    /// <summary>Whether the font has a glyph of its own for the character, not just its code page's '?'.</summary>
    public bool HasGlyph(Rune character)
    {
        if (_extra.ContainsKey(character.Value))
        {
            return true;
        }

        var code = Cp437.FromUnicode(character.Value);
        return code <= byte.MaxValue && Cp437.ToUnicode((byte)code) == character.Value
            && TryGetGlyph(character, out var sprite) && Sheet[sprite] is { Width: > 1, Height: > 1 };
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
