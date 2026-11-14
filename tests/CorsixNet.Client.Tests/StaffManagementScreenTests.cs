using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim.Staff;
using Microsoft.Xna.Framework;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class StaffManagementScreenTests
{
    [Fact]
    public void AHeadingOrdersOneWayThenTheOtherThenBackToHiring()
    {
        var screen = new StaffManagementScreen();
        screen.OrderBy(StaffOrder.Skill);
        Assert.Equal((StaffOrder.Skill, false), (screen.Order, screen.Descending));
        screen.OrderBy(StaffOrder.Skill);
        Assert.Equal((StaffOrder.Skill, true), (screen.Order, screen.Descending));
        screen.OrderBy(StaffOrder.Skill);
        Assert.Equal((StaffOrder.Hire, false), (screen.Order, screen.Descending));
    }

    [Fact]
    public void PagesTurnOnlyWhileThereIsMoreThanOne()
    {
        var screen = new StaffManagementScreen();
        screen.Scroll(10, 1);
        Assert.Equal(1, screen.Page);
        screen.Scroll(25, 1);
        screen.Scroll(25, 1);
        Assert.Equal(3, screen.Page);
        screen.Scroll(25, 1);
        Assert.Equal(3, screen.Page);
        screen.Scroll(25, -1);
        Assert.Equal(2, screen.Page);
        screen.SetCategory(StaffClass.Nurse);
        Assert.Equal((StaffClass.Nurse, 1), (screen.Category, screen.Page));
    }

    [Fact]
    public void TheScrollDotGoesDownWithThePage()
    {
        Assert.Null(StaffManagementScreen.ScrollDot(10, 1));
        Assert.Equal(168, StaffManagementScreen.ScrollDot(25, 1));
        Assert.Equal(168 + 41, StaffManagementScreen.ScrollDot(25, 2));
        Assert.Equal(168 + 83, StaffManagementScreen.ScrollDot(25, 3));
    }

    [Fact]
    public void ARowIsHitOnlyWhereSomeoneIsListed()
    {
        var screen = new StaffManagementScreen();
        Assert.Equal(0, screen.RowAt(new Point(100, 90), 2));
        Assert.Equal(1, screen.RowAt(new Point(100, 120), 2));
        Assert.Null(screen.RowAt(new Point(100, 150), 2));
        Assert.Null(screen.RowAt(new Point(30, 90), 2));
    }

    [Fact]
    public void ArrowKeysChangeTheKindAndTheMemberOfStaff()
    {
        var screen = new StaffManagementScreen();
        screen.Key("ingame_scroll_left", []);
        Assert.Equal(StaffClass.Doctor, screen.Category);
        screen.Key("ingame_scroll_right", []);
        Assert.Equal(StaffClass.Nurse, screen.Category);
        var listed = Enumerable.Range(100, 12).ToList();
        screen.Key("ingame_scroll_down", listed);
        Assert.Equal((100, 1), (screen.Selected, screen.Page));
        screen.Key("ingame_scroll_up", listed);
        Assert.Equal(100, screen.Selected);
        screen.SetCategory(StaffClass.Nurse);
        screen.Key("ingame_scroll_up", listed);
        Assert.Equal((111, 2), (screen.Selected, screen.Page));
    }

    [Fact]
    public void WithoutTheGraphicsItIsATable() => Assert.False(new StaffManagementWindow().IsFullscreen(new WindowTestHost()));
}
