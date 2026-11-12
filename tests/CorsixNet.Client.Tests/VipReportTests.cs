using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim;
using CorsixNet.Sim.Disasters;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.People;

namespace CorsixNet.Client.Tests;

/// <summary>The VIP's report and an emergency's result on the fax (<c>makeVipEndFax</c>, <c>makeEmergencyEndFax</c>).</summary>
public sealed class VipReportTests
{
    [Fact]
    public void APleasedVipsReportTellsOfTheBoostAndTheGrantAndExpires()
    {
        var host = new WindowTestHost();
        var hospital = host.Simulation.Hospitals[0];

        VipVisits.OnVipLeft(host.Simulation, hospital, new VipBehaviour(3, 2));

        Assert.NotNull(hospital.Vips.Report);
        Assert.Contains(FaxWindow.Pending(host.Simulation, 0), key => key.StartsWith("vipreport:", StringComparison.Ordinal));
        var lines = host.Lines(new FaxWindow());
        Assert.Equal("VIP 3 has made a report on your hospital:", lines[0]);
        Assert.Contains("Your reputation has gone up.", lines);
        Assert.Contains("You have been given a grant of 0.", lines);

        // It goes when its 20 days are up.
        var expires = hospital.Vips.Report!.Value.Expires;
        while (host.Simulation.Tick <= expires)
        {
            host.Simulation.Step();
        }

        Assert.Null(hospital.Vips.Report);
    }

    [Fact]
    public void ADispleasedVipsReportTellsOfTheLossAndIsSaved()
    {
        var host = new WindowTestHost();
        var hospital = host.Simulation.Hospitals[0];
        VipVisits.OnVipLeft(host.Simulation, hospital, new VipBehaviour(1, 12));

        var lines = FaxWindow.VipReportLines(host, hospital.Vips.Report!.Value).ToList();
        Assert.Equal(["VIP 1 has made a report on your hospital:", "A poor hospital. Things must improve.", "Your reputation has gone down."], lines);

        using var stream = new MemoryStream();
        host.Simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, host.Simulation.Content);
        Assert.Equal(hospital.Vips.Report, restored.Hospitals[0].Vips.Report);
    }

    [Fact]
    public void AnEmergencysResultTellsOfThePeopleSavedAndTheBonus()
    {
        var host = new WindowTestHost();
        var report = new EmergencyReport(3, 5, 5000, 3000, 100);

        Assert.Equal(["You saved 3 of the 5 people in the emergency.", "The most you could have earned was 5000. You earned 3000."], FaxWindow.EmergencyReportLines(host, report));
    }
}
