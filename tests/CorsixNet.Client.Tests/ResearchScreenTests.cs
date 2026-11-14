using CorsixNet.Client.Ui.Windows;
using Microsoft.Xna.Framework;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class ResearchScreenTests
{
    [Fact]
    public void TheArrowsAndTheAnimationsAreWhereCorsixThHasThem()
    {
        Assert.Equal(new Rectangle(372, 62, 40, 40), ResearchScreen.Arrow(1, -1));
        Assert.Equal(new Rectangle(450, 226, 40, 40), ResearchScreen.Arrow(5, 1));
        Assert.Equal((5, 13), ResearchScreen.Animation(0));
        Assert.Equal((6, 14), ResearchScreen.Animation(4));
        Assert.Equal((5, 13), ResearchScreen.Animation(32));
    }

    [Fact]
    public void WithoutTheGraphicsResearchIsATable() => Assert.False(new ResearchWindow().IsFullscreen(new WindowTestHost()));
}
