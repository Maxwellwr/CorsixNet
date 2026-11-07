using CorsixNet.Formats;

namespace CorsixNet.Content.Tests;

public class MapFileTests
{
    [Fact]
    public void ParsesTilesCamerasAndHeliports()
    {
        var data = new MapFileBuilder(playerCount: 2)
            .Tile(3, 5, floor: 7, northWall: 90, westWall: 91, flags: 0x21, extraFlags: 0x10, objectType: 4, objectDirection: 2, parcel: 6)
            .Camera(0, 63, 64)
            .Camera(1, 10, 20)
            .Heliport(1, 127, 1)
            .Build();

        var map = MapFile.Parse(data);

        Assert.Equal(2, map.PlayerCount);
        Assert.Equal(new MapFileTile(2, 4, 7, 90, 91, 0x21, 0, 0x10, 6), map[3, 5]);
        Assert.Equal([(63, 64), (10, 20)], map.CameraTiles);
        Assert.Equal((127, 1), map.HeliportTiles[1]);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(9, MapFile.MaxPlayers)]
    public void ClampsPlayerCount(byte stored, int expected)
    {
        var data = new MapFileBuilder().Build();
        data[0] = stored;

        Assert.Equal(expected, MapFile.Parse(data).PlayerCount);
    }

    [Fact]
    public void RejectsShortData()
    {
        Assert.Throws<InvalidDataException>(() => MapFile.Parse(new byte[MapFile.MinimumLength - 1]));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(80, 0x52)]
    [InlineData(140, 0x8E)]
    [InlineData(255, 0)]
    public void MapsBlockCodesToSprites(byte code, byte sprite)
    {
        Assert.Equal(sprite, MapFile.BlockSprite(code));
    }
}
