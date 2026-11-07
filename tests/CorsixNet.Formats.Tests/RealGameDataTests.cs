namespace CorsixNet.Formats.Tests;

public class RealGameDataTests
{
    [Fact]
    public void LocatesFullGame()
    {
        var install = RealGameData.Require();

        Assert.False(install.IsDemo, "The tests expect the full game, not the demo.");
    }

    [Fact]
    public void ReadDataFileDecompressesRncFiles()
    {
        var install = RealGameData.Require();

        Assert.True(Rnc.IsCompressed(install.ReadAllBytes("DATA", "VBLK-0.TAB")));
        var table = install.ReadDataFile("DATA", "VBLK-0.TAB");

        Assert.False(Rnc.IsCompressed(table));
        Assert.Equal(0, table.Length % SpriteSheet.TableEntrySize);
    }
}
