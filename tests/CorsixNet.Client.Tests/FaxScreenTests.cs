using CorsixNet.Client.Ui.Windows;
using Microsoft.Xna.Framework;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class FaxScreenTests
{
    [Fact]
    public void ChoicesFillTheThreePlacesFromTheBottom()
    {
        Assert.Equal(new Rectangle(492, 175 + (2 * 48), 43, 43), FaxScreen.ChoiceButton(0, 1));
        Assert.Equal(new Rectangle(492, 175 + 48, 43, 43), FaxScreen.ChoiceButton(0, 2));
        Assert.Equal(new Rectangle(492, 175, 43, 43), FaxScreen.ChoiceButton(0, 3));
    }

    [Fact]
    public void AReportFaxIsOneMessageWithItsCloseChoice()
    {
        var host = new WindowTestHost();
        var hospital = host.Simulation.Hospitals[0];
        CorsixNet.Sim.Economy.VipVisits.OnVipLeft(host.Simulation, hospital, new CorsixNet.Sim.People.VipBehaviour(3, 2));
        var fax = new FaxWindow();
        var message = Assert.Single(fax.Messages(host));
        var choice = Assert.Single(message.Choices);
        choice.Choose();
        Assert.Null(hospital.Vips.Report);
        Assert.False(fax.IsFullscreen(host));
    }
}
