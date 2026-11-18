using OpenWards.Client.Ui.Windows;

namespace OpenWards.Client.Tests;

/// <summary>The network game's level list.</summary>
public class LobbyWindowTests
{
    [Fact]
    public void The_host_chooses_another_level_after_a_question()
    {
        var host = new WindowTestHost();
        var chosen = new List<string>();
        var window = new LobbyWindow(() => ["LEVEL.L1", "LEVEL.L2"], () => "LEVEL.L1", () => true, chosen.Add);
        host.Open(window);

        Assert.Equal(["Ward No. 1", "Ward No. 2"], host.Lines(window));
        host.Click(window, "Ward No. 2");
        var question = Assert.IsType<ConfirmWindow>(host.Opened[^1]);
        Assert.Empty(chosen);

        host.Click(question, "OK");
        Assert.Equal(["LEVEL.L2"], chosen);
        Assert.DoesNotContain(window, host.Opened);
    }

    [Fact]
    public void Other_levels_keep_their_file_name()
    {
        var host = new WindowTestHost();
        var window = new LobbyWindow(() => ["Avatar.level"], () => null, () => true, _ => { });
        host.Open(window);

        Assert.Equal(["Avatar.level"], host.Lines(window));
    }

    [Fact]
    public void The_other_players_only_see_the_levels()
    {
        var host = new WindowTestHost();
        var window = new LobbyWindow(() => ["LEVEL.L1", "LEVEL.L2"], () => "LEVEL.L2", () => false, _ => Assert.Fail("Only the host chooses."));
        host.Open(window);

        Assert.Equal(["Only player 1 chooses the level.", "Ward No. 1", "Ward No. 2"], host.Lines(window));
        Assert.Equal(
            ["The server offers no other levels."],
            host.Lines(new LobbyWindow(() => [], () => null, () => true, _ => { })));
    }
}
