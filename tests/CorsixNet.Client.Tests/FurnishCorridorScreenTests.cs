using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim.Definitions;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class FurnishCorridorScreenTests
{
    [Fact]
    public void TheCorridorsObjectsComeInCorsixThsOrderWithPreviews()
    {
        var host = new WindowTestHost();
        var objects = FurnishCorridorScreen.Objects(host);
        Assert.Equal("reception_desk", objects[0].Id);
        Assert.All(objects, o => Assert.True(FurnishCorridorScreen.Previews.ContainsKey(o.Id)));
        Assert.Equal(host.Simulation.Content.Objects.Count(o => o.CorridorObject), objects.Count);
    }

    [Fact]
    public void ConfirmedObjectsGoOnTheCursorOneAfterTheOther()
    {
        var host = new WindowTestHost();
        var tool = new CorsixNet.Client.Maps.FurnishCorridorTool();
        var bench = host.Simulation.Content.FindObject("bench")!;
        var plant = host.Simulation.Content.FindObject("plant")!;
        tool.Start(new ObjectDefinition[] { bench, bench, plant });
        Assert.Equal("bench", tool.PlacingId);
        Assert.Equal(2, tool.Queued);
        tool.Stop();
        Assert.Equal(0, tool.Queued);
        Assert.False(new FurnishCorridorWindow(_ => { }).IsFullscreen(host));
    }
}
