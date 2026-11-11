// Earthquakes, ported from CorsixTH CorsixTH/Lua/earthquake.lua (Earthquake:tick, onEndDay, nextEarthquake,
// createEarthquake), the screen shake of hospitals/player_hospital.lua (PlayerHospital:tickEarthquake) and the
// earthquake cheats of cheats.lua (cheatEarthquake, cheatToggleEarthquake).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Disasters;

/// <summary>
/// One of the level's planned earthquakes (<c>quake_control[n]</c>): it starts on a random day of a random month of
/// the game from <see cref="StartMonth"/> to <see cref="EndMonth"/> and does <see cref="Severity"/> rounds of damage.
/// </summary>
/// <param name="StartMonth">The first month of the game it may start in, from 1.</param>
/// <param name="EndMonth">The last month of the game it may start in.</param>
/// <param name="Severity">Its size; 0 ends the level's earthquakes.</param>
public readonly record struct PlannedEarthquake(int StartMonth, int EndMonth, int Severity);

/// <summary>How hard the screen shakes during an earthquake (CorsixTH's <c>beginShakeScreen</c> intensities).</summary>
public enum EarthquakeShake : byte
{
    /// <summary>No shaking.</summary>
    None = 0,

    /// <summary>0.2: the start of the warning quake, which the same hour turns into <see cref="Small"/>.</summary>
    Warning = 1,

    /// <summary>0.5: the first and last two rounds of damage, and the warning quake.</summary>
    Small = 2,

    /// <summary>1: the rounds of damage in between.</summary>
    Large = 3,
}

/// <summary>
/// The world's earthquakes, as CorsixTH's <c>Earthquake</c>: the next one is planned from the level's
/// <see cref="PlannedEarthquake"/> entries in turn; on its day a short warning quake comes, then 600 hours later the
/// main quake, which does a round of damage every 16 hours until its size is used up.
/// </summary>
/// <remarks>
/// A round of damage wears the machines in every room (<c>Machine:earthquakeImpact</c>, done by the simulation when
/// <see cref="Tick"/> says so). CorsixTH also knocks over patients in the corridors; the falling animation is not ported.
/// </remarks>
public sealed class Earthquakes
{
    /// <summary>Hours between rounds of damage (CorsixTH's <c>damage_time</c>).</summary>
    public const int DamageTime = 16;

    /// <summary>Hours from the warning quake to the main one (<c>warning_period</c>).</summary>
    public const int WarningPeriod = 600;

    /// <summary>How long the warning quake lasts, in hours (<c>warning_length</c>).</summary>
    public const int WarningLength = 25;

    /// <summary>The largest size the cheat makes when no earthquake is planned (CorsixTH: "above 6 seems disastrous").</summary>
    private const int MaxCheatSize = 6;

    /// <summary>Whether an earthquake is under way: from its day until its last round of damage.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Whether the cheat has turned earthquakes off.</summary>
    public bool IsDisabled { get; private set; }

    /// <summary>Whether the next earthquake has been planned and its warning has not come yet.</summary>
    public bool IsNextPlanned { get; private set; }

    /// <summary>The month of the game the next or current earthquake starts in, from 1.</summary>
    public int StartMonth { get; private set; }

    /// <summary>The day of that month.</summary>
    public int StartDay { get; private set; }

    /// <summary>The earthquake's size: how many rounds of damage it does.</summary>
    public int Size { get; private set; }

    /// <summary>The rounds of damage still to come.</summary>
    public int RemainingDamage { get; private set; }

    /// <summary>Hours to the next round of damage.</summary>
    public int DamageTimer { get; private set; }

    /// <summary>Hours to the main quake.</summary>
    public int WarningTimer { get; private set; }

    /// <summary>How many of the level's planned earthquakes have been used.</summary>
    public int PlannedSoFar { get; private set; }

    /// <summary>How hard the screen shakes now.</summary>
    public EarthquakeShake Shake { get; private set; }

    /// <summary>Starts the level's earthquakes: plans the first one (CorsixTH's <c>Earthquake:Earthquake</c>).</summary>
    internal static Earthquakes Start(IReadOnlyList<PlannedEarthquake> planned, SimRandom random)
    {
        var earthquakes = new Earthquakes();
        earthquakes.PlanNext(planned, random);
        return earthquakes;
    }

    /// <summary>One hour of an earthquake (<c>Earthquake:tick</c>). True when this hour does a round of damage.</summary>
    internal bool Tick(IReadOnlyList<PlannedEarthquake> planned, SimRandom random)
    {
        if (!IsActive || IsDisabled)
        {
            return false;
        }

        if (RemainingDamage == 0)
        {
            IsActive = false;
            Shake = EarthquakeShake.None;
            PlanNext(planned, random);
            return false;
        }

        if (WarningTimer == WarningPeriod)
        {
            IsNextPlanned = false;
            Shake = EarthquakeShake.Warning;
        }

        // The end of the warning quake.
        if (WarningTimer >= WarningPeriod - WarningLength && WarningTimer - 1 < WarningPeriod - WarningLength)
        {
            Shake = EarthquakeShake.None;
        }

        if (WarningTimer > 0)
        {
            WarningTimer--;

            // Nothing more happens between the warning quake and the main one. CorsixTH's "main_start" announcement
            // comes after this test and so never plays: the hour the timer reaches 0 returns here too.
            if (WarningTimer < WarningPeriod - WarningLength)
            {
                return false;
            }
        }

        // Earthquakes start and end small: gently while at most two rounds have been done or are left.
        Shake = RemainingDamage <= 2 || Size - RemainingDamage <= 2 ? EarthquakeShake.Small : EarthquakeShake.Large;

        if (WarningTimer > 0)
        {
            return false;
        }

        DamageTimer--;
        if (DamageTimer > 0)
        {
            return false;
        }

        RemainingDamage--;
        DamageTimer += DamageTime;
        return true;
    }

    /// <summary>Starts the planned earthquake on its day (<c>Earthquake:onEndDay</c>).</summary>
    internal void OnEndDay(GameDate date)
    {
        if (date.MonthOfGame == StartMonth && date.Day == StartDay && !IsDisabled)
        {
            IsActive = true;
        }
    }

    /// <summary>
    /// The cheat's earthquake (<c>Earthquake:createEarthquake</c>): the planned one comes today, or a new one of size
    /// 1 to 6 if none is planned. False if one is under way or earthquakes are off.
    /// </summary>
    internal bool Create(GameDate date, SimRandom random)
    {
        if (IsActive || IsDisabled)
        {
            return false;
        }

        StartDay = date.Day;
        StartMonth = date.MonthOfGame;
        if (!IsNextPlanned)
        {
            Size = random.NextInclusive(1, MaxCheatSize);
            RemainingDamage = Size;
            DamageTimer = DamageTime;
            WarningTimer = WarningPeriod;
        }

        return true;
    }

    /// <summary>
    /// Turns earthquakes off or back on (<c>cheatToggleEarthquake</c>). Unlike CorsixTH, turning them off mid-quake
    /// stops the screen shaking, which would otherwise go on until they are turned on again.
    /// </summary>
    internal void ToggleDisabled()
    {
        IsDisabled = !IsDisabled;
        if (IsDisabled)
        {
            Shake = EarthquakeShake.None;
        }
    }

    /// <summary>
    /// Plans the level's next earthquake, if it has one (<c>Earthquake:nextEarthquake</c>).
    /// </summary>
    /// <remarks>
    /// CorsixTH draws the day from the length of the month after the start month (<c>month_length[(start_month % 12) + 1]</c>),
    /// so a day 31 drawn before a 30-day month never comes and the level's earthquakes stop. This draws it from the
    /// start month's own length. An entry whose months are the wrong way round, an error in Lua, starts in its
    /// <see cref="PlannedEarthquake.StartMonth"/>.
    /// </remarks>
    private void PlanNext(IReadOnlyList<PlannedEarthquake> planned, SimRandom random)
    {
        IsActive = false;
        if (PlannedSoFar >= planned.Count || planned[PlannedSoFar].Severity == 0)
        {
            return;
        }

        var control = planned[PlannedSoFar];
        StartMonth = random.NextInclusive(control.StartMonth, Math.Max(control.StartMonth, control.EndMonth));
        StartDay = random.NextInclusive(1, GameDate.DaysIn((((StartMonth - 1) % 12) + 12) % 12 + 1));
        Size = control.Severity;
        RemainingDamage = Size;
        DamageTimer = DamageTime;
        WarningTimer = WarningPeriod;
        PlannedSoFar++;
        IsNextPlanned = true;
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteBool(IsActive);
        writer.WriteBool(IsDisabled);
        writer.WriteBool(IsNextPlanned);
        writer.WriteInt32(StartMonth);
        writer.WriteInt32(StartDay);
        writer.WriteInt32(Size);
        writer.WriteInt32(RemainingDamage);
        writer.WriteInt32(DamageTimer);
        writer.WriteInt32(WarningTimer);
        writer.WriteInt32(PlannedSoFar);
        writer.WriteByte((byte)Shake);
    }

    internal static Earthquakes Read(SimReader reader)
    {
        var earthquakes = new Earthquakes
        {
            IsActive = reader.ReadBool(),
            IsDisabled = reader.ReadBool(),
            IsNextPlanned = reader.ReadBool(),
            StartMonth = reader.ReadInt32(),
            StartDay = reader.ReadInt32(),
            Size = reader.ReadInt32(),
            RemainingDamage = reader.ReadInt32(),
            DamageTimer = reader.ReadInt32(),
            WarningTimer = reader.ReadInt32(),
            PlannedSoFar = reader.ReadInt32(),
        };
        var shake = reader.ReadByte();
        earthquakes.Shake = shake <= (byte)EarthquakeShake.Large ? (EarthquakeShake)shake : throw new InvalidDataException($"Unknown earthquake shake {shake}.");
        return earthquakes;
    }
}
