using CorsixNet.Client.Ui.Windows;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class GraphsScreenTests
{
    [Fact]
    public void ValuesGoBetweenTheBottomAndTheTop()
    {
        Assert.Equal(353, GraphsScreen.Y(0, 0, 100));
        Assert.Equal(85, GraphsScreen.Y(100, 0, 100));
        Assert.Equal(219, GraphsScreen.Y(50, 0, 100));
        Assert.Equal(353, GraphsScreen.Y(5, 5, 5));
    }

    [Fact]
    public void LabelsDoNotOverlap()
    {
        var placed = GraphsScreen.PlaceLabels([85, 85, 200, 353], 12);
        Assert.Equal([85, 97, 194, 341], placed);
    }

    [Fact]
    public void WithoutTheGraphicsTheGraphsAreAWindow() => Assert.False(new GraphWindow().IsFullscreen(new WindowTestHost()));
}
