using CorsixNet.Client.Ui;
using CorsixNet.Client.Ui.Windows;
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
        UiWindow[] windows = [new BankManagerWindow(), new TownMapWindow(), new GraphWindow(), new ResearchWindow(), new PolicyWindow(), new StaffWindow(nurse.Id), new PatientWindow(patient.Id)];

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
}
