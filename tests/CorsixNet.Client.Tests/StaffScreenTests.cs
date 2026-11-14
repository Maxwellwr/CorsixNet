using CorsixNet.Client.Ui.Windows;
using Microsoft.Xna.Framework;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class StaffScreenTests
{
    [Fact]
    public void TheViewCircleAndBarsAreWhereCorsixThHasThem()
    {
        Assert.True(StaffScreen.InViewCircle(new Point(55, 248), handyman: false));
        Assert.False(StaffScreen.InViewCircle(new Point(55, 248 + 39), handyman: false));
        Assert.True(StaffScreen.InViewCircle(new Point(55, 276), handyman: true));
        Assert.Equal(new Point(220, 304), StaffScreen.Size(false));
        Assert.Equal(new Point(220, 332), StaffScreen.Size(true));
        Assert.Equal(20, StaffScreen.Bar(0.5m));
        Assert.Equal(40, StaffScreen.Bar(1.2m));
        Assert.Equal(0, StaffScreen.Bar(0m));
    }

    [Fact]
    public void WithoutTheGraphicsTheStaffWindowIsATable() => Assert.False(new StaffWindow(1).IsFullscreen(new WindowTestHost()));
}
