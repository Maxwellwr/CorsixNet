namespace CorsixNet.Formats.Tests;

public class PixelArtScalerTests
{
    private const byte Light = 1;
    private const byte Dark = 2;

    private static readonly Palette TestPalette = MakePalette();

    [Fact]
    public void DoublesTheSize()
    {
        var scaled = PixelArtScaler.Scale2x(Image(3, 2, Light), TestPalette);

        Assert.Equal((6, 4), (scaled.Width, scaled.Height));
        Assert.All(scaled.Pixels, pixel => Assert.Equal(Light, pixel));
    }

    [Fact]
    public void KeepsAxisAlignedRectanglesSharp()
    {
        // A dark 2x2 square in a light 6x6 image must stay a perfect 4x4 square.
        var image = Image(6, 6, Light);
        foreach (var (x, y) in new[] { (2, 2), (3, 2), (2, 3), (3, 3) })
        {
            image.Pixels[(y * 6) + x] = Dark;
        }

        var scaled = PixelArtScaler.Scale2x(image, TestPalette);

        Assert.Equal(NearestNeighbour(image), scaled.Pixels);
    }

    [Fact]
    public void SmoothsDiagonalStaircases()
    {
        // A thick 45 degree edge: everything below the diagonal is dark.
        var image = Image(8, 8, Light);
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                if (x <= y)
                {
                    image.Pixels[(y * 8) + x] = Dark;
                }
            }
        }

        var scaled = PixelArtScaler.Scale2x(image, TestPalette);

        Assert.NotEqual(NearestNeighbour(image), scaled.Pixels);
        Assert.All(scaled.Pixels, pixel => Assert.Contains(pixel, new[] { Light, Dark }));
    }

    [Fact]
    public void TwoPassesQuadrupleTheSize()
    {
        var scaled = PixelArtScaler.Scale(Image(3, 5, Dark), TestPalette, passes: 2);

        Assert.Equal((12, 20), (scaled.Width, scaled.Height));
    }

    [Fact]
    public void ZeroPassesReturnTheImage()
    {
        var image = Image(2, 2, Dark);

        Assert.Same(image, PixelArtScaler.Scale(image, TestPalette, passes: 0));
    }

    [Fact]
    public void LeavesEmptyImagesEmpty()
    {
        Assert.True(PixelArtScaler.Scale2x(IndexedImage.Empty, TestPalette).IsEmpty);
    }

    [Fact]
    public void TreatsIndicesWithTheSameColourAsOne()
    {
        // Index 3 has the same colour as index 2; the output uses the lower index.
        var image = Image(2, 1, Dark);
        image.Pixels[1] = 3;

        var scaled = PixelArtScaler.Scale2x(image, TestPalette);

        Assert.All(scaled.Pixels, pixel => Assert.Equal(Dark, pixel));
    }

    [Fact]
    public void ScalesEveryGameSpriteWithoutError()
    {
        var install = RealGameData.Require();
        var palette = install.LoadPalette("DATA", "MPALETTE.DAT");
        var sheet = install.LoadSpriteSheet("DATA", "VBLK-0", complex: false);

        for (var i = 0; i < sheet.Count; i++)
        {
            var scaled = PixelArtScaler.Scale2x(sheet[i], palette);
            Assert.Equal((sheet[i].Width * 2, sheet[i].Height * 2), (scaled.Width, scaled.Height));
        }
    }

    [Fact]
    public void ScaledFloorTilesLeaveNoNewGaps()
    {
        var tile = Diamond();

        var scaled = PixelArtScaler.ScaleTile(tile, TestPalette, passes: 1);

        Assert.Equal((128, 64), (scaled.Width, scaled.Height));
        Assert.Equal(NearestNeighbour(Coverage(tile, factor: 1)), Coverage(scaled, factor: 2).Pixels);
    }

    [Fact]
    public void ScaledGameFloorTilesLeaveNoNewGaps()
    {
        var install = RealGameData.Require();
        var palette = install.LoadPalette("DATA", "MPALETTE.DAT");
        var floor = install.LoadSpriteSheet("DATA", "VBLK-0", complex: false)[1];

        var scaled = PixelArtScaler.ScaleTile(floor, palette, passes: 2);

        Assert.Equal(NearestNeighbour(Coverage(floor, 1, palette), factor: 4), Coverage(scaled, 4, palette).Pixels);
    }

    /// <summary>
    /// Lays copies of a tile along the isometric lattice and returns which pixels of the central
    /// region are covered (1) or left open (0).
    /// </summary>
    private static IndexedImage Coverage(IndexedImage tile, int factor, Palette? palette = null)
    {
        palette ??= TestPalette;
        int stepX = 32 * factor, stepY = 16 * factor;
        int width = tile.Width, height = tile.Height;
        var covered = new byte[width * height];
        for (var a = -2; a <= 2; a++)
        {
            for (var b = -2; b <= 2; b++)
            {
                int ox = (stepX * a) + (stepX * b), oy = (stepY * a) - (stepY * b);
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        int sx = x - ox, sy = y - oy;
                        if (sx >= 0 && sx < width && sy >= 0 && sy < height && !palette[tile[sx, sy]].IsTransparent)
                        {
                            covered[(y * width) + x] = 1;
                        }
                    }
                }
            }
        }

        return new IndexedImage(width, height, covered);
    }

    /// <summary>A 64x32 floor diamond shaped like the game's, with a dark stripe inside.</summary>
    private static IndexedImage Diamond()
    {
        var pixels = Enumerable.Repeat(Palette.TransparentIndex, 64 * 32).ToArray();
        for (var y = 0; y < 32; y++)
        {
            var row = y < 16 ? y : 31 - y;
            for (var x = 30 - (2 * row); x <= 33 + (2 * row); x++)
            {
                pixels[(y * 64) + x] = (x + y) % 9 == 0 ? Dark : Light;
            }
        }

        return new IndexedImage(64, 32, pixels);
    }

    private static IndexedImage Image(int width, int height, byte fill)
    {
        return new IndexedImage(width, height, Enumerable.Repeat(fill, width * height).ToArray());
    }

    /// <summary>Enlarges an image pixel for pixel.</summary>
    private static byte[] NearestNeighbour(IndexedImage image, int factor = 2)
    {
        var output = new byte[image.Pixels.Length * factor * factor];
        for (var y = 0; y < image.Height * factor; y++)
        {
            for (var x = 0; x < image.Width * factor; x++)
            {
                output[(y * image.Width * factor) + x] = image[x / factor, y / factor];
            }
        }

        return output;
    }

    private static Palette MakePalette()
    {
        var data = new byte[768];
        data[3] = data[4] = data[5] = 60;  // 1: light
        data[6] = data[7] = data[8] = 10;  // 2: dark
        data[9] = data[10] = data[11] = 10; // 3: same as 2
        for (var i = 4; i < 256; i++)
        {
            data[i * 3] = (byte)(i % 64);
            data[(i * 3) + 1] = (byte)((i / 4) % 64);
            data[(i * 3) + 2] = 33;
        }

        return Palette.Parse(data);
    }
}
