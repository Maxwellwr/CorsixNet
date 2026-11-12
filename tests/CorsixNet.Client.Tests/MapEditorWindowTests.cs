using CorsixNet.Client.Maps;
using CorsixNet.Client.Ui.Windows;
using CorsixNet.Content.Levels;
using CorsixNet.Content.Settings;

namespace CorsixNet.Client.Tests;

public sealed class MapEditorWindowTests
{
    [Fact]
    public void The_parcel_box_takes_a_number_and_sets_that_parcel()
    {
        var host = new WindowTestHost();
        var tool = new MapEditorTool(MapEditor.Blank());
        var window = new MapEditorWindow(tool, null!, _ => { });

        host.Click(window, "Set parcel: 10");
        Assert.True(window.IsCapturing);
        foreach (var key in new[] { "delete", "delete", "1", "5", "7" })
        {
            window.Capture(new Hotkey(key, false, false, false));
        }

        Assert.Contains(host.Lines(window), line => line.Contains("Parcel: 15_", StringComparison.Ordinal));
        window.Capture(new Hotkey("return", false, false, false));

        Assert.False(window.IsCapturing);
        Assert.Equal((EditorMode.Parcel, 15), (tool.Mode, tool.Number));
    }

    [Fact]
    public void The_player_count_buttons_set_the_maps_player_count()
    {
        var host = new WindowTestHost();
        var tool = new MapEditorTool(MapEditor.Blank());
        var window = new MapEditorWindow(tool, null!, _ => { });

        host.Click(window, "4 players");

        Assert.Equal(4, tool.Editor.Map.PlayerCount);
    }
}
