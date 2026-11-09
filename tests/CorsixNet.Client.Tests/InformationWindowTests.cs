using CorsixNet.Client.Ui.Windows;

namespace CorsixNet.Client.Tests;

/// <summary>The information, confirmation and tip of the day dialogs.</summary>
public class InformationWindowTests
{
    [Fact]
    public void ConfirmingRunsTheActionAndCancellingDoesNot()
    {
        var host = new WindowTestHost();
        var done = 0;
        var window = new ConfirmWindow("Quit?", () => done++);
        host.Open(window);

        host.Click(window, "Cancel");
        Assert.Equal(0, done);
        Assert.Empty(host.Opened);

        host.Open(window);
        host.Click(window, "OK");
        Assert.Equal(1, done);
        Assert.Empty(host.Opened);
    }

    [Fact]
    public void InformationShowsItsParagraphsAndCloses()
    {
        var host = new WindowTestHost();
        var window = new InformationWindow(["First.", "Second."]);
        host.Open(window);

        var lines = host.Lines(window);
        Assert.Contains("First.", lines);
        Assert.Contains("Second.", lines);
        Assert.False(window.IsSameAs(new InformationWindow(["Other."])));

        host.Click(window, "OK");
        Assert.Empty(host.Opened);
    }

    [Fact]
    public void TheTipsGoRoundBothWays()
    {
        var host = new WindowTestHost();
        var window = new TipOfTheDayWindow(-1);
        Assert.Equal(TipOfTheDayWindow.Tips.Count - 1, window.Tip);

        host.Click(window, "Next tip");
        Assert.Equal(0, window.Tip);
        host.Click(window, "Previous tip");
        Assert.Equal(TipOfTheDayWindow.Tips.Count - 1, window.Tip);
    }
}
