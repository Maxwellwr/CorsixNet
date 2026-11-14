using CorsixNet.Client.Ui.Windows;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class BankManagerScreenTests
{
    [Fact]
    public void TheManagersFaceMovesAtSetPointsAndComesBack()
    {
        Assert.Equal((7, 9, 12), BankManagerScreen.Face(0));
        // The first blink starts at tick 24: the eyes close on its third tick.
        Assert.Equal((8, 9, 12), BankManagerScreen.Face(26));
        Assert.Equal((7, 9, 12), BankManagerScreen.Face(35));
        // The smile, from tick 132.
        Assert.Equal(14, BankManagerScreen.Face(139).Smile);
        // A whole cycle of 421 ticks ends where it began.
        Assert.Equal(BankManagerScreen.Face(0), BankManagerScreen.Face(421));
        Assert.Equal((7, 9, 12), BankManagerScreen.Face(420));
    }

    [Fact]
    public void WithoutTheGraphicsTheBankManagerIsATable()
    {
        var host = new WindowTestHost();
        var window = new BankManagerWindow();
        Assert.False(window.IsFullscreen(host));
        Assert.Contains("Balance", host.Lines(window).First(), StringComparison.Ordinal);
    }
}
