using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;

namespace CorsixNet.Client.Tests;

/// <summary>The cheats, progress report, annual report and level end windows.</summary>
public class ReportWindowTests
{
    [Fact]
    public void ACheatFromTheWindowMarksTheHospital()
    {
        var host = new WindowTestHost();
        var window = new CheatsWindow();
        var balance = host.Simulation.Hospitals[0].Balance;

        host.Click(window, "Money +$10,000");
        Assert.Equal(balance + 10_000, host.Simulation.Hospitals[0].Balance);
        Assert.True(host.Simulation.Hospitals[0].Cheated);

        host.Click(window, "No rest: off");
        Assert.True(host.Simulation.Hospitals[0].ActiveCheats.HasFlag(ToggleCheats.NoRest));
        Assert.Contains(window.Rows(host).SelectMany(row => row.Cells), cell => cell.Text == "No rest: on" && cell.Highlight);
    }

    [Fact]
    public void TheProgressReportSaysWhenALevelHasNoGoals()
    {
        var host = new WindowTestHost();

        Assert.Equal(["This level has no goals."], host.Lines(new ProgressReportWindow()));
    }

    [Fact]
    public void WinningOffersTheNextLevel()
    {
        var host = new WindowTestHost();
        var window = new LevelEndWindow();
        host.Submit(new CheatCommand(CheatKind.WinLevel));
        Assert.Equal(EndOutcome.Won, host.Simulation.Hospitals[0].Outcome);

        host.Click(window, "Next level");

        Assert.True(host.Started);
    }

    [Fact]
    public void TheAnnualReportListsTheYearsFigures()
    {
        var host = new WindowTestHost();

        var lines = host.Lines(new AnnualReportWindow());

        Assert.Contains("No trophies or awards this year.", lines);
        Assert.Contains(lines, line => line.StartsWith("Hospital value|", StringComparison.Ordinal));
    }
}
