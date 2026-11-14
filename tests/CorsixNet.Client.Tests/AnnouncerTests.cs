using CorsixNet.Client.Audio;
using CorsixNet.Sim.Calendar;

namespace CorsixNet.Client.Tests;

/// <summary>The announcer (CorsixTH's announcer.lua).</summary>
public class AnnouncerTests
{
    [Fact]
    public void TheMostUrgentPlaysFirstOneAtATimeAndDuplicatesAreNotQueuedTwice()
    {
        var announcer = new Announcer(new Random(1));
        var played = new List<string>();
        TimeSpan Play(string name)
        {
            played.Add(name);
            return TimeSpan.FromSeconds(2);
        }

        announcer.Request("low.wav", AnnouncementPriority.Low, 0, staffedDesk: true);
        announcer.Request("high.wav", AnnouncementPriority.High, 0, staffedDesk: true);
        announcer.Request("high.wav", AnnouncementPriority.High, 0, staffedDesk: true);
        Assert.Equal(2, announcer.Waiting);

        announcer.Update(0, 0, paused: false, staffedDesk: true, enabled: true, Play);
        Assert.Equal(["high.wav"], played);
        announcer.Update(1000, 0, false, true, true, Play);
        Assert.Equal(["high.wav"], played);
        announcer.Update(2000, 0, false, true, true, Play);
        Assert.Equal(["high.wav", "low.wav"], played);
    }

    [Fact]
    public void WithoutAStaffedDeskOrWhilePausedOnlyCriticalOnesAreHeardAndOldOnesExpire()
    {
        var announcer = new Announcer(new Random(1));
        var played = new List<string>();
        TimeSpan Play(string name)
        {
            played.Add(name);
            return TimeSpan.Zero;
        }

        announcer.Request("normal.wav", AnnouncementPriority.Normal, 0, staffedDesk: false);
        announcer.Request("critical.wav", AnnouncementPriority.Critical, 0, staffedDesk: false);
        Assert.Equal(1, announcer.Waiting);
        announcer.Update(0, 0, paused: true, staffedDesk: false, enabled: true, Play);
        Assert.Equal(["critical.wav"], played);

        // A normal announcement keeps a week.
        announcer.Request("normal.wav", AnnouncementPriority.Normal, 0, staffedDesk: true);
        announcer.Update(0, (7 * GameDate.HoursPerDay) + 1, paused: false, staffedDesk: true, enabled: true, Play);
        Assert.Equal(["critical.wav"], played);
        Assert.Equal(0, announcer.Waiting);
    }

    [Fact]
    public void AfterTwoMinutesOfSilenceARandomAnnouncementComes()
    {
        var announcer = new Announcer(new Random(1));
        var played = new List<string>();
        for (long now = 0; now <= 121_000; now += 1000)
        {
            announcer.Update(now, 0, paused: false, staffedDesk: true, enabled: true, name =>
            {
                played.Add(name);
                return TimeSpan.Zero;
            });
        }

        Assert.Equal(["rand*.wav"], played);
    }
}
