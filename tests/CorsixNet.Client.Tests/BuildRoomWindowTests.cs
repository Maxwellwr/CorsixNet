using CorsixNet.Client.Ui.Windows;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class BuildRoomWindowTests
{
    [Fact]
    public void EveryRoomHasAKindAndAPreview()
    {
        var host = new WindowTestHost();
        foreach (var room in host.Simulation.Content.Rooms)
        {
            Assert.Contains(BuildRoomWindow.Categories, category => category.Contains(room.Id));
            Assert.True(BuildRoomWindow.Previews.ContainsKey(room.Id), room.Id);
        }
    }

    [Fact]
    public void WithoutTheGraphicsThereIsNoOriginalWindow() => Assert.False(new BuildRoomWindow(_ => { }).IsFullscreen(new WindowTestHost()));
}
