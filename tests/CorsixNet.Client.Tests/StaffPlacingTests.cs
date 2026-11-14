using CorsixNet.Client.Maps;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class StaffPlacingTests
{
    [Fact]
    public void ALeftClickPutsThemDownAndARightClickCancels()
    {
        Assert.Equal(StaffPlacingClick.Place, StaffPlacing.Click(candidateGone: false, left: true, right: false, paused: false));
        Assert.Equal(StaffPlacingClick.Cancel, StaffPlacing.Click(candidateGone: false, left: false, right: true, paused: false));
        Assert.Equal(StaffPlacingClick.None, StaffPlacing.Click(candidateGone: false, left: false, right: false, paused: false));
    }

    [Fact]
    public void WhilePausedClicksLeaveThemOnTheCursor()
    {
        // A hire put down while paused vanished from the cursor and only happened once the game went on.
        Assert.Equal(StaffPlacingClick.Paused, StaffPlacing.Click(candidateGone: false, left: true, right: false, paused: true));
        Assert.Equal(StaffPlacingClick.Paused, StaffPlacing.Click(candidateGone: false, left: false, right: true, paused: true));
        Assert.Equal(StaffPlacingClick.None, StaffPlacing.Click(candidateGone: false, left: false, right: false, paused: true));
    }

    [Fact]
    public void ACandidateNoLongerForHireLeavesTheCursor()
    {
        Assert.Equal(StaffPlacingClick.Cancel, StaffPlacing.Click(candidateGone: true, left: false, right: false, paused: false));
        Assert.Equal(StaffPlacingClick.Cancel, StaffPlacing.Click(candidateGone: true, left: true, right: false, paused: true));
    }
}
