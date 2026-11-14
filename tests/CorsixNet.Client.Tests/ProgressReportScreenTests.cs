using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim.Economy;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class ProgressReportScreenTests
{
    [Fact]
    public void GoalsHaveTheirIconsAndBars()
    {
        Assert.Equal(("REP02V", 10), ProgressReportScreen.Icon(EndCriterion.Reputation));
        Assert.Equal(("MPOINTER", 18), ProgressReportScreen.Icon(EndCriterion.PatientHappiness));
        var win = new ProgressEntry(new EndGoal(EndCriterion.Reputation, 1, AtLeast: true, 600, 0, 0), IsLoseGoal: false, null);
        Assert.Equal(25, ProgressReportScreen.BarHeight(win, 300));
        Assert.Equal(50, ProgressReportScreen.BarHeight(win, 900));
        var lose = new ProgressEntry(new EndGoal(EndCriterion.Reputation, 0, AtLeast: false, 200, 300, 0), IsLoseGoal: true, null);
        Assert.Equal(25, ProgressReportScreen.BarHeight(lose, 250));
    }

    [Fact]
    public void WarmthIsNormalisedAsThePatientWindowDoes()
    {
        Assert.Equal(0m, ProgressReportScreen.NormaliseWarmth(0.05m));
        Assert.Equal(1m, ProgressReportScreen.NormaliseWarmth(0.6m));
        Assert.Equal(0.5m, ProgressReportScreen.NormaliseWarmth(0.29m));
        Assert.Equal(0.5m, ProgressReportScreen.NormaliseWarmth(null));
    }
}
