using CorsixNet.Client.Ui.Windows;
using Microsoft.Xna.Framework;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class PatientScreenTests
{
    [Fact]
    public void TheViewCircleAndTheHealthGraphAreWhereCorsixThHasThem()
    {
        Assert.True(PatientScreen.InViewCircle(new Point(55, 254)));
        Assert.False(PatientScreen.InViewCircle(new Point(55 + 39, 254)));
        var points = PatientScreen.GraphPoints([1m, 0.5m], 20);
        Assert.Equal(new Vector2(58 + (18 * 3.8f), 27), points[0]);
        Assert.Equal(new Vector2(58 + (19 * 3.8f), 62), points[1]);
    }

    [Fact]
    public void WithoutTheGraphicsThePatientWindowIsATable() => Assert.False(new PatientWindow(1).IsFullscreen(new WindowTestHost()));
}
