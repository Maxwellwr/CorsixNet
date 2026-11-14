using CorsixNet.Client.Ui.Windows;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class MachineScreenTests
{
    [Fact]
    public void TheStatusBarShowsTheUsesLeft()
    {
        Assert.Equal(40, MachineScreen.StatusWidth(0, 7));
        Assert.Equal(20, MachineScreen.StatusWidth(5, 10));
        Assert.Equal(0, MachineScreen.StatusWidth(12, 10));
        Assert.Equal(0, MachineScreen.StatusWidth(0, 0));
    }

    [Fact]
    public void WithoutTheGraphicsTheMachineWindowIsATable() => Assert.False(new MachineWindow(1).IsFullscreen(new WindowTestHost()));
}
