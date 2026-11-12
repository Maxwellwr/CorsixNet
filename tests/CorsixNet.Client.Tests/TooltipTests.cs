using CorsixNet.Client.Ui;
using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Client.Tests;

/// <summary>The windows' buttons carry the original's tooltips (CorsixTH's <c>tooltip.*</c>).</summary>
public sealed class TooltipTests
{
    [Fact]
    public void TheDialogsButtonsHaveTooltips()
    {
        var host = new WindowTestHost();
        var nurse = host.Hire(StaffClass.Nurse);
        var patient = host.AddPatient();
        host.Adviser.Say("Hello", 0);
        UiWindow[] windows =
        [
            new BankManagerWindow(), new TownMapWindow(), new GraphWindow(), new ResearchWindow(), new PolicyWindow(), new StaffWindow(nurse.Id), new PatientWindow(patient.Id),
            new HireWindow(), new CasebookWindow(), new InformationWindow(["Hello"]), new ConfirmWindow("Sure?", () => { }), new TipOfTheDayWindow(0),
            new NewGameWindow([Difficulty.Easy, Difficulty.Medium], "Dr", _ => { }), new FaxWindow(), new CheatsWindow(), new AdviserHistoryWindow(),
            new LevelEndWindow(),
        ];

        foreach (var window in windows)
        {
            var untipped = window.Rows(host).SelectMany(row => row.Cells).Where(cell => cell.IsButton && cell.Tooltip is null).Select(cell => cell.Text).ToList();
            Assert.True(untipped.Count == 0, $"{window.Title}: {string.Join(", ", untipped)}");
        }
    }

    [Fact]
    public void TheEnglishCorrectionsAndStandInsAreUsedWithoutTheGamesStrings()
    {
        var host = new WindowTestHost();
        var graph = new GraphWindow();
        var research = new ResearchWindow();

        var cells = graph.Rows(host).Concat(research.Rows(host)).SelectMany(row => row.Cells).ToList();

        Assert.Contains(cells, cell => cell.Text == "Reputation" && cell.Tooltip == "Toggle reputation");
        Assert.Contains(cells, cell => cell.Text == "Money in" && cell.Tooltip == "Toggle money in");
        Assert.Contains(cells, cell => cell.Text == "+1" && cell.Tooltip == "Increase Cure research percentage");
    }

    [Theory]
    [InlineData(EndCriterion.Reputation, false, 600, 450.7, "Your Reputation should not be less than 600. Currently it's 450")]
    [InlineData(EndCriterion.PercentageKilled, true, 30, 12, "You should kill less than 30% of your patients. Currently you have killed 12%")]
    [InlineData(EndCriterion.StaffHappiness, false, 70, 65, "Your average staff happiness should be over 70%. Currently it's 65%")]
    [InlineData(EndCriterion.StaffHappiness, true, 20, 65, "Your average staff happiness should not be less than 20%. Currently it's 65%")]
    [InlineData(EndCriterion.MonthsPlayed, false, 24, 5, "You need to reach the year 2002. Currently it's the year 2000")]
    [InlineData(EndCriterion.MonthsPlayed, true, 18, 13, "You need to win by the end of June 2001. Currently it's January 2001")]
    public void TheProgressReportsGoalsSayWhereTheHospitalStands(EndCriterion criterion, bool isLoseGoal, double target, double now, string expected) =>
        Assert.Equal(expected, ProgressReportWindow.Tooltip(new WindowTestHost(), criterion, isLoseGoal, (decimal)target, (decimal)now));
}
