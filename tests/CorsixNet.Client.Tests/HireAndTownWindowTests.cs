using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Client.Tests;

/// <summary>The hire window, the town map and the watch.</summary>
public class HireAndTownWindowTests
{
    [Fact]
    public void TheHireWindowPutsTheCandidateOnTheCursor()
    {
        var host = new WindowTestHost();
        var window = new HireWindow();
        var candidates = host.Simulation.AvailableStaff(StaffClass.Nurse).Count;
        Assert.True(candidates > 0);

        host.Click(window, $"Nurses ({candidates})");
        host.Click(window, "Hire", skip: 1);

        Assert.Equal((StaffClass.Nurse, 1), host.Hiring);
    }

    [Fact]
    public void TheHireWindowSaysWhenTheHospitalCannotAffordIt()
    {
        var host = new WindowTestHost(new LevelRules(10, [], []));
        var window = new HireWindow();

        host.Click(window, "Hire");

        Assert.Null(host.Hiring);
        Assert.Single(host.Notices);
        Assert.Equal(["wrong2.wav"], host.Sounds);
    }

    [Fact]
    public void TheTownMapTurnsTheHeatingAndListsTheLand()
    {
        var host = new WindowTestHost();
        var window = new TownMapWindow();
        var heat = host.Simulation.Hospitals[0].RadiatorHeat;

        host.Click(window, "Warmer");
        Assert.Equal(heat + 1, host.Simulation.Hospitals[0].RadiatorHeat);
        Assert.Contains("Parcel 1|You||", host.Lines(window));
    }

    [Fact]
    public void TheWatchCountsDownToTheOpeningAndOpensAtOnce()
    {
        var host = new WindowTestHost();
        var window = new WatchWindow();
        Assert.False(host.Simulation.Hospitals[0].IsOpen);
        Assert.False(window.IsDone(host));
        Assert.StartsWith("Opening in ", host.Lines(window)[0], StringComparison.Ordinal);

        host.Click(window, "Open now");

        Assert.True(host.Simulation.Hospitals[0].IsOpen);
        Assert.True(window.IsDone(host));
        Assert.IsType<OpenHospitalCommand>(host.Submitted[^1]);
    }
}
