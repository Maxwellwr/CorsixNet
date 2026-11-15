using CorsixNet.Client.Ui;
using CorsixNet.Client.Ui.Windows;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
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

    [Fact]
    public void TheDollarCursorShowsOverTheManagerAndThePieChartOverTheStatement()
    {
        var host = new WindowTestHost();
        var screen = new BankManagerScreen();
        var window = new BankManagerWindow();
        var at = new Point(300, 200);
        Assert.Equal(GameCursor.Bank, screen.Cursor(at));

        static MouseState Mouse(Point at, ButtonState left) => new(at.X, at.Y, 0, left, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        screen.Mouse(host, window, at, Mouse(at, ButtonState.Pressed), Mouse(at, ButtonState.Released));
        screen.Mouse(host, window, at, Mouse(at, ButtonState.Released), Mouse(at, ButtonState.Pressed));

        Assert.Equal(GameCursor.BankSummary, screen.Cursor(at));
        Assert.Null(screen.Cursor(new Point(700, 10)));
    }

    [Fact]
    public void F2OpensItOnTheStatementAndThePagesCanBeSwitched()
    {
        var at = new Point(300, 200);
        var window = new BankManagerWindow(statement: true);
        Assert.True(window.IsShowingStatement);
        Assert.Equal(GameCursor.BankSummary, new BankManagerScreen(statement: true).Cursor(at));

        window.ShowStatement(false);
        Assert.False(window.IsShowingStatement);
        Assert.False(new BankManagerWindow().IsShowingStatement);
    }
}
