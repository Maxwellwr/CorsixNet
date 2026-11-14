// The announcer, ported from CorsixTH CorsixTH/Lua/announcer.lua (AnnouncementQueue, Announcer:playAnnouncement and
// onTick, the random announcements).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

namespace CorsixNet.Client.Audio;

/// <summary>How urgent an announcement is (CorsixTH's <c>AnnouncementPriority</c>).</summary>
internal enum AnnouncementPriority
{
    Critical = 1,
    High = 2,
    Normal = 3,
    Low = 4,
}

/// <summary>
/// Plays announcements one at a time, the most urgent first: a request for one already waiting only refreshes it;
/// waiting ones expire (High after 31 days, Normal 7, Low 3, Critical never); only Critical ones are queued without a
/// staffed reception desk or played while the game is paused; after 90 to 120 seconds without one, a random
/// announcement (rand*.wav) is queued.
/// </summary>
internal sealed class Announcer(Random random)
{
    private const int HoursPerDay = Sim.Calendar.GameDate.HoursPerDay;

    private readonly List<(string Name, AnnouncementPriority Priority, long Created, long Decay)> _queue = [];
    private long _playingUntil;
    private long _silentSince = -1;
    private long _randomAfter = RandomTarget(random);

    /// <summary>Waiting announcements, for tests.</summary>
    public int Waiting => _queue.Count;

    /// <summary>
    /// Announcer:playAnnouncement: queues an announcement at the game's hour <paramref name="hours"/>, unless the same is
    /// waiting already (then it is refreshed), or there is no staffed reception desk and it is not critical.
    /// </summary>
    public void Request(string name, AnnouncementPriority priority, long hours, bool staffedDesk)
    {
        var index = _queue.FindIndex(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _queue[index] = _queue[index] with { Created = hours };
            return;
        }

        if (staffedDesk || priority == AnnouncementPriority.Critical)
        {
            long decay = priority switch
            {
                AnnouncementPriority.Critical => -1,
                AnnouncementPriority.High => 31 * HoursPerDay,
                AnnouncementPriority.Normal => 7 * HoursPerDay,
                _ => 3 * HoursPerDay,
            };
            _queue.Add((name, priority, hours, decay));
        }
    }

    /// <summary>
    /// Announcer:onTick, at <paramref name="now"/> (milliseconds) and the game's hour <paramref name="hours"/>:
    /// <paramref name="play"/> plays one and says how long it lasts; with announcements off the queue just drains.
    /// </summary>
    public void Update(long now, long hours, bool paused, bool staffedDesk, bool enabled, Func<string, TimeSpan> play)
    {
        if (!paused)
        {
            // The random announcements count real time without one, as CorsixTH's ticks of the SDL clock.
            _silentSince = _silentSince < 0 ? now : _silentSince;
            if (now - _silentSince >= _randomAfter)
            {
                Request("rand*.wav", AnnouncementPriority.Low, hours, staffedDesk);
                _randomAfter = RandomTarget(random);
                _silentSince = now;
            }
        }

        var critical = _queue.Any(entry => entry.Priority == AnnouncementPriority.Critical);
        if ((paused && !critical) || (!paused && !staffedDesk && !critical))
        {
            return;
        }

        while (now >= _playingUntil && _queue.Count > 0)
        {
            var next = _queue.OrderBy(entry => entry.Priority).First();
            if (paused && next.Priority != AnnouncementPriority.Critical)
            {
                return;
            }

            _queue.Remove(next);
            if (next.Decay >= 0 && hours > next.Created + next.Decay)
            {
                continue;
            }

            if (enabled)
            {
                _playingUntil = now + (long)play(next.Name).TotalMilliseconds;
                _silentSince = now;
            }
        }
    }

    /// <summary>90 to 120 seconds (_setRandomAnnouncementTarget's 5000 to 6667 ticks of 18 ms).</summary>
    private static long RandomTarget(Random random) => random.Next(5000, 6668) * 18L;
}
