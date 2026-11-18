using Microsoft.Xna.Framework;

namespace OpenWards.Client.Tests;

public sealed class MouseGrabPolicyTests
{
    [Fact]
    public void FocusAloneDoesNotGrab()
    {
        var policy = new MouseGrabPolicy();

        Assert.False(policy.Update(active: true, clickedInside: false, wanted: true));
    }

    [Fact]
    public void AClickInsideGrabsUntilFocusIsLost()
    {
        var policy = new MouseGrabPolicy();

        Assert.True(policy.Update(active: true, clickedInside: true, wanted: true));
        Assert.True(policy.Update(active: true, clickedInside: false, wanted: true));
        Assert.False(policy.Update(active: false, clickedInside: false, wanted: true));

        // Back by Alt+Tab: free until the next click.
        Assert.False(policy.Update(active: true, clickedInside: false, wanted: true));
        Assert.True(policy.Update(active: true, clickedInside: true, wanted: true));
    }

    [Fact]
    public void MenusLetGoAndTheGameTakesItBack()
    {
        var policy = new MouseGrabPolicy();
        policy.Update(active: true, clickedInside: true, wanted: true);

        Assert.False(policy.Update(active: true, clickedInside: false, wanted: false));
        Assert.True(policy.Update(active: true, clickedInside: false, wanted: true));
    }

    [Fact]
    public void AClickWithoutFocusDoesNotCount()
    {
        var policy = new MouseGrabPolicy();

        Assert.False(policy.Update(active: false, clickedInside: true, wanted: true));
        Assert.False(policy.Update(active: true, clickedInside: false, wanted: true));
    }

    [Theory]
    [InlineData(640, 480, true)]
    [InlineData(380, 110, false)]
    [InlineData(640, 200, false)]
    public void OnlyDialogsCoveringTheGameLetGo(int width, int height, bool covers) =>
        Assert.Equal(covers, MouseGrabPolicy.CoversGame(new Point(width, height)));
}
