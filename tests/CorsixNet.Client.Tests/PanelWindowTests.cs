using CorsixNet.Client.Maps;
using CorsixNet.Client.Ui;
using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim.Definitions;

namespace CorsixNet.Client.Tests;

/// <summary>The bottom panel, the furnish corridor window and the room edit panel.</summary>
public class PanelWindowTests
{
    [Fact]
    public void TheBottomPanelStaysWhenDialogsClose()
    {
        var layer = new WindowLayer(text => text.Length * 6, 10);
        layer.Open(new BottomPanel(_ => { }));
        layer.Open(new PolicyWindow());

        Assert.True(layer.Any);
        Assert.True(layer.CloseTop());
        Assert.False(layer.Any);
        Assert.False(layer.CloseTop());
        layer.Open(new CasebookWindow());
        layer.CloseAll();
        Assert.IsType<BottomPanel>(Assert.Single(layer.Windows));
    }

    [Fact]
    public void TheBottomPanelOpensDialogsAndAsksForTheRest()
    {
        var host = new WindowTestHost();
        var actions = new List<string>();
        var panel = new BottomPanel(actions.Add);

        host.Click(panel, "Hire");
        host.Click(panel, "Build");
        host.Click(panel, "Corridor");

        Assert.IsType<HireWindow>(Assert.Single(host.Opened));
        Assert.Equal(["rooms", "corridor"], actions);
        Assert.StartsWith("$100,000|", host.Lines(panel)[0], StringComparison.Ordinal);
    }

    [Fact]
    public void TheFurnishCorridorWindowPutsTheObjectOnTheCursor()
    {
        var host = new WindowTestHost();
        ObjectDefinition? bought = null;
        var window = new FurnishCorridorWindow(definition => bought = definition);
        host.Opened.Add(window);
        var corridor = host.Simulation.Content.Objects.Where(o => o.CorridorObject).OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).First();

        host.Click(window, "Buy");

        Assert.Equal(corridor, bought);
        Assert.Empty(host.Opened);
    }

    [Fact]
    public void TheRoomEditPanelIsOnlyThereWhileFurnishing()
    {
        var host = new WindowTestHost();
        var tool = new BuildRoomTool();

        Assert.True(new RoomEditWindow(tool, _ => { }).IsDone(host));
        tool.Start(host.Simulation.Content.FindRoom("gp")!);
        Assert.True(new RoomEditWindow(tool, _ => { }).IsDone(host));
    }
}
