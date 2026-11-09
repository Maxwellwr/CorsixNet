// Game speeds ported from CorsixTH CorsixTH/Lua/world.lua (tick_rates, World:setSpeed, World:previousSpeed).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

namespace CorsixNet.Content.Interface;

/// <summary>The game speeds of CorsixTH, slowest first, plus CorsixNet's <see cref="Insane"/>.</summary>
public enum GameSpeed
{
    Pause,
    Slowest,
    Slower,
    Normal,
    MaxSpeed,
    AndThenSomeMore,

    /// <summary>Not in CorsixTH: seven hours every frame, 21 times Normal.</summary>
    Insane,

    /// <summary>Held down with a key: eight hours each tick, then back to the previous speed.</summary>
    SpeedUp,
}

/// <summary>How each <see cref="GameSpeed"/> turns real time into simulation ticks.</summary>
public static class GameSpeeds
{
    /// <summary>CorsixTH's world clock: a frame every 18 ms.</summary>
    public static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(18);

    /// <summary>
    /// Game hours (simulation ticks) that pass at once, and how many frames apart, as CorsixTH's
    /// <c>tick_rates</c>: Normal is one hour every 3 frames (54 ms). Insane is CorsixNet's own.
    /// </summary>
    public static (int Hours, int Frames) Rate(GameSpeed speed) => speed switch
    {
        GameSpeed.Pause => (0, 1),
        GameSpeed.Slowest => (1, 56),
        GameSpeed.Slower => (1, 12),
        GameSpeed.Normal => (1, 3),
        GameSpeed.MaxSpeed => (1, 2),
        GameSpeed.AndThenSomeMore => (1, 1),
        GameSpeed.Insane => (7, 1),
        GameSpeed.SpeedUp => (8, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(speed), speed, null),
    };

    /// <summary>Real time between two batches of hours at <paramref name="speed"/>.</summary>
    public static TimeSpan Interval(GameSpeed speed) => Frame * Rate(speed).Frames;

    /// <summary>The name CorsixTH's speed menu uses.</summary>
    public static string Name(GameSpeed speed) => speed switch
    {
        GameSpeed.Pause => "Pause",
        GameSpeed.Slowest => "Slowest",
        GameSpeed.Slower => "Slower",
        GameSpeed.Normal => "Normal",
        GameSpeed.MaxSpeed => "Max speed",
        GameSpeed.AndThenSomeMore => "And then some more",
        GameSpeed.Insane => "Insane",
        GameSpeed.SpeedUp => "Speed up",
        _ => throw new ArgumentOutOfRangeException(nameof(speed), speed, null),
    };
}

/// <summary>
/// The current speed and the clock that turns real time into ticks, with CorsixTH's rules for
/// switching: pause and speed-up are passing states that remember the speed to go back to, speed-up
/// does nothing while paused, and the time towards the next tick carries over in proportion.
/// </summary>
public sealed class GameClock
{
    /// <summary>Upper bound on ticks per update, so a long stall does not freeze the client.</summary>
    public const int MaxTicksPerUpdate = 80;

    private TimeSpan _untilNext;

    public GameClock(GameSpeed speed = GameSpeed.Normal)
    {
        Speed = speed;
        PreviousSpeed = speed is GameSpeed.Pause or GameSpeed.SpeedUp ? GameSpeed.Normal : speed;
        _untilNext = GameSpeeds.Interval(speed);
    }

    public GameSpeed Speed { get; private set; }

    /// <summary>The last speed other than pause and speed-up: where those go back to.</summary>
    public GameSpeed PreviousSpeed { get; private set; }

    public bool IsPaused => Speed == GameSpeed.Pause;

    /// <summary>Changes the speed, as CorsixTH's <c>World:setSpeed</c>.</summary>
    public void SetSpeed(GameSpeed speed)
    {
        if (speed == Speed || (speed == GameSpeed.SpeedUp && IsPaused))
        {
            return;
        }

        if (Speed is not (GameSpeed.Pause or GameSpeed.SpeedUp))
        {
            PreviousSpeed = Speed;
        }

        var oldInterval = GameSpeeds.Interval(Speed);
        var newInterval = GameSpeeds.Interval(speed);
        _untilNext = IsPaused ? newInterval : _untilNext / oldInterval * newInterval;
        Speed = speed;
    }

    /// <summary>Goes back to <see cref="PreviousSpeed"/>; used when pause or speed-up ends.</summary>
    public void Resume() => SetSpeed(PreviousSpeed);

    /// <summary>Lets <paramref name="elapsed"/> real time pass; returns how many ticks to run.</summary>
    public int Advance(TimeSpan elapsed)
    {
        var (hours, _) = GameSpeeds.Rate(Speed);
        if (hours == 0)
        {
            return 0;
        }

        var interval = GameSpeeds.Interval(Speed);
        var ticks = 0;
        _untilNext -= elapsed;
        while (_untilNext <= TimeSpan.Zero && ticks < MaxTicksPerUpdate)
        {
            ticks += hours;
            _untilNext += interval;
        }

        if (ticks >= MaxTicksPerUpdate)
        {
            _untilNext = interval;
        }

        return ticks;
    }
}
