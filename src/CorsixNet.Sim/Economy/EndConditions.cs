// Winning and losing a level, ported from CorsixTH CorsixTH/Lua/endconditions.lua (EndConditions), with the
// percentages from CorsixTH/Lua/hospital.lua (Hospital:updatePercentages) and the averages of
// Hospital:_getAverageHumanoidsAttribute.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Economy;

/// <summary>What a win or lose goal measures; the numbers are the level files' <c>Criteria</c>.</summary>
public enum EndCriterion : byte
{
    Reputation = 1,
    Balance = 2,
    PercentageCured = 3,
    NumCured = 4,
    PercentageKilled = 5,

    /// <summary>The hospital's value.</summary>
    Value = 6,

    /// <summary>CorsixTH's own criterion: the staff's mean happiness in percent.</summary>
    StaffHappiness = 7,

    /// <summary>CorsixTH's own criterion: the patients' mean happiness in percent.</summary>
    PatientHappiness = 8,

    /// <summary>CorsixTH's own criterion: months played.</summary>
    MonthsPlayed = 9,
}

/// <summary>Whether a hospital has won or lost its level.</summary>
public enum EndOutcome : byte
{
    None,
    Won,
    Lost,
}

/// <summary>
/// One goal of a level: a hospital meets a win goal when the measure reaches <paramref name="Value"/> (at least it with
/// <paramref name="AtLeast"/>, else at most) and a lose goal when it passes it (more with <paramref name="AtLeast"/>,
/// else less). A hospital wins or loses when it meets every goal of one group.
/// </summary>
/// <param name="Criterion">What is measured.</param>
/// <param name="Group">The group (CorsixTH's <c>Group</c>).</param>
/// <param name="AtLeast">CorsixTH's <c>MaxMin</c> 1; otherwise 0.</param>
/// <param name="Value">The level to reach or pass.</param>
/// <param name="Bound">For a lose goal, where the progress report starts to warn (<c>Bound</c>).</param>
/// <param name="Start">The measure at the start: the starting cash or reputation, else 0.</param>
public readonly record struct EndGoal(EndCriterion Criterion, int Group, bool AtLeast, int Value, int Bound, int Start);

/// <summary>A goal on the progress report, with how close a lose goal is (0 at its bound, 1 when lost).</summary>
public readonly record struct ProgressEntry(EndGoal Goal, bool IsLoseGoal, decimal? Progress);

/// <summary>The level's goals to win and to lose by (CorsixTH's <c>EndConditions</c>).</summary>
public sealed class EndConditions
{
    /// <summary>No goals: the level can be neither won nor lost (free build, or a level without criteria).</summary>
    public static readonly EndConditions None = new([], []);

    private const int MaxGoals = 256;
    private const int MinPatientsCounted = 4;
    private const decimal DefaultHappiness = 0.5m;

    /// <param name="win">Win goals; one per criterion and group, a later one replacing an earlier one.</param>
    /// <param name="lose">Lose goals, likewise.</param>
    public EndConditions(IEnumerable<EndGoal> win, IEnumerable<EndGoal> lose)
    {
        WinGoals = Distinct(win);
        LoseGoals = Distinct(lose);
    }

    /// <summary>The win goals, by group and criterion.</summary>
    public IReadOnlyList<EndGoal> WinGoals { get; }

    /// <summary>The lose goals, by group and criterion.</summary>
    public IReadOnlyList<EndGoal> LoseGoals { get; }

    /// <summary>
    /// <c>EndConditions:checkEndGame</c>: a hospital that meets every goal of a win group and owes the bank nothing
    /// wins; otherwise one that meets every goal of a lose group loses to
    /// the last of them, by criterion number.
    /// </summary>
    public (EndOutcome Outcome, EndGoal? LostTo) Check(Simulation simulation, Hospital hospital)
    {
        foreach (var group in Groups(WinGoals))
        {
            if (WinScore(simulation, hospital, group) == 1 && hospital.Loan == 0)
            {
                return (EndOutcome.Won, null);
            }
        }

        foreach (var group in Groups(LoseGoals))
        {
            EndGoal? lostTo = null;
            var met = 0;
            foreach (var goal in group)
            {
                if (Measure(simulation, hospital, goal.Criterion) is { } measure && (measure - goal.Value) * Sign(goal) > 0)
                {
                    lostTo = goal;
                    met++;
                }
            }

            if (met == group.Count)
            {
                return (EndOutcome.Lost, lostTo);
            }
        }

        return (EndOutcome.None, null);
    }

    /// <summary>
    /// <c>EndConditions:generateReportTable</c>: up to five lose goals past their bound, those closest to being lost
    /// first, filled up with the goals of the win group the hospital has met most of, ordered by criterion.
    /// </summary>
    public IReadOnlyList<ProgressEntry> ProgressReport(Simulation simulation, Hospital hospital)
    {
        // The lose goal past its bound closest to being lost, for each criterion.
        var closest = new SortedDictionary<EndCriterion, ProgressEntry>();
        foreach (var goal in LoseGoals)
        {
            if (Measure(simulation, hospital, goal.Criterion) is not { } measure || (measure - goal.Bound) * Sign(goal) <= 0)
            {
                continue;
            }

            // CorsixTH divides by zero when the bound is the lose value; such a goal is as good as lost.
            var progress = goal.Bound == goal.Value ? 1 : 1 - ((measure - goal.Value) / (goal.Bound - goal.Value));
            if (!closest.TryGetValue(goal.Criterion, out var known) || progress > known.Progress)
            {
                closest[goal.Criterion] = new ProgressEntry(goal, IsLoseGoal: true, progress);
            }
        }

        var report = closest.Values.ToList();
        if (report.Count > 5)
        {
            report = [.. report.OrderByDescending(entry => entry.Progress).Take(5)];
        }

        var best = BestWinGroup(simulation, hospital);
        foreach (var goal in WinGoals.Where(goal => goal.Group == best).OrderBy(goal => goal.Criterion))
        {
            if (report.Count == 5)
            {
                break;
            }

            if (report.All(entry => entry.Goal.Criterion != goal.Criterion))
            {
                report.Add(new ProgressEntry(goal, IsLoseGoal: false, null));
            }
        }

        return [.. report.OrderBy(entry => entry.Goal.Criterion)];
    }

    /// <summary>
    /// What a criterion measures for a hospital now (<c>EndConditions:getAttribute</c>); null for CorsixTH's killed
    /// percentage while patients have gone home uncured but none was cured or died, which it computes as 0 / 0 and
    /// which then meets no goal.
    /// </summary>
    public static decimal? Measure(Simulation simulation, Hospital hospital, EndCriterion criterion)
    {
        return criterion switch
        {
            EndCriterion.Reputation => hospital.Reputation,
            EndCriterion.Balance => hospital.Balance,
            EndCriterion.PercentageCured => hospital.PercentageCured,
            EndCriterion.NumCured => hospital.Cured,
            EndCriterion.PercentageKilled => hospital.PercentageKilled,
            EndCriterion.Value => hospital.Value,
            EndCriterion.StaffHappiness => 100 * AverageHappiness(simulation, hospital, staff: true, minCount: 1),
            EndCriterion.PatientHappiness => 100 * AverageHappiness(simulation, hospital, staff: false, MinPatientsCounted),
            EndCriterion.MonthsPlayed => GameDate.FromHours(simulation.Hours + 1).MonthOfGame,
            _ => null,
        };
    }

    internal void Write(SimWriter writer)
    {
        WriteGoals(writer, WinGoals);
        WriteGoals(writer, LoseGoals);
    }

    internal static EndConditions Read(SimReader reader)
    {
        var win = ReadGoals(reader);
        return new EndConditions(win, ReadGoals(reader));
    }

    private static int Sign(EndGoal goal) => goal.AtLeast ? 1 : -1;

    /// <summary><c>EndConditions:_checkWinGroup</c>: the share of the group's goals the hospital meets.</summary>
    private static decimal WinScore(Simulation simulation, Hospital hospital, IReadOnlyList<EndGoal> group)
    {
        var met = group.Count(goal => Measure(simulation, hospital, goal.Criterion) is { } measure && (measure - goal.Value) * Sign(goal) >= 0);
        return met / (decimal)group.Count;
    }

    /// <summary>
    /// <c>EndConditions:_findBestWinGroup</c>: the win group with the best score, the first if none is met at all.
    /// CorsixTH never raises the best score as it goes, so it picks the last group with any goal met; this picks the
    /// best.
    /// </summary>
    private int BestWinGroup(Simulation simulation, Hospital hospital)
    {
        var best = 1;
        var score = 0m;
        foreach (var group in Groups(WinGoals))
        {
            var test = WinScore(simulation, hospital, group);
            if (test > score)
            {
                score = test;
                best = group[0].Group;
            }
        }

        return best;
    }

    /// <summary>
    /// <c>Hospital:getAverageStaffAttribute</c> and <c>getAveragePatientAttribute</c> for happiness: the mean over the
    /// hospital's staff or patients on its land, made up to <paramref name="minCount"/> with the default of 0.5.
    /// </summary>
    private static decimal AverageHappiness(Simulation simulation, Hospital hospital, bool staff, int minCount)
    {
        var sum = 0m;
        var count = 0;
        foreach (var entity in simulation.Entities)
        {
            if (entity is not Humanoid humanoid)
            {
                continue;
            }

            var counts = staff
                ? humanoid.Profile is not null && humanoid.Employer == hospital.Player && !humanoid.IsFired
                : humanoid.Behaviour is PatientBehaviour && humanoid.HospitalPlayer == hospital.Player;
            if (counts && IsInHospital(simulation, hospital, humanoid.Tile))
            {
                sum += humanoid.Happiness;
                count++;
            }
        }

        while (count < minCount)
        {
            sum += DefaultHappiness;
            count++;
        }

        return sum / count;
    }

    /// <summary><c>Hospital:isInHospital</c>: a hospital tile on land the hospital owns.</summary>
    private static bool IsInHospital(Simulation simulation, Hospital hospital, TilePosition position)
    {
        var map = simulation.Map;
        if (!map.Contains(position.X, position.Y))
        {
            return false;
        }

        ref var tile = ref map[position.X, position.Y];
        return tile.Has(TileFlags.Hospital) && tile.Parcel < map.ParcelCount && map.GetParcelOwner(tile.Parcel) == hospital.Player + 1;
    }

    private static List<List<EndGoal>> Groups(IReadOnlyList<EndGoal> goals)
    {
        var groups = new List<List<EndGoal>>();
        foreach (var goal in goals)
        {
            if (groups.Count == 0 || groups[^1][0].Group != goal.Group)
            {
                groups.Add([]);
            }

            groups[^1].Add(goal);
        }

        return groups;
    }

    private static EndGoal[] Distinct(IEnumerable<EndGoal> goals)
    {
        var byKey = new SortedDictionary<(int Group, EndCriterion Criterion), EndGoal>();
        foreach (var goal in goals)
        {
            if (!Enum.IsDefined(goal.Criterion))
            {
                continue;
            }

            byKey[(goal.Group, goal.Criterion)] = goal;
        }

        return [.. byKey.Values];
    }

    private static void WriteGoals(SimWriter writer, IReadOnlyList<EndGoal> goals)
    {
        writer.WriteInt32(goals.Count);
        foreach (var goal in goals)
        {
            writer.WriteByte((byte)goal.Criterion);
            writer.WriteInt32(goal.Group);
            writer.WriteBool(goal.AtLeast);
            writer.WriteInt32(goal.Value);
            writer.WriteInt32(goal.Bound);
            writer.WriteInt32(goal.Start);
        }
    }

    private static List<EndGoal> ReadGoals(SimReader reader)
    {
        var count = reader.ReadCount(MaxGoals);
        var goals = new List<EndGoal>(count);
        for (var i = 0; i < count; i++)
        {
            goals.Add(new EndGoal((EndCriterion)reader.ReadByte(), reader.ReadInt32(), reader.ReadBool(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
        }

        return goals;
    }
}
