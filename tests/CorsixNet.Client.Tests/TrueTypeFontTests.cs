using CorsixNet.Client.Rendering;
using CorsixNet.Formats;

namespace CorsixNet.Client.Tests;

public sealed class TrueTypeFontTests
{
    [Fact]
    public void A_file_that_is_not_a_font_is_not_loaded()
    {
        Assert.Null(TrueTypeFace.Load([]));
        Assert.Null(TrueTypeFace.Load("not a font, just some text"u8.ToArray()));
    }

    [Fact]
    public void The_face_knows_which_characters_it_has()
    {
        using var face = TrueTypeFace.Load(SyntheticFont.Latin("Mgя"))!;

        Assert.True(face.HasGlyph('я'));
        Assert.False(face.HasGlyph('ж'));

        // White space is not counted; each missing character once.
        Assert.Equal(0, face.MissingGlyphs("Mg я"));
        Assert.Equal(2, face.MissingGlyphs("жжMщ"));
    }

    // Capitals 700 units and small letters 500 units tall of 1000, small letters 500 wide on average.
    [Theory]
    [InlineData(7, 0, 0, 8, 10)]
    [InlineData(7, 6, 0, 8, 12)]
    [InlineData(7, 6, 5.0, 8, 9)]
    [InlineData(7, 6, 0, 14, 14)]
    [InlineData(0, 0, 0, 8, 14)]
    public void The_size_matches_the_bitmap_font_s_letters_without_widening_the_text(int capitals, int small, double width, int minimum, int expected)
    {
        using var face = TrueTypeFace.Load(SyntheticFont.Build(
            [new('H', 600, 50, 0, 550, 700), new('x', 500, 50, 0, 450, 500), .. "abcdefghijklmnopqrstuvwyz".Select(c => new SyntheticFont.Glyph(c, 500, 50, 0, 450, 500))]))!;

        Assert.Equal(expected, face.PixelSizeFor(capitals, small, width, minimum));
    }

    [Fact]
    public void A_size_keeps_the_face_s_proportions()
    {
        using var face = TrueTypeFace.Load(SyntheticFont.Latin("M"))!;

        face.SetPixelSize(12);

        Assert.Equal(12, face.PixelSize);
    }

    // At 14 pixels to the em of 1000 units: 'M' advances 9.8 pixels, 10 above the baseline; 'g' 8.4, 7 above and 3
    // below. Advances are whole pixels (FreeType's hinting may round 'g' either way); a line is one pixel more than its
    // glyphs reach.
    [Fact]
    public void A_line_is_laid_out_with_whole_pixel_advances_on_one_baseline()
    {
        using var face = TrueTypeFace.Load(SyntheticFont.Latin("Mg"))!;
        face.SetPixelSize(14);

        var line = face.Layout("MgM");

        var m = line.Glyphs[0].Metrics;
        var g = line.Glyphs[1].Metrics;
        Assert.Equal(10, m.Advance);
        Assert.InRange(g.Advance, 8, 9);
        Assert.Equal([0, m.Advance, m.Advance + g.Advance], line.Glyphs.Select(glyph => glyph.X));
        Assert.Equal(10, line.Baseline);
        Assert.Equal(1 + 10 + 3, line.Height);
        Assert.Equal(1 + line.Glyphs[2].X + m.BearingX + m.Width, line.Width);
        Assert.Equal((0, 0), (face.Layout(string.Empty).Width, face.Layout(string.Empty).Height));
    }

    // 'M' fills 0.7 to 9.1 pixels across and 0 to 9.8 up: whole rows and columns are covered, the edges partly or, one
    // bit a pixel, not at all.
    [Fact]
    public void A_glyph_is_rasterised_as_coverage()
    {
        using var face = TrueTypeFace.Load(SyntheticFont.Latin("M "))!;
        face.SetPixelSize(14);
        var line = face.Layout("M ");

        var bitmap = face.Rasterize(line.Glyphs[0].Index);

        Assert.InRange(bitmap.Width, 8, 10);
        Assert.Equal(10, bitmap.Height);
        Assert.Equal(255, bitmap.Pixels[(5 * bitmap.Width) + 5]);
        Assert.All(face.Rasterize(line.Glyphs[1].Index).Pixels, coverage => Assert.Equal(0, coverage));
    }

    [Fact]
    public void Lines_the_bitmap_font_can_draw_keep_it()
    {
        var sprites = Enumerable.Range(0, 128 - BitmapFont.FirstCharacter).Select(_ => new IndexedImage(4, 4, new byte[16])).ToList();
        var font = new BitmapFont(SpriteSheet.FromImages(sprites));

        Assert.True(TextRenderer.BitmapCanDraw(font, "Previous tip 40000"));

        // Cyrillic is not in the code page, which would make it '?'.
        Assert.False(TextRenderer.BitmapCanDraw(font, "1 Янв"));
    }

    // match_bitmap_font: the size of 'M' (sprite 'M' - 31) and the colour scoring most, black and white pixels
    // counting four times as much as mid greys.
    [Fact]
    public void The_bitmap_font_gives_the_size_and_colour_of_its_M()
    {
        var palette = TestPalette();
        var sprites = Enumerable.Repeat(IndexedImage.Empty, 'M' - BitmapFont.FirstCharacter).ToList();
        byte[] pixels = [1, 1, 1, 2, 2, Palette.TransparentIndex, Palette.TransparentIndex, Palette.TransparentIndex];
        sprites.Add(new IndexedImage(4, 2, pixels));

        var match = BitmapFontMatch.Find(SpriteSheet.FromImages(sprites), palette);

        Assert.Equal(new BitmapFontMatch(4, 2, palette[2]) { CapitalHeight = 2, Baseline = 2 }, match);
    }

    [Fact]
    public void A_font_of_digits_takes_its_zero_and_failing_that_the_average()
    {
        var palette = TestPalette();
        var sprites = Enumerable.Repeat(IndexedImage.Empty, '0' - BitmapFont.FirstCharacter).ToList();
        sprites.Add(new IndexedImage(3, 5, [.. Enumerable.Repeat((byte)2, 15)]));
        Assert.Equal(new BitmapFontMatch(3, 5, palette[2]) { CapitalHeight = 5, Baseline = 5 }, BitmapFontMatch.Find(SpriteSheet.FromImages(sprites), palette));

        var others = new[] { new IndexedImage(4, 6, [.. Enumerable.Repeat((byte)2, 24)]), new IndexedImage(6, 8, [.. Enumerable.Repeat((byte)2, 48)]) };
        Assert.Equal(new BitmapFontMatch(5, 7, palette[2]), BitmapFontMatch.Find(SpriteSheet.FromImages(others), palette));
        Assert.Null(BitmapFontMatch.Find(SpriteSheet.FromImages([IndexedImage.Empty]), palette));
    }

    [Fact]
    public void The_configured_font_comes_first_then_those_beside_the_game()
    {
        var files = new Dictionary<string, byte[]>
        {
            ["/my/font.ttf"] = SyntheticFont.Latin("M"),
            [Path.Combine("/game", "GoNotoKurrent-Regular.ttf")] = SyntheticFont.Latin("Mя"),
            ["/system/cyrillic.ttf"] = SyntheticFont.Latin("Mя"),
        };

        using (var fonts = Fonts(files, "/my/font.ttf"))
        {
            // The configured font is taken for any language, as CorsixTH's single face is.
            Assert.Equal("/my/font.ttf", fonts.Find("я")?.Path);
            Assert.False(fonts.CanDraw("я"));
        }

        using (var fonts = Fonts(files, GameConfigPlaceholder))
        {
            Assert.Equal(Path.Combine("/game", "GoNotoKurrent-Regular.ttf"), fonts.Find("я")?.Path);
        }
    }

    [Fact]
    public void A_system_font_is_taken_that_has_the_language_s_characters()
    {
        var files = new Dictionary<string, byte[]>
        {
            ["/system/latin.ttf"] = SyntheticFont.Latin("M"),
            ["/system/cyrillic.ttf"] = SyntheticFont.Latin("Mя"),
            ["/system/broken.ttf"] = [1, 2, 3],
        };

        using var fonts = Fonts(files, null);

        Assert.Equal("/system/cyrillic.ttf", fonts.Find("Mя")?.Path);
        Assert.True(fonts.CanDraw("я"));
        Assert.Equal("/system/latin.ttf", fonts.Find("M")?.Path);

        // None has every character: the one missing the fewest.
        Assert.Equal("/system/cyrillic.ttf", fonts.Find("яж")?.Path);
        Assert.False(fonts.CanDraw("яж"));
    }

    [Fact]
    public void Without_any_font_nothing_is_found()
    {
        using var fonts = Fonts([], null);

        Assert.Null(fonts.Find("я"));
        Assert.False(fonts.CanDraw("я"));
    }

    private const string GameConfigPlaceholder = @"X:\ThemeHospital\font.ttc";

    private static UnicodeFonts Fonts(Dictionary<string, byte[]> files, string? configured) =>
        new(configured, "/game", ["/system/missing.ttf", "/system/broken.ttf", "/system/latin.ttf", "/system/cyrillic.ttf"], files.ContainsKey, path => files[path]);

    private static Palette TestPalette()
    {
        var data = new byte[Palette.Size * 3];

        // 1: mid grey, 2: white (6-bit values).
        data[3] = data[4] = data[5] = 32;
        data[6] = data[7] = data[8] = 63;
        return Palette.Parse(data);
    }
}
