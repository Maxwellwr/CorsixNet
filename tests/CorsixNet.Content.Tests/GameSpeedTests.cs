using CorsixNet.Content.Interface;

namespace CorsixNet.Content.Tests;

public sealed class GameSpeedTests
{
    [Theory]
    [InlineData(GameSpeed.Pause, 0, 1)]
    [InlineData(GameSpeed.Slowest, 1, 56)]
    [InlineData(GameSpeed.Slower, 1, 12)]
    [InlineData(GameSpeed.Normal, 1, 3)]
    [InlineData(GameSpeed.MaxSpeed, 1, 2)]
    [InlineData(GameSpeed.AndThenSomeMore, 1, 1)]
    [InlineData(GameSpeed.Insane, 7, 1)]
    [InlineData(GameSpeed.SpeedUp, 8, 1)]
    public void Rates_match_corsixth_tick_rates(GameSpeed speed, int hours, int frames)
    {
        Assert.Equal((hours, frames), GameSpeeds.Rate(speed));
    }

    [Fact]
    public void Insane_is_21_times_normal()
    {
        var normal = new GameClock(GameSpeed.Normal);
        var insane = new GameClock(GameSpeed.Insane);
        var second = TimeSpan.FromMilliseconds(18 * 3 * 10);

        Assert.Equal(10, normal.Advance(second));
        Assert.Equal(210, Enumerable.Range(0, 3).Sum(_ => insane.Advance(second / 3)));
    }

    [Fact]
    public void Insane_runs_seven_ticks_a_frame()
    {
        var clock = new GameClock(GameSpeed.Insane);

        Assert.Equal(7, clock.Advance(GameSpeeds.Frame));
        Assert.Equal(0, clock.Advance(GameSpeeds.Frame / 2));
        Assert.Equal(7, clock.Advance(GameSpeeds.Frame / 2));
    }

    [Fact]
    public void Insane_is_a_speed_that_pause_and_speed_up_return_to()
    {
        var clock = new GameClock(GameSpeed.Normal);
        clock.SetSpeed(GameSpeed.Insane);
        clock.SetSpeed(GameSpeed.SpeedUp);
        clock.Resume();
        Assert.Equal(GameSpeed.Insane, clock.Speed);

        clock.SetSpeed(GameSpeed.Pause);
        clock.Resume();
        Assert.Equal(GameSpeed.Insane, clock.Speed);
    }

    [Fact]
    public void Every_speed_has_a_name()
    {
        Assert.Equal("Insane", GameSpeeds.Name(GameSpeed.Insane));
        Assert.All(Enum.GetValues<GameSpeed>(), speed => Assert.False(string.IsNullOrEmpty(GameSpeeds.Name(speed))));
    }
}
