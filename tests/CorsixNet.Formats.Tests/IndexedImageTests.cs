namespace CorsixNet.Formats.Tests;

public class IndexedImageTests
{
    [Fact]
    public void FromRawIgnoresTrailingBytes()
    {
        var image = IndexedImage.FromRaw([1, 2, 3, 4, 5], width: 2, height: 2);

        Assert.Equal([1, 2, 3, 4], image.Pixels);
        Assert.Equal(3, image[0, 1]);
    }

    [Fact]
    public void FromRawRejectsShortData()
    {
        Assert.Throws<InvalidDataException>(() => IndexedImage.FromRaw([1, 2, 3], width: 2, height: 2));
    }

    [Fact]
    public void ConvertsToColors()
    {
        var palette = Palette.Parse(new byte[768]);
        var image = new IndexedImage(2, 1, [0, Palette.TransparentIndex]);

        var colors = image.ToColors(palette);

        Assert.Equal([new PaletteColor(0, 0, 0, 255), PaletteColor.Transparent], colors);
    }

    [Fact]
    public void LoadsGameBackground()
    {
        var install = RealGameData.Require();

        var background = install.LoadRawBitmap("QDATA", "LOAD01V");

        Assert.Equal((640, 480), (background.Width, background.Height));
    }

    [Fact]
    public void LoadsTheStaffFaceParts()
    {
        var install = RealGameData.Require();

        // hire_staff.lua's loadRaw("Face01V", 65, 1350): 18 hairs, faces and chins under each other.
        var parts = install.LoadRawBitmap("QDATA", "FACE01V", 65, 1350);

        Assert.Equal((65, 1350), (parts.Width, parts.Height));
        Assert.Contains(parts.Pixels.ToArray(), pixel => pixel != 0);
    }
}
