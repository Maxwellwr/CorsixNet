using System.Text;

namespace CorsixNet.Formats.Tests;

public class BitmapFontTests
{
    [Theory]
    [InlineData('A', 'A')]
    [InlineData('é', 0x82)]
    [InlineData('ü', 0x81)]
    [InlineData('£', 0x9C)]
    [InlineData('°', 0xF8)]
    [InlineData('Æ', '?')]
    [InlineData('€', '?')]
    public void MapsUnicodeToFontCodes(char character, int expected)
    {
        Assert.Equal(expected, Cp437.FromUnicode(character));
    }

    [Fact]
    public void RoundTripsMappedCharacters()
    {
        for (var code = 0x80; code <= 0xFF; code++)
        {
            var unicode = Cp437.ToUnicode((byte)code);
            if (unicode != '?')
            {
                Assert.Equal(code, Cp437.FromUnicode(unicode));
            }
        }
    }

    [Fact]
    public void LaysOutGlyphsWithSpacing()
    {
        // Sprites for codes 31, 32 (' '), 33 ('!'), 34 ('"').
        var font = new BitmapFont(MakeSheet((1, 1), (3, 5), (2, 7), (4, 6)), letterSpacing: 1);

        Assert.True(font.TryGetGlyph(new Rune('!'), out var bang));
        Assert.Equal(2, bang);
        Assert.Equal([(1, 0), (2, 4), (3, 7)], font.Layout(" !\""));
        Assert.Equal((12, 7), font.MeasureLine(" !\""));
    }

    [Fact]
    public void SkipsCharactersOutsideTheFont()
    {
        var font = new BitmapFont(MakeSheet((1, 1), (3, 5)));

        Assert.False(font.TryGetGlyph(new Rune('A'), out _));
        Assert.Equal((6, 5), font.MeasureLine("A A\n "));
    }

    [Fact]
    public void MeasuresGameFont()
    {
        var install = RealGameData.Require();
        var font = new BitmapFont(install.LoadSpriteSheet("QDATA", "FONT01V", complex: false));

        var (width, height) = font.MeasureLine("Hello World");

        Assert.InRange(width, 40, 120);
        Assert.InRange(height, 8, 16);
    }

    private static SpriteSheet MakeSheet(params (int Width, int Height)[] sizes)
    {
        var table = new List<byte>();
        foreach (var (width, height) in sizes)
        {
            table.AddRange([0, 0, 0, 0, (byte)width, (byte)height]);
        }

        return SpriteSheet.Parse(table.ToArray(), [], complex: false);
    }
}
