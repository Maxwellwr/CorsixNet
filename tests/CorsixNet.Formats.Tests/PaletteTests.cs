namespace CorsixNet.Formats.Tests;

public class PaletteTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 4)]
    [InlineData(32, 130)]
    [InlineData(63, 255)]
    public void ExpandsSixBitComponentsWithRounding(byte component, byte expected)
    {
        Assert.Equal(expected, Palette.Expand6Bit(component));
    }

    [Fact]
    public void ParsesSixBitRgb()
    {
        var data = new byte[768];
        data[3] = 63;
        data[4] = 32;
        data[5] = 1;

        var palette = Palette.Parse(data);

        Assert.Equal(new PaletteColor(0, 0, 0, 255), palette[0]);
        Assert.Equal(new PaletteColor(255, 130, 4, 255), palette[1]);
    }

    [Fact]
    public void MakesMagentaAndLastEntryTransparent()
    {
        var data = new byte[768];
        data[6] = 63;
        data[8] = 63;
        data[765] = 10;

        var palette = Palette.Parse(data);

        Assert.True(palette[2].IsTransparent);
        Assert.True(palette[Palette.TransparentIndex].IsTransparent);
    }

    [Fact]
    public void CanKeepLastEntryOpaque()
    {
        var data = new byte[768];
        data[765] = 10;

        var palette = Palette.Parse(data, transparentLastEntry: false);

        Assert.Equal(new PaletteColor(40, 0, 0, 255), palette[255]);
    }

    [Fact]
    public void ParsesEightBitRgba()
    {
        var data = new byte[1024];
        data[4] = 200;
        data[5] = 100;
        data[6] = 50;
        data[7] = 128;

        var palette = Palette.Parse(data, eightBit: true);

        Assert.Equal(new PaletteColor(200, 100, 50, 128), palette[1]);
    }

    [Fact]
    public void RejectsUnexpectedLength()
    {
        Assert.Throws<InvalidDataException>(() => Palette.Parse(new byte[767]));
    }

    [Fact]
    public void LoadsGamePalette()
    {
        var install = RealGameData.Require();

        var palette = install.LoadPalette("DATA", "MPALETTE.DAT");

        Assert.Equal(new PaletteColor(0, 0, 0, 255), palette[0]);
        Assert.True(palette[255].IsTransparent);
    }
}
