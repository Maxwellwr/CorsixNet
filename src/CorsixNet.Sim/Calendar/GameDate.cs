// The game calendar ported from CorsixTH CorsixTH/Lua/date.lua (class Date).
// Copyright (c) 2018 Pavel "sofo" Schoffer and the CorsixTH contributors. Licensed under the MIT license;
// see THIRD-PARTY-NOTICES.md.

namespace CorsixNet.Sim.Calendar;

/// <summary>
/// A moment of game time, as CorsixTH's <c>Date</c>: years of twelve months without leap years, days
/// of <see cref="HoursPerDay"/> hours. The game starts at hour 0 of 1 January of year 1.
/// </summary>
/// <param name="Year">The year, from 1.</param>
/// <param name="Month">The month of the year, 1 to 12.</param>
/// <param name="Day">The day of the month, from 1.</param>
/// <param name="Hour">The hour of the day, 0 to <see cref="HoursPerDay"/> − 1.</param>
public readonly record struct GameDate(int Year, int Month, int Day, int Hour)
{
    /// <summary>Hours in a game day; a simulation tick is one hour.</summary>
    public const int HoursPerDay = 50;

    /// <summary>Days in a game year; Theme Hospital has no leap years.</summary>
    public const int DaysPerYear = 365;

    private static readonly int[] MonthLengths = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

    /// <summary>The first hour of the game.</summary>
    public static GameDate Start => new(1, 1, 1, 0);

    /// <summary>Days in <see cref="Month"/>.</summary>
    public int DaysInMonth => DaysIn(Month);

    /// <summary>
    /// Months started since the game began: 1 in the first January, 13 in the second, as CorsixTH's
    /// <c>Date:monthOfGame</c>.
    /// </summary>
    public int MonthOfGame => ((Year - 1) * 12) + Month;

    /// <summary>Whether this is the last day of its month.</summary>
    public bool IsLastDayOfMonth => Day == DaysInMonth;

    /// <summary>Whether this is 31 December.</summary>
    public bool IsLastDayOfYear => Month == 12 && IsLastDayOfMonth;

    /// <summary>Days in a month (1 to 12).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The month is not 1 to 12.</exception>
    public static int DaysIn(int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        return MonthLengths[month - 1];
    }

    /// <summary>The date <paramref name="hours"/> game hours after <see cref="Start"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The hours are negative.</exception>
    public static GameDate FromHours(long hours)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hours);
        var days = hours / HoursPerDay;
        var hour = (int)(hours % HoursPerDay);
        var year = (int)(days / DaysPerYear) + 1;
        var dayOfYear = (int)(days % DaysPerYear);
        var month = 1;
        while (dayOfYear >= DaysIn(month))
        {
            dayOfYear -= DaysIn(month);
            month++;
        }

        return new GameDate(year, month, dayOfYear + 1, hour);
    }

    /// <summary>Game hours from <see cref="Start"/> to this date.</summary>
    public long ToHours()
    {
        long days = ((long)(Year - 1) * DaysPerYear) + Day - 1;
        for (var month = 1; month < Month; month++)
        {
            days += DaysIn(month);
        }

        return (days * HoursPerDay) + Hour;
    }

    /// <summary>The date as CorsixTH writes it, <c>y-mm-ddThh</c>.</summary>
    public override string ToString() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Year}-{Month:00}-{Day:00}T{Hour:00}");
}
