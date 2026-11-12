using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim.Economy;

namespace CorsixNet.Client.Tests;

/// <summary>Codes typed on the fax (<c>UIFax:validate</c>, <c>Cheats:processCheatCode</c>).</summary>
public sealed class FaxCodeTests
{
    [Theory]
    [InlineData("24328", (int)FaxCode.Cheats)]
    [InlineData("112", (int)FaxCode.Hello)]
    [InlineData("1", (int)FaxCode.None)]
    [InlineData("363749", (int)FaxCode.Toggle)]
    public void CodesAreRecognised(string code, int expected) => Assert.Equal((FaxCode)expected, FaxCodes.Check(code));

    [Theory]
    [InlineData("768546", CheatKind.ToggleSpawnRate)]
    [InlineData("363749", CheatKind.ToggleNoRest)]
    [InlineData("367140", CheatKind.ToggleQueueJump)]
    [InlineData("386164", CheatKind.ToggleSuperDoctor)]
    public void EachToggleCheatHasACode(string code, CheatKind cheat) => Assert.Equal(cheat, FaxCodes.ToggleCheat(code)?.Cheat);

    [Fact]
    public void TheOriginalsCodeOpensTheCheatsAndAWrongCodeBuzzes()
    {
        var host = new WindowTestHost();
        var fax = new FaxWindow();
        host.Opened.Add(fax);

        foreach (var digit in "24328")
        {
            host.Click(fax, digit.ToString());
        }

        Assert.Equal("24328", fax.Code);
        host.Click(fax, "Validate");

        Assert.Contains(host.Opened, window => window is CheatsWindow);
        Assert.Equal("fax_yes.wav", host.Sounds[^1]);
        Assert.Equal("Congratulations, you have unlocked cheats!", host.Adviser.Current);

        host.Click(fax, "9");
        host.Click(fax, "9");
        host.Click(fax, "Correct");
        Assert.Equal("9", fax.Code);
        host.Click(fax, "Validate");
        Assert.Equal("fax_no.wav", host.Sounds[^1]);
        Assert.Equal(string.Empty, fax.Code);
    }

    [Fact]
    public void AToggleCodeTurnsItsCheatOnAndOff()
    {
        var host = new WindowTestHost();
        var fax = new FaxWindow();
        var hospital = host.Simulation.Hospitals[0];

        Type(host, fax, "367140");
        Assert.True(hospital.ActiveCheats.HasFlag(ToggleCheats.QueueJump));
        Assert.True(hospital.Cheated);
        Assert.StartsWith("Your patients know how to queue.", host.Adviser.Current);

        host.Adviser.Next(0);
        Type(host, fax, "367140");
        Assert.False(hospital.ActiveCheats.HasFlag(ToggleCheats.QueueJump));
        Assert.StartsWith("People have turned selfish", host.Adviser.Current);
    }

    private static void Type(WindowTestHost host, FaxWindow fax, string code)
    {
        foreach (var digit in code)
        {
            host.Click(fax, digit.ToString());
        }

        host.Click(fax, "Validate");
    }
}
