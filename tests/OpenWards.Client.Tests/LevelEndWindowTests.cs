using OpenWards.Client.Ui.Windows;
using OpenWards.Sim.Economy;

namespace OpenWards.Client.Tests;

/// <summary>The level's end (CorsixTH's World:loseGame message).</summary>
public sealed class LevelEndWindowTests
{
    [Fact]
    public void TheLosingMessageSaysWhy()
    {
        var lines = LevelEndWindow.LostLines(null, new EndGoal(EndCriterion.Balance, 1, false, -5000, 0, 0));
        Assert.Equal(3, lines.Count);
        Assert.Equal("Your bank balance fell below -5000.", lines[2]);
        Assert.Equal(2, LevelEndWindow.LostLines(null, null).Count);
    }
}
