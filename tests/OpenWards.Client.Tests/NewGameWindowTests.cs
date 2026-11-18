using OpenWards.Client.Ui.Windows;
using OpenWards.Content.Settings;
using OpenWards.Sim.Economy;

namespace OpenWards.Client.Tests;

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

    [Fact]
    public void The_name_is_typed_in_its_box_and_saved_unless_blank()
    {
        var host = new WindowTestHost();
        var saved = new List<string>();
        var window = new NewGameWindow([Difficulty.Medium], "Maxim", (_, _) => { }, saved.Add);
        host.Open(window);

        host.Click(window, "Maxim");
        Assert.True(window.IsCapturing);
        foreach (var character in "!-Dr Ж+1234567890")
        {
            window.Type(character);
        }

        // Maxim plus ten allowed characters reach the 15 character limit; ! and Ж are not taken.
        Assert.Equal("Maxim-Dr +12345", window.TypedName);
        window.Capture(new Hotkey("backspace"));
        window.Capture(new Hotkey("return"));
        Assert.False(window.IsCapturing);
        Assert.Equal("Maxim-Dr +1234", window.PlayerName);
        Assert.Equal(["Maxim-Dr +1234"], saved);

        // A blank name puts the old one back, Escape too, and neither is saved.
        host.Click(window, window.PlayerName);
        while (window.TypedName.Length > 0)
        {
            window.Capture(new Hotkey("backspace"));
        }

        window.Type(' ');
        window.Capture(new Hotkey("return"));
        host.Click(window, window.PlayerName);
        window.Type('X');
        window.Capture(new Hotkey("escape"));
        Assert.Single(saved);
        Assert.Equal("Maxim-Dr +1234", window.PlayerName);
    }

    [Fact]
    public void Start_keeps_the_name_being_typed()
    {
        var host = new WindowTestHost();
        string? saved = null;
        var window = new NewGameWindow([Difficulty.Medium], "Maxim", (_, _) => { }, name => saved = name);
        host.Open(window);

        window.StartTyping();
        window.Type('a');
        host.Click(window, "Start");

        Assert.Equal("Maxima", saved);
    }
}
