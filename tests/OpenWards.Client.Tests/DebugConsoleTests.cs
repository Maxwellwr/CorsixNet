using OpenWards.Client.Ui.Windows;
using OpenWards.Content.Settings;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Economy;

namespace OpenWards.Client.Tests;

public sealed class DebugConsoleTests
{
    [Fact]
    public void ALineBecomesTheCheatCommandsItNames()
    {
        Assert.Equal([new CheatCommand(CheatKind.Money)], DebugConsoleWindow.Parse("money").Commands);
        Assert.Equal(3, DebugConsoleWindow.Parse("  Patient 3 ").Commands.Count);
        Assert.All(DebugConsoleWindow.Parse("patient 3").Commands, command => Assert.Equal(new CheatCommand(CheatKind.CreatePatient), command));

        // Repeats are capped, so a typo cannot flood the game.
        Assert.Single(DebugConsoleWindow.Parse("vip 40").Commands);
        Assert.Equal(50, DebugConsoleWindow.Parse("patient 9999").Commands.Count);

        Assert.NotNull(DebugConsoleWindow.Parse("teleport").Problem);
        Assert.NotNull(DebugConsoleWindow.Parse("money lots").Problem);
        Assert.Empty(DebugConsoleWindow.Parse("help").Commands);
        Assert.Null(DebugConsoleWindow.Parse("   ").Problem);
    }

    [Fact]
    public void ReturnRunsTheTypedLineAndWritesWhatHappened()
    {
        var host = new WindowTestHost();
        var console = new DebugConsoleWindow();
        host.Open(console);
        Assert.True(console.IsCapturing);

        foreach (var character in "money 2")
        {
            console.Type(character);
        }

        console.Capture(new Hotkey("return"));
        _ = console.Rows(host).ToList();

        Assert.Equal([new CheatCommand(CheatKind.Money), new CheatCommand(CheatKind.Money)], host.Submitted);
        Assert.Equal(["- money 2", "Done."], console.Lines);
        Assert.Equal(string.Empty, console.TypedText);

        foreach (var character in "fly")
        {
            console.Type(character);
        }

        host.Click(console, "Execute");
        Assert.Equal(2, host.Submitted.Count);
        Assert.StartsWith("Unknown command: fly", console.Lines[^1], StringComparison.Ordinal);

        host.Click(console, "Close");
        Assert.DoesNotContain(console, host.Opened);
    }

    [Fact]
    public void RatAsksForTheDebugMenusRat()
    {
        var host = new WindowTestHost();
        string? action = null;
        var console = new DebugConsoleWindow(name => action = name);
        foreach (var character in "rat")
        {
            console.Type(character);
        }

        console.Execute(host);
        Assert.Equal("debugrat", action);
        Assert.Empty(host.Submitted);
    }
}
