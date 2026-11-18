using OpenWards.Client.Ui.Windows;
using Xunit;

namespace OpenWards.Client.Tests;

public sealed class HireScreenTests
{
    [Fact]
    public void WithoutTheGraphicsTheHireWindowIsATableWithDoctorsChosen()
    {
        var window = new HireWindow();
        var host = new WindowTestHost();
        Assert.False(window.IsFullscreen(host));
        Assert.Equal(OpenWards.Sim.Staff.StaffClass.Doctor, window.Category);
        Assert.Equal(new Microsoft.Xna.Framework.Point(242, 323), HireScreen.Size);
    }
}
