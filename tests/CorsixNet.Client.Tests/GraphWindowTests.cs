using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim.Economy;

namespace CorsixNet.Client.Tests;

/// <summary>The graphs window (F8).</summary>
public class GraphWindowTests
{
    [Fact]
    public void TheScalesPickEveryMonthYearOrFourYearsFromTheNewest()
    {
        var months = Enumerable.Range(0, 100).Select(i => new MonthStatistics(i, 0, 0, 0, 0, 0, 0, 0)).ToList();

        Assert.Equal(Enumerable.Range(88, 12).Reverse().Select(i => (long)i), GraphWindow.Values(months, 3).Select(s => s.MoneyIn));
        Assert.Equal([99L, 87, 75, 63, 51, 39, 27, 15, 3], GraphWindow.Values(months, 2).Select(s => s.MoneyIn));
        Assert.Equal([99L, 51, 3], GraphWindow.Values(months, 1).Select(s => s.MoneyIn));
    }

    [Fact]
    public void EachLineIsScaledToItsOwnRange()
    {
        Assert.Equal(0.5, GraphWindow.Height(5, 0, 10));
        Assert.Equal(1, GraphWindow.Height(-2, -10, -2));
        Assert.Equal(0, GraphWindow.Height(3, 3, 3));
    }

    [Fact]
    public void TheButtonsSwitchLinesAndScales()
    {
        var host = new WindowTestHost();
        var window = new GraphWindow();
        Assert.Contains(window.Rows(host).SelectMany(row => row.Cells), cell => cell.Text == "Months" && cell.OnClick == null);

        host.Click(window, "Years");
        host.Click(window, "Wages");

        var cells = window.Rows(host).SelectMany(row => row.Cells).ToList();
        Assert.Contains(cells, cell => cell.Text == "Months" && cell.OnClick != null);
        Assert.Contains(cells, cell => cell.Text == "Wages" && !cell.Highlight);
        Assert.Contains(cells, cell => cell.Text == "Balance" && cell.Highlight);

        host.Click(window, "Figures");
        Assert.IsType<GraphFiguresWindow>(Assert.Single(host.Opened));
    }
}
