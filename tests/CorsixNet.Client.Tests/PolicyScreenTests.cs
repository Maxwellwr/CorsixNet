using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim.Economy;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class PolicyScreenTests
{
    [Theory]
    [InlineData(PolicyKind.SendHome, 100, 129 + 29)]
    [InlineData(PolicyKind.GuessCure, 900, 129 + 269)]
    [InlineData(PolicyKind.StopProcedure, 1000, 124)]
    [InlineData(PolicyKind.StopProcedure, 2000, 124 + 299)]
    [InlineData(PolicyKind.GoToStaffRoom, 600, 149 + 150)]
    public void SlidersStandWhereCorsixThPutsThemAndGiveTheirValueBack(PolicyKind policy, int value, int x)
    {
        Assert.Equal(x, PolicyScreen.SliderX(policy, value));
        Assert.InRange(PolicyScreen.SliderValue(policy, x), value - 4, value + 4);
    }

    [Fact]
    public void WithoutTheGraphicsThePolicyIsATable() => Assert.False(new PolicyWindow().IsFullscreen(new WindowTestHost()));
}
