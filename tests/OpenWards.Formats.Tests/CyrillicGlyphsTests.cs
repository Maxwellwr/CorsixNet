using System.Text;

namespace OpenWards.Formats.Tests;

public class CyrillicGlyphsTests
{
    [Fact]
    public void Added_glyphs_are_drawn_for_their_characters_and_the_code_page_s_question_mark_is_not_a_glyph()
    {
        var sprites = Enumerable.Range(0, 128 - BitmapFont.FirstCharacter).Select(_ => new IndexedImage(3, 3, new byte[9])).ToList();
        var font = new BitmapFont(SpriteSheet.FromImages(sprites));
        var zhe = new IndexedImage(5, 3, new byte[15]);

        var extended = font.WithGlyphs(new Dictionary<int, IndexedImage> { ['Ж'] = zhe });

        Assert.False(font.HasGlyph(new Rune('Ж')));
        Assert.True(extended.HasGlyph(new Rune('Ж')));
        Assert.True(extended.HasGlyph(new Rune('A')));
        Assert.Same(zhe, extended.Sheet[extended.Layout("Ж").Single().SpriteIndex]);
        Assert.Equal((5 + 3, 3), extended.MeasureLine("ЖA"));
    }

    [Fact]
    public void A_font_without_letters_gets_no_cyrillic()
    {
        var digits = Enumerable.Range(0, 128 - BitmapFont.FirstCharacter)
            .Select(i => i + BitmapFont.FirstCharacter is >= '0' and <= '9' ? new IndexedImage(3, 3, new byte[9]) : IndexedImage.Empty)
            .ToList();

        Assert.Empty(CyrillicGlyphs.Build(new BitmapFont(SpriteSheet.FromImages(digits))));
    }

    // The game's usual font: every Russian letter, standing on the Latin letters' baseline, И the mirror of N and
    // the letters Latin has (А, о) its own.
    [Fact]
    public void The_game_s_font_gets_every_russian_letter()
    {
        var install = RealGameData.Require();
        var font = new BitmapFont(install.LoadSpriteSheet("QDATA", "FONT01V", complex: false));

        var glyphs = CyrillicGlyphs.Build(font);

        Assert.All(CyrillicGlyphs.Russian, letter => Assert.True(glyphs.ContainsKey(letter), $"{letter} missing"));
        var n = Sprite(font, 'N');
        Assert.Equal(Bottom(n), Bottom(glyphs['Ж']));
        Assert.Equal(Bottom(n), Bottom(glyphs['я']));
        Assert.Equal(Top(Sprite(font, 'x')), Top(glyphs['н']));
        Assert.Equal(Mirror(n), Ink(glyphs['И']));
        Assert.Equal(Ink(Sprite(font, 'A')), Ink(glyphs['А']));
        Assert.Equal(Ink(Sprite(font, 'o')), Ink(glyphs['о']));
    }

    // Serif and hand-drawn fonts cannot be put together from plain stems and bars; they get nothing rather than some.
    [Theory]
    [InlineData("FONT43V")]
    [InlineData("FONT46V")]
    public void A_font_whose_letters_cannot_all_be_made_gets_none(string name)
    {
        var install = RealGameData.Require();

        Assert.Empty(CyrillicGlyphs.Build(new BitmapFont(install.LoadSpriteSheet("QDATA", name, complex: false))));
    }

    private static IndexedImage Sprite(BitmapFont font, char character)
    {
        Assert.True(font.TryGetGlyph(new Rune(character), out var sprite));
        return font.Sheet[sprite];
    }

    private static int Top(IndexedImage image) => Enumerable.Range(0, image.Height).First(y => Row(image, y).Any(p => p != Palette.TransparentIndex));

    private static int Bottom(IndexedImage image) => Enumerable.Range(0, image.Height).Last(y => Row(image, y).Any(p => p != Palette.TransparentIndex));

    private static IEnumerable<byte> Row(IndexedImage image, int y) => Enumerable.Range(0, image.Width).Select(x => image[x, y]);

    /// <summary>The inked columns, row by row, as text.</summary>
    private static string Ink(IndexedImage image)
    {
        var columns = Enumerable.Range(0, image.Width).Where(x => Enumerable.Range(0, image.Height).Any(y => image[x, y] != Palette.TransparentIndex)).ToList();
        return string.Join('\n', Enumerable.Range(0, image.Height).Select(y => string.Concat(columns.Select(x => image[x, y] == Palette.TransparentIndex ? '.' : (char)('A' + (image[x, y] % 26))))));
    }

    private static string Mirror(IndexedImage image) => string.Join('\n', Ink(image).Split('\n').Select(row => new string([.. row.Reverse()])));
}
