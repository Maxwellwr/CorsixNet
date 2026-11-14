using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim.Economy;
using Microsoft.Xna.Framework;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class AnnualReportScreenTests
{
    [Fact]
    public void TrophiesStandApartFromAwardsAndPenalties()
    {
        Assert.True(AnnualReportScreen.IsTrophy(YearEndAwardKind.DrinksSoldTrophy));
        Assert.True(AnnualReportScreen.IsTrophy(YearEndAwardKind.HappyVips));
        Assert.False(AnnualReportScreen.IsTrophy(YearEndAwardKind.PoorReputation));
        Assert.Equal((27, 26), AnnualReportScreen.Text(YearEndAwardKind.DrinksSoldTrophy));
        Assert.Equal((56, 2), AnnualReportScreen.Text(YearEndAwardKind.PoorReputation));
    }

    [Fact]
    public void AwardsHangThreeOnEachSide()
    {
        Assert.Equal(new Point(16, 74), AnnualReportScreen.AwardPlace(0));
        Assert.Equal(new Point(16, 304), AnnualReportScreen.AwardPlace(2));
        Assert.Equal(new Point(525, 189), AnnualReportScreen.AwardPlace(4));
    }
}
