using System.Text;
using CorsixNet.Formats;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Rendering;

/// <summary>
/// Draws single lines of text with a Theme Hospital bitmap font, or with a TrueType face standing in for it when the
/// language needs characters the bitmap font does not have (CorsixTH's <c>Graphics:loadLanguageFont</c>, whose font
/// proxy lets the language change the font of text already set up).
/// </summary>
internal sealed class TextRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly BitmapFont _font;
    private readonly SheetTextures _glyphs;
    private readonly BitmapFontMatch? _match;
    private readonly Dictionary<int, Texture2D?> _trueTypeGlyphs = [];
    private TrueTypeFace? _face;
    private int _trueTypeSize;
    private int _trueTypeLineHeight;
    private int _bitmapShift;

    public TextRenderer(GraphicsDevice device, BitmapFont font, Palette palette)
    {
        _device = device;
        _font = font;
        _glyphs = new SheetTextures(device, font.Sheet, palette);
        _match = BitmapFontMatch.Find(font.Sheet, palette);
    }

    /// <summary>Whether text is drawn with a TrueType face rather than the bitmap font.</summary>
    public bool IsTrueType => _face is not null;

    /// <summary>
    /// Whether lines the bitmap font has every character of (Latin text, numbers) are still drawn with it while a
    /// TrueType face is in use, standing on the face's baseline; true by default.
    /// </summary>
    public bool BitmapForLatin { get; set; } = Environment.GetEnvironmentVariable("CNET_NOLATIN") != "1";

    public int LineHeight => _face is null ? _font.MeasureLine("Mg").Height + _font.LineSpacing : _trueTypeLineHeight;

    /// <summary>
    /// Draws with the face from now on, coloured after the bitmap font (<c>freetype_font:setFontOptions</c>) and sized
    /// so that its letters are as tall as the bitmap font's (see <see cref="TrueTypeFace.PixelSizeFor"/>), but at least
    /// <paramref name="minimumSize"/> pixels to the em, as ideograms need more pixels than letters; null goes back to
    /// the bitmap font.
    /// </summary>
    public void UseTrueType(TrueTypeFace? face, int minimumSize = 8)
    {
        if (ReferenceEquals(face, _face) && (face is null || _trueTypeSize == SizeFor(face, minimumSize)))
        {
            return;
        }

        DisposeTrueTypeGlyphs();
        _face = face;
        if (face is not null)
        {
            _trueTypeSize = SizeFor(face, minimumSize);
            face.Monochrome = Environment.GetEnvironmentVariable("CNET_MONO") == "1";
            face.SetPixelSize(_trueTypeSize);
            var standard = face.Layout("Mg");
            _trueTypeLineHeight = standard.Height;
            _bitmapShift = _match is { Baseline: > 0 } match ? standard.Baseline - match.Baseline : 0;
        }
    }

    /// <summary>Whether the text is drawn with the TrueType face, rather than with the bitmap font.</summary>
    public bool IsTrueTypeFor(string text) => _face is not null && !(BitmapForLatin && BitmapCanDraw(text));

    public (int Width, int Height) Measure(string text)
    {
        if (_face is null)
        {
            return _font.MeasureLine(text);
        }

        if (!IsTrueTypeFor(text))
        {
            var (width, height) = _font.MeasureLine(text);
            return (width, Math.Max(height + _bitmapShift, _trueTypeLineHeight));
        }

        _face.SetPixelSize(_trueTypeSize);
        var line = _face.Layout(text);
        return (line.Width, line.Height);
    }

    public void Draw(SpriteBatch batch, string text, Vector2 position, int scale = 1, Color? tint = null)
    {
        if (_face is not null)
        {
            if (IsTrueTypeFor(text))
            {
                DrawTrueType(batch, _face, text, position, scale, tint ?? Color.White);
                return;
            }

            position.Y += _bitmapShift * scale;
        }

        foreach (var (sprite, x) in _font.Layout(text))
        {
            if (_glyphs[sprite] is { } glyph)
            {
                batch.Draw(glyph, position + new Vector2(x * scale, 0), null, tint ?? Color.White, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
            }
        }
    }

    public void Dispose()
    {
        _glyphs.Dispose();
        DisposeTrueTypeGlyphs();
    }

    /// <summary>
    /// Each glyph at its place on the line, in the bitmap font's colour (<c>ttf_color</c> left to be detected) times the
    /// tint; CorsixTH renders the whole line into one texture, which comes to the same pixels.
    /// </summary>
    private void DrawTrueType(SpriteBatch batch, TrueTypeFace face, string text, Vector2 position, int scale, Color tint)
    {
        var color = _match is { Color: var c } ? new Color(c.R * tint.R / 255, c.G * tint.G / 255, c.B * tint.B / 255, tint.A) : tint;
        face.SetPixelSize(_trueTypeSize);
        var line = face.Layout(text);
        foreach (var glyph in line.Glyphs)
        {
            if (GlyphTexture(face, glyph.Index) is not { } texture)
            {
                continue;
            }

            var x = glyph.X + glyph.Metrics.BearingX;
            var y = line.Baseline - glyph.Metrics.BearingY;
            batch.Draw(texture, position + new Vector2(x * scale, y * scale), null, color, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
        }
    }

    private Texture2D? GlyphTexture(TrueTypeFace face, int index)
    {
        if (_trueTypeGlyphs.TryGetValue(index, out var texture))
        {
            return texture;
        }

        face.SetPixelSize(_trueTypeSize);
        var bitmap = face.Rasterize(index);
        if (bitmap.Width > 0 && bitmap.Height > 0)
        {
            // White with the coverage as alpha, premultiplied as BlendState.AlphaBlend expects.
            var colors = new Color[bitmap.Pixels.Length];
            for (var i = 0; i < colors.Length; i++)
            {
                var a = bitmap.Pixels[i];
                colors[i] = new Color(a, a, a, a);
            }

            texture = new Texture2D(_device, bitmap.Width, bitmap.Height);
            texture.SetData(colors);
        }

        _trueTypeGlyphs[index] = texture;
        return texture;
    }

    /// <summary>Whether every character of the text is one the bitmap font draws; white space is always.</summary>
    private bool BitmapCanDraw(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                continue;
            }

            // Characters the code page has not come back as '?', which the font does have.
            var code = Cp437.FromUnicode(rune.Value);
            if (code > byte.MaxValue || Cp437.ToUnicode((byte)code) != rune.Value
                || !_font.TryGetGlyph(rune, out var sprite) || _font.Sheet[sprite] is not { Width: > 1, Height: > 1 })
            {
                return false;
            }
        }

        return true;
    }

    private int SizeFor(TrueTypeFace face, int minimumSize) => _match is { CapitalHeight: > 0 } match
        ? face.PixelSizeFor(match.CapitalHeight, match.SmallHeight, minimumSize)
        : Math.Max(14, minimumSize);

    private void DisposeTrueTypeGlyphs()
    {
        foreach (var texture in _trueTypeGlyphs.Values)
        {
            texture?.Dispose();
        }

        _trueTypeGlyphs.Clear();
    }
}
