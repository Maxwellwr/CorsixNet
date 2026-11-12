using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim.Economy;

namespace CorsixNet.Client.Tests;

/// <summary>The new game dialog.</summary>
public class NewGameWindowTests
{
    [Fact]
    public void StartsAtMediumWhenItIsThereAndStartsTheChosenDifficulty()
    {
        var host = new WindowTestHost();
        Difficulty? started = null;
        var window = new NewGameWindow([Difficulty.Easy, Difficulty.Medium, Difficulty.Hard], "Maxim", (difficulty, _) => started = difficulty);
        host.Open(window);

        Assert.Equal(Difficulty.Medium, window.Selected);
        Assert.Contains("Player name|Maxim", host.Lines(window));

        host.Click(window, NewGameWindow.Name(Difficulty.Hard));
        Assert.Equal(Difficulty.Hard, window.Selected);
        Assert.Contains(NewGameWindow.Description(Difficulty.Hard), host.Lines(window));

        host.Click(window, "Start");
        Assert.Equal(Difficulty.Hard, started);
        Assert.Empty(host.Opened);
    }

    [Fact]
    public void OffersOnlyTheInstalledDifficultiesAndCancelsWithoutStarting()
    {
        var host = new WindowTestHost();
        var started = false;
        var window = new NewGameWindow([Difficulty.Medium], string.Empty, (_, _) => started = true);
        host.Open(window);

        Assert.DoesNotContain(host.Lines(window), line => line.Contains(NewGameWindow.Name(Difficulty.Easy), StringComparison.Ordinal));
        host.Click(window, "Cancel");
        Assert.False(started);
        Assert.Empty(host.Opened);
    }
}
