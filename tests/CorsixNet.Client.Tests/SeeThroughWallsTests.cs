using CorsixNet.Client.Maps;
using CorsixNet.Sim.Maps;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class SeeThroughWallsTests
{
    [Fact]
    public void WallsJustInFrontOfThePlaceHideIt()
    {
        var target = new TilePosition(10, 10);
        Assert.True(MapRenderer.HidesTile(11, 10, target));
        Assert.True(MapRenderer.HidesTile(10, 11, target));
        Assert.True(MapRenderer.HidesTile(11, 12, target));

        // Its own walls and those behind it or far in front do not.
        Assert.False(MapRenderer.HidesTile(10, 10, target));
        Assert.False(MapRenderer.HidesTile(9, 10, target));
        Assert.False(MapRenderer.HidesTile(10, 9, target));
        Assert.False(MapRenderer.HidesTile(12, 12, target));
    }
}
