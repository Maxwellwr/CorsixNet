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
    public void SpriteTableHasWholeEntries()
    {
        var install = RealGameData.Require();

        // Sprite tables are arrays of 6-byte entries: 32-bit offset, width, height.
        var table = install.ReadAllBytes("DATA", "VBLK-0.TAB");

        Assert.NotEmpty(table);
        Assert.Equal(0, table.Length % 6);
    }
}
