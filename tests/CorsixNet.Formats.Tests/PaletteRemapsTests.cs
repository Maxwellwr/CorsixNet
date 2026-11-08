namespace CorsixNet.Formats.Tests;

public class PaletteRemapsTests
{
    [Fact]
    public void SplitsTablesAndIgnoresATrailingPart()
    {
        var data = new byte[(2 * PaletteRemaps.TableSize) + 10];
        data[PaletteRemaps.TableSize + 3] = 42;

        var remaps = PaletteRemaps.Parse(data);

        Assert.Equal(2, remaps.Count);
        Assert.Equal(42, remaps[1][3]);
        Assert.Equal(PaletteRemaps.TableSize, remaps[0].Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => remaps[2].Length);
    }

    [Fact]
    public void RemapsImagesButKeepsTransparency()
    {
        var table = new byte[Palette.Size];
        for (var i = 0; i < table.Length; i++)
        {
            table[i] = (byte)(255 - i);
        }

        var image = new IndexedImage(3, 1, [0, 10, Palette.TransparentIndex]);

        var remapped = image.Remap(table);

        Assert.Equal([255, 245, Palette.TransparentIndex], remapped.Pixels);
        Assert.Equal([0, 10, Palette.TransparentIndex], image.Pixels);
        Assert.Throws<ArgumentException>(() => image.Remap(new byte[3]));
    }

    [Fact]
    public void BuildsASheetFromImages()
    {
        var sheet = SpriteSheet.FromImages([IndexedImage.Empty, new IndexedImage(1, 1, [7])]);

        Assert.Equal(2, sheet.Count);
        Assert.True(sheet[0].IsEmpty);
        Assert.Equal(7, sheet[1][0, 0]);
    }

    [Fact]
    public void TurnsTheBlueprintRedWithGhost1Table6()
    {
        var install = RealGameData.Require();

        var remaps = PaletteRemaps.Parse(install.ReadDataFile("QDATA", "GHOST1.DAT"));
        var wall = install.LoadSpriteSheet("DATA", "VSPR-0", complex: false)[1482];
        var palette = install.LoadPalette("DATA", "MPALETTE.DAT");

        Assert.Equal(256, remaps.Count);
        var colors = wall.ToColors(palette).Where(c => !c.IsTransparent).ToArray();
        var red = wall.Remap(remaps[6]).ToColors(palette).Where(c => !c.IsTransparent).ToArray();
        Assert.True(colors.Count(c => c.B > c.R) > colors.Length / 2, "The blueprint wall should be mostly blue.");
        Assert.True(red.Count(c => c.R > c.B) > red.Length / 2, "The remapped wall should be mostly red.");
    }
}
