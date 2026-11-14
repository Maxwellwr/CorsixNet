using CorsixNet.Client.Ui.Windows;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class WatchScreenTests
{
    [Fact]
    public void TheFaceFillsRedAsTheTimerRunsDown()
    {
        Assert.Equal((true, (int?)null), WatchScreen.Face(12));
        Assert.Equal((true, (int?)2), WatchScreen.Face(11));
        Assert.Equal((true, (int?)7), WatchScreen.Face(6));
        Assert.Equal((false, (int?)8), WatchScreen.Face(5));
        Assert.Equal((false, (int?)12), WatchScreen.Face(1));
        Assert.Equal((false, (int?)null), WatchScreen.Face(0));
    }

    [Fact]
    public void WithoutTheGraphicsTheWatchIsATable() => Assert.False(new WatchWindow().IsFullscreen(new WindowTestHost()));
}
