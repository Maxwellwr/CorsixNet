using CorsixNet.Client.Ui.Windows;

namespace CorsixNet.Client.Tests;

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

        Assert.Equal(["LEVEL.L1", "LEVEL.L2"], host.Lines(window));
        host.Click(window, "LEVEL.L2");
        var question = Assert.IsType<ConfirmWindow>(host.Opened[^1]);
        Assert.Empty(chosen);

        host.Click(question, "OK");
        Assert.Equal(["LEVEL.L2"], chosen);
        Assert.DoesNotContain(window, host.Opened);
    }

    [Fact]
    public void The_other_players_only_see_the_levels()
    {
        var host = new WindowTestHost();
        var window = new LobbyWindow(() => ["LEVEL.L1", "LEVEL.L2"], () => "LEVEL.L2", () => false, _ => Assert.Fail("Only the host chooses."));
        host.Open(window);

        Assert.Equal(["Only player 1 chooses the level.", "LEVEL.L1", "LEVEL.L2"], host.Lines(window));
        Assert.Equal(
            ["The server offers no other levels."],
            host.Lines(new LobbyWindow(() => [], () => null, () => true, _ => { })));
    }
}
