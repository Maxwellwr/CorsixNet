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

    [Fact]
    public void TheRotateObjectKeyTurnsTheObjectOnTheCursor()
    {
        var host = new WindowTestHost();
        var tool = new CorsixNet.Client.Maps.FurnishCorridorTool();
        var sounds = new List<string>();
        tool.PlaySound = sounds.Add;
        tool.Start(new ObjectDefinition[] { host.Simulation.Content.FindObject("bench")! });
        var at = new CorsixNet.Sim.Maps.TilePosition(6, 6);
        tool.Update(host.Simulation, 0, new CorsixNet.Client.Maps.BuildInput(at, 128, 128, false, false, false, false, false, false, false));
        var before = tool.Ghost!.Value.Orientation;

        tool.Update(host.Simulation, 0, new CorsixNet.Client.Maps.BuildInput(at, 128, 128, false, false, false, false, false, false, false, Rotate: true));

        Assert.NotEqual(before, tool.Ghost!.Value.Orientation);
        Assert.Equal("swoosh.wav", sounds[^1]);
    }
}
