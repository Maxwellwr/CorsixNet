using OpenWards.Client.Ui.Windows;
using Xunit;

namespace OpenWards.Client.Tests;

public sealed class WatchScreenTests
{
    [Fact]
    public void TheFaceFillsRedAsTheTimerRunsDown()
    {
        Assert.Equal((true, (int?)null), WatchScreen.Face(12));
        Assert.Equal((true, (int?)2), WatchScreen.Face(11));
        Assert.Equal((true, (int?)7), WatchScreen.Face(6));
        Assert.Equal((false, (int?)8), WatchScreen.Face(5));
        Assert.Equal((false, (int?)12), WatchScreen.Face(1));
        Assert.Equal((false, (int?)null), WatchScreen.Face(0));
    }

    [Fact]
    public void WithoutTheGraphicsTheWatchIsATable() => Assert.False(new WatchWindow().IsFullscreen(new WindowTestHost()));

    [Fact]
    public void TheEpidemicWatchsButtonTogglesVaccinationMode()
    {
        var host = new WindowTestHost();
        var hospital = host.Simulation.Hospitals[0];
        new OpenWards.Sim.Commands.OpenHospitalCommand().Execute(host.Simulation, 0);
        hospital.Epidemics.Current = new OpenWards.Sim.Disasters.Epidemic("broken_wind") { IsCoverUp = true, WatchTimer = 12 };
        var screen = new WatchScreen();
        var at = new Microsoft.Xna.Framework.Point(15, 10);
        var up = new Microsoft.Xna.Framework.Input.MouseState(15, 10, 0, Microsoft.Xna.Framework.Input.ButtonState.Released, default, default, default, default);
        var down = new Microsoft.Xna.Framework.Input.MouseState(15, 10, 0, Microsoft.Xna.Framework.Input.ButtonState.Pressed, default, default, default, default);
        screen.Mouse(host, at, down, up);
        screen.Mouse(host, at, up, down);
        Assert.True(host.IsVaccinationMode);
        screen.Mouse(host, at, down, up);
        screen.Mouse(host, at, up, down);
        Assert.False(host.IsVaccinationMode);
    }
}
