using CorsixNet.Sim.Calendar;

namespace CorsixNet.Sim.Tests;

/// <summary>The game calendar.</summary>
public class GameDateTests
{
    [Theory]
    [InlineData(0, 1, 1, 1, 0)]
    [InlineData(49, 1, 1, 1, 49)]
    [InlineData(50, 1, 1, 2, 0)]
    [InlineData(31 * 50, 1, 2, 1, 0)] // January has 31 days
    [InlineData((31 + 28) * 50, 1, 3, 1, 0)] // no leap years
    [InlineData((365 * 50) - 1, 1, 12, 31, 49)]
    [InlineData(365 * 50, 2, 1, 1, 0)]
    public void HoursCountFromTheFirstOfJanuaryOfYearOne(long hours, int year, int month, int day, int hour)
    {
        var date = GameDate.FromHours(hours);

        Assert.Equal(new GameDate(year, month, day, hour), date);
        Assert.Equal(hours, date.ToHours());
    }

    [Fact]
    public void EveryHourOfTwoYearsSurvivesTheRoundTrip()
    {
        var previous = GameDate.FromHours(0);
        Assert.Equal(GameDate.Start, previous);
        for (long hours = 1; hours < 2 * 365 * 50; hours++)
        {
            var date = GameDate.FromHours(hours);
            Assert.Equal(hours, date.ToHours());
            Assert.InRange(date.Day, 1, date.DaysInMonth);
            Assert.True(date.Hour == previous.Hour + 1 || (date.Hour == 0 && previous.Hour == GameDate.HoursPerDay - 1));
            previous = date;
        }
    }

    [Fact]
    public void MonthOfGameCountsMonthsStarted()
    {
        Assert.Equal(1, GameDate.Start.MonthOfGame);
        Assert.Equal(12, new GameDate(1, 12, 31, 49).MonthOfGame);
        Assert.Equal(14, new GameDate(2, 2, 1, 0).MonthOfGame);
    }

    [Fact]
    public void TheLastDaysOfMonthsAndYears()
    {
        Assert.True(new GameDate(1, 2, 28, 0).IsLastDayOfMonth);
        Assert.False(new GameDate(1, 2, 28, 0).IsLastDayOfYear);
        Assert.False(new GameDate(1, 4, 30, 0).IsLastDayOfYear);
        Assert.False(new GameDate(1, 12, 30, 0).IsLastDayOfMonth);
        Assert.True(new GameDate(3, 12, 31, 7).IsLastDayOfYear);
    }

    [Fact]
    public void TheSimulationDateFollowsItsTicks()
    {
        var simulation = new Simulation(TestMaps.FromRows("..", ".."), seed: 1);
        for (var i = 0; i < 75; i++)
        {
            simulation.Step();
        }

        Assert.Equal(new GameDate(1, 1, 2, 25), simulation.Date);
        Assert.Equal("1-01-02T25", simulation.Date.ToString());
    }
}
