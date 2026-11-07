namespace CorsixNet.Formats.Tests;

public class SpriteSheetTests
{
    private const byte T = Palette.TransparentIndex;

    [Fact]
    public void DecodesSimpleChunks()
    {
        // 3 literals, skip 2 (0xFE), 1 literal, then fill the rest of the row.
        byte[] chunks = [3, 10, 11, 12, 0xFE, 1, 13, 0];

        var sprite = DecodeSingle(chunks, width: 3, height: 3, complex: false);

        Assert.Equal([10, 11, 12, T, T, 13, T, T, T], sprite.Pixels);
    }

    [Fact]
    public void EndOfLineAfterFullRowDoesNotSkipNextRow()
    {
        byte[] chunks = [2, 1, 2, 0, 2, 3, 4];

        var sprite = DecodeSingle(chunks, width: 2, height: 2, complex: false);

        Assert.Equal([1, 2, 3, 4], sprite.Pixels);
    }

    [Fact]
    public void FillsMissingDataWithTransparency()
    {
        var sprite = DecodeSingle([1, 7], width: 2, height: 2, complex: false);

        Assert.Equal([7, T, T, T], sprite.Pixels);
    }

    [Fact]
    public void IgnoresDataBeyondTheImage()
    {
        var sprite = DecodeSingle([4, 1, 2, 3, 4], width: 1, height: 2, complex: false);

        Assert.Equal([1, 2], sprite.Pixels);
    }

    [Fact]
    public void DecodesComplexChunks()
    {
        byte[] chunks =
        [
            2, 5, 6,        // 2 literals
            0x82,           // skip 2
            0x40, 9,        // run of 4 x colour 9 (0x40 - 60)
            0xFF, 3, 7,     // run of 3 x colour 7
            0,              // fill the rest of the row
        ];

        var sprite = DecodeSingle(chunks, width: 8, height: 3, complex: true);

        Assert.Equal(
            [
                5, 6, T, T, 9, 9, 9, 9,
                7, 7, 7, T, T, T, T, T,
                T, T, T, T, T, T, T, T,
            ],
            sprite.Pixels);
    }

    [Fact]
    public void DecodesLongComplexRuns()
    {
        // 0xC0 is a run of 0xC0 - 60 - 64 = 68 pixels.
        byte[] chunks = [0xC0, 8, 2, 1, 2];

        var sprite = DecodeSingle(chunks, width: 70, height: 1, complex: true);

        Assert.Equal([.. Enumerable.Repeat((byte)8, 68), 1, 2], sprite.Pixels);
    }

    [Fact]
    public void ParsesTableEntries()
    {
        byte[] table =
        [
            0, 0, 0, 0, 0, 0,   // empty sprite
            0, 0, 0, 0, 2, 1,   // 2x1 at offset 0
            3, 0, 0, 0, 1, 1,   // 1x1 at offset 3
            9, 9,               // trailing partial entry
        ];
        byte[] data = [2, 4, 5, 1, 6];

        var sheet = SpriteSheet.Parse(table, data, complex: false);

        Assert.Equal(3, sheet.Count);
        Assert.True(sheet[0].IsEmpty);
        Assert.Equal([4, 5], sheet[1].Pixels);
        Assert.Equal([6], sheet[2].Pixels);
    }

    [Fact]
    public void OffsetPastEndGivesTransparentSprite()
    {
        byte[] table = [100, 0, 0, 0, 2, 1];

        var sheet = SpriteSheet.Parse(table, [1, 2], complex: false);

        Assert.Equal([T, T], sheet[0].Pixels);
    }

    [Fact]
    public void LoadsGameSpriteSheets()
    {
        var install = RealGameData.Require();

        var blocks = install.LoadSpriteSheet("DATA", "VBLK-0", complex: false);
        var panel = install.LoadSpriteSheet("DATA", "PANEL02V", complex: true);

        // Sprite 1 of the map blocks is a floor tile.
        Assert.Equal((64, 32), (blocks[1].Width, blocks[1].Height));
        Assert.Contains(Enumerable.Range(0, panel.Count), i => !panel[i].IsEmpty);
    }

    private static IndexedImage DecodeSingle(byte[] chunks, int width, int height, bool complex)
    {
        byte[] table = [0, 0, 0, 0, (byte)width, (byte)height];
        return SpriteSheet.Parse(table, chunks, complex)[0];
    }
}
