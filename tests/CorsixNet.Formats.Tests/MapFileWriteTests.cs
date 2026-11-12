using CorsixNet.Formats;

namespace CorsixNet.Formats.Tests;

/// <summary>Writing map files (<c>level_map::save</c>).</summary>
public sealed class MapFileWriteTests
{
    [Fact]
    public void WrittenBytesParseBack()
    {
        var tiles = new MapFileTile[MapFile.Size * MapFile.Size];
        tiles[(5 * MapFile.Size) + 7] = new MapFileTile(2, 58, 0x10, 0x20, 0x21, 0x3E, 0, 16, 513);
        var file = MapFile.Create(2, tiles, [(10, 11), (100, 90)], [(1, 2), (127, 127)]);

        var data = file.ToBytes();
        var parsed = MapFile.Parse(data);

        Assert.Equal(MapFile.MinimumLength, data.Length);
        Assert.Equal(2, data[0]);
        Assert.Equal(3, data[131106 + (2 * MapFile.Size * MapFile.Size)]);
        Assert.Equal(2, parsed.PlayerCount);
        Assert.Equal(tiles[(5 * MapFile.Size) + 7], parsed[7, 5]);
        Assert.Equal([(10, 11), (100, 90)], parsed.CameraTiles);
        Assert.Equal([(1, 2), (127, 127)], parsed.HeliportTiles);
    }

    [Fact]
    public void BlockCodesGiveBackTheirSprites()
    {
        Assert.Equal(0, MapFile.BlockCode(0));
        for (var code = 1; code < 256; code++)
        {
            var sprite = MapFile.BlockSprite((byte)code);
            if (sprite != 0)
            {
                Assert.Equal(sprite, MapFile.BlockSprite(MapFile.BlockCode(sprite)));
            }
        }
    }

    [Fact]
    public void RejectsTheWrongNumberOfTilesOrPlayers()
    {
        Assert.Throws<ArgumentException>(() => MapFile.Create(1, new MapFileTile[10], [(0, 0)], [(0, 0)]));
        Assert.Throws<ArgumentException>(() => MapFile.Create(1, new MapFileTile[MapFile.Size * MapFile.Size], [(0, 0), (1, 1)], [(0, 0)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapFile.Create(5, new MapFileTile[MapFile.Size * MapFile.Size], [], []));
    }
}
