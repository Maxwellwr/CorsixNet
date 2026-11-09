// Which level files are read, and where starting cash and costs come from, ported from CorsixTH
// CorsixTH/Lua/map.lua (Map:_loadOriginalCampaignLevel), CorsixTH/Lua/hospital.lua (Hospital:Hospital,
// Hospital:getObjectBuildCost) and CorsixTH/Lua/world.lua (World:getAvailableRooms); the fallback
// starting cash and reputation are CorsixTH/Lua/base_config.lua's town.StartCash and StartRep, and the staff settings fall back
// to the same file's staff, gbv and staff_levels. The planned earthquakes are CorsixTH/Lua/earthquake.lua's quake_control.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using System.Text.RegularExpressions;
using CorsixNet.Formats;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Disasters;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Content.Levels;

/// <summary>Builds a level's <see cref="LevelRules"/> from the original game's level files.</summary>
public static partial class LevelRulesLoader
{
    /// <summary>The original's medium difficulty, which CorsixTH's campaign uses by default.</summary>
    public const string DefaultDifficulty = "FULL";

    /// <summary>Starting cash when the level files give none (CorsixTH's base_config.lua).</summary>
    public const int DefaultStartCash = 40000;

    /// <summary>Number of levels in the original campaign; each has its own settings file.</summary>
    public const int CampaignLevels = 12;

    /// <summary>base_config.lua's <c>gbv.HoldVisualMonths</c>.</summary>
    private const int BaseHoldVisualMonths = 2;

    /// <summary>base_config.lua's <c>gbv.HoldVisualPeepCount</c>.</summary>
    private const int BaseHoldVisualPeepCount = 6;

    /// <summary>CorsixTH's base_config.lua: the names of the computer opponents 0 to 14.</summary>
    private static readonly string[] BaseCompetitorNames =
    [
        "ORAC", "COLOSSUS", "HAL", "MULTIVAC", "HOLLY", "DEEP THOUGHT", "ZEN", "SKYNET", "MARVIN", "CEREBRO", "MOTHER",
        "JAYNE", "CORSIX", "ROUJIN", "EDVIN",
    ];

    /// <summary>base_config.lua's <c>visuals_available</c>; its <c>visuals</c>, <c>non_visuals</c> and <c>non_visuals_available</c> are all 0.</summary>
    private static readonly int[] BaseVisualsAvailable = [0, 12, 3, 12, 18, 6, 0, 6, 12, 0, 18, 0, 0, 6];

    /// <summary>
    /// The level number of an original level map such as <c>LEVEL.L1</c>, or null for other maps.
    /// </summary>
    /// <remarks>
    /// Both '/' and '\' separate directories here, whatever the platform: <see cref="Path.GetFileName(string)"/>
    /// keeps a Windows path whole on Linux and macOS.
    /// </remarks>
    public static int? LevelNumber(string mapFileName)
    {
        var fileName = mapFileName[(mapFileName.LastIndexOfAny(['/', '\\']) + 1)..];
        var match = OriginalLevel().Match(fileName);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>
    /// Reads the settings of a level: the difficulty's base file (such as <c>FULL00.SAM</c>), then,
    /// for a campaign level, the level's own file (such as <c>FULL01.SAM</c>).
    /// </summary>
    public static LevelConfig LoadConfig(ThemeHospitalInstall install, int? levelNumber, string difficulty = DefaultDifficulty)
    {
        var config = LevelConfig.Parse(install.ReadDataFile("LEVELS", $"{difficulty}00.SAM"));
        if (levelNumber is >= 1 and <= CampaignLevels)
        {
            config.Apply(install.ReadDataFile("LEVELS", $"{difficulty}{levelNumber:00}.SAM"));
        }

        return config;
    }

    /// <summary>The money settings of level <paramref name="levelNumber"/> (if any) for the given content.</summary>
    public static LevelRules FromConfig(LevelConfig config, int? levelNumber, GameContent content)
    {
        // A level's own town settings win over the general ones.
        var startCash = (levelNumber is { } level ? config.GetInt($"towns[{level}].StartCash") : null)
            ?? config.GetInt("town.StartCash")
            ?? DefaultStartCash;
        var startReputation = (levelNumber is { } repLevel ? config.GetInt($"towns[{repLevel}].StartRep") : null)
            ?? config.GetInt("town.StartRep")
            ?? Hospital.AverageReputation;

        var rooms = content.Rooms
            .Where(room => room.LevelConfigId != 0 && config.GetInt($"rooms[{room.LevelConfigId}].Cost") is not null)
            .Select(room => KeyValuePair.Create(room.Id, config.GetInt($"rooms[{room.LevelConfigId}].Cost")!.Value));
        var objects = content.Objects
            .Where(definition => config.GetInt($"objects[{definition.ObjectType}].StartCost") is not null)
            .Select(definition => KeyValuePair.Create(definition.Id, config.GetInt($"objects[{definition.ObjectType}].StartCost")!.Value));
        return new LevelRules(startCash, rooms, objects, StaffFromConfig(config), PopulationFromConfig(config), DiseasesFromConfig(config), startReputation,
            AwardsFromConfig(config), config.GetInt("gbv.LandCostPerTile") ?? LevelRules.DefaultLandCostPerTile,
            EndConditionsFromConfig(config, startCash, startReputation),
            TownInt(config, levelNumber, "InterestRate") ?? Hospital.DefaultInterestRate,
            TownInt(config, levelNumber, "OverdraftDiff") ?? Hospital.DefaultOverdraftDifferential,
            ObjectAvailabilityFromConfig(config, content),
            EarthquakesFromConfig(config),
            config.GetInt("payroll.MaxSalary") ?? LevelRules.DefaultMaxSalary,
            new TirednessRules(
                config.GetInt("gbv.Tired") ?? TirednessRules.Default.Tired,
                config.GetInt("gbv.VeryTired") ?? TirednessRules.Default.VeryTired,
                config.GetInt("gbv.CrackUpTired") ?? TirednessRules.Default.CrackUpTired),
            config.GetInt("gbv.MayorLaunch") ?? LevelRules.DefaultMayorLaunch,
            EmergenciesFromConfig(config),
            EpidemicsFromConfig(config),
            CompetitorsFromConfig(config),
            config.GetInt("gbv.ScoreMaxInc") ?? LevelRules.DefaultScoreMaxIncrease,
            levelNumber,
            RandomEmergenciesFromConfig(config));
    }

    /// <summary>
    /// The computer opponents that play (<c>computer[n].Playing</c> 1), with their names (<c>computer[n].Name</c>, by
    /// default CorsixTH's base_config.lua's), in the order World:World's <c>pairs</c> visits base_config.lua's table:
    /// 1 to 14, then 0.
    /// </summary>
    public static IEnumerable<PlannedCompetitor> CompetitorsFromConfig(LevelConfig config)
    {
        foreach (var index in Enumerable.Range(1, BaseCompetitorNames.Length - 1).Append(0))
        {
            if (config.GetInt($"computer[{index}].Playing") == 1)
            {
                yield return new PlannedCompetitor(index + 1, config[$"computer[{index}].Name"] ?? BaseCompetitorNames[index]);
            }
        }
    }

    /// <summary>
    /// The epidemic settings: <c>expertise[n].ContRate</c> and <c>gbv.ContagiousSpreadFactor</c>, <c>ReduceContMonths</c>,
    /// <c>ReduceContPeepCount</c>, <c>VacCost</c>, <c>EpidemicFine</c>, <c>EpidemicCompLo</c>, <c>EpidemicCompHi</c>,
    /// <c>EpidemicRepLossMinimum</c>, <c>EpidemicEvacMinimum</c> and <c>EpidemicConcurrentLimit</c>, on top of
    /// CorsixTH's base_config.lua.
    /// </summary>
    public static EpidemicRules EpidemicsFromConfig(LevelConfig config)
    {
        var defaults = EpidemicRules.Default;
        var rates = new int[DiseaseRules.ExpertiseCount + 1];
        for (var i = 1; i < rates.Length; i++)
        {
            rates[i] = config.GetInt($"expertise[{i}].ContRate") ?? 0;
        }

        return new EpidemicRules(
            rates,
            config.GetInt("gbv.ContagiousSpreadFactor") ?? defaults.SpreadFactor,
            config.GetInt("gbv.ReduceContMonths") ?? defaults.ReduceContagiousMonths,
            config.GetInt("gbv.ReduceContPeepCount") ?? defaults.ReduceContagiousPeopleCount,
            config.GetInt("gbv.VacCost") ?? defaults.VaccinationCost,
            config.GetInt("gbv.EpidemicFine") ?? defaults.Fine,
            config.GetInt("gbv.EpidemicCompLo") ?? defaults.CompensationLow,
            config.GetInt("gbv.EpidemicCompHi") ?? defaults.CompensationHigh,
            config.GetInt("gbv.EpidemicRepLossMinimum") ?? defaults.ReputationLossMinimum,
            config.GetInt("gbv.EpidemicEvacMinimum") ?? defaults.EvacuationMinimum,
            config.GetInt("gbv.EpidemicConcurrentLimit") ?? defaults.ConcurrentLimit);
    }

    /// <summary>
    /// The <c>quake_control[n].StartMonth</c>, <c>EndMonth</c> and <c>Severity</c> entries from 0 up to the first one the
    /// files leave out; a missing field counts as 0. CorsixTH's base_config.lua has none.
    /// </summary>
    public static IEnumerable<PlannedEarthquake> EarthquakesFromConfig(LevelConfig config)
    {
        for (var i = 0; ; i++)
        {
            var startMonth = config.GetInt($"quake_control[{i}].StartMonth");
            var endMonth = config.GetInt($"quake_control[{i}].EndMonth");
            var severity = config.GetInt($"quake_control[{i}].Severity");
            if (startMonth is null && endMonth is null && severity is null)
            {
                yield break;
            }

            yield return new PlannedEarthquake(startMonth ?? 0, endMonth ?? 0, severity ?? 0);
        }
    }

    /// <summary>
    /// <c>World:nextEmergency</c>'s test <c>control[0].Random or control[0].Mean</c>: either setting, whatever its value
    /// (0 is true in Lua), makes the level's emergencies random, <c>Mean</c> (180) days apart give or take
    /// <c>Variance</c> (30).
    /// </summary>
    public static RandomEmergencies? RandomEmergenciesFromConfig(LevelConfig config)
    {
        var mean = config.GetInt("emergency_control[0].Mean");
        return config.GetInt("emergency_control[0].Random") is null && mean is null
            ? null
            : new RandomEmergencies(mean ?? RandomEmergencies.DefaultMean, config.GetInt("emergency_control[0].Variance") ?? RandomEmergencies.DefaultVariance);
    }

    /// <summary>
    /// The <c>emergency_control[n]</c> entries (<c>StartMonth</c>, <c>EndMonth</c>, <c>Min</c>, <c>Max</c>, <c>Illness</c>,
    /// <c>PercWin</c>, <c>Bonus</c>) up to the first two numbers in a row that the files leave out, as CorsixTH steps over
    /// one missing entry (level 3 has no fifth). CorsixTH's base_config.lua has an empty first one, which never comes.
    /// </summary>
    public static IEnumerable<PlannedEmergency> EmergenciesFromConfig(LevelConfig config)
    {
        var missing = 0;
        for (var i = 0; missing < 2; i++)
        {
            int? Get(string field) => config.GetInt($"emergency_control[{i}].{field}");
            var fields = new[] { Get("StartMonth"), Get("EndMonth"), Get("Min"), Get("Max"), Get("Illness"), Get("PercWin"), Get("Bonus") };
            if (fields.All(field => field is null))
            {
                missing++;
                continue;
            }

            missing = 0;
            yield return new PlannedEmergency(i, fields[0] ?? 0, fields[1] ?? 0, fields[2] ?? 0, fields[3] ?? 0, fields[4] ?? 0, fields[5] ?? 0, fields[6] ?? 0);
        }
    }

    /// <summary>
    /// The <c>objects[n].AvailableForLevel</c> and <c>objects[n].StartAvail</c> settings of the objects the level names;
    /// a missing one counts as 1, as for an object the level does not name at all.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, ObjectAvailability>> ObjectAvailabilityFromConfig(LevelConfig config, GameContent content)
    {
        foreach (var definition in content.Objects)
        {
            var forLevel = config.GetInt($"objects[{definition.ObjectType}].AvailableForLevel");
            var atStart = config.GetInt($"objects[{definition.ObjectType}].StartAvail");
            if (forLevel is not null || atStart is not null)
            {
                yield return KeyValuePair.Create(definition.Id, new ObjectAvailability(forLevel != 0, atStart != 0));
            }
        }
    }

    /// <summary>
    /// The <c>popn[n].Month</c> and <c>popn[n].Change</c> entries, read on top of CorsixTH's base_config.lua entries
    /// index by index; the list goes on as long as the indices do without a gap.
    /// </summary>
    public static IReadOnlyList<PopulationChange> PopulationFromConfig(LevelConfig config)
    {
        var defaults = LevelRules.Free.Population;
        var entries = new List<PopulationChange>();
        while (true)
        {
            var i = entries.Count;
            var fallback = i < defaults.Count ? defaults[i] : (PopulationChange?)null;
            var month = config.GetInt($"popn[{i}].Month") ?? fallback?.Month;
            var change = config.GetInt($"popn[{i}].Change") ?? fallback?.Change;
            if (month is null || change is null)
            {
                return entries;
            }

            entries.Add(new PopulationChange(month.Value, change.Value));
        }
    }

    /// <summary>
    /// The level's <c>win_criteria[n]</c> and <c>lose_criteria[n]</c> entries (CorsixTH's <c>EndConditions:_loadGoals</c>);
    /// entries with a criterion CorsixTH does not know, such as base_config.lua's zeros, are left out.
    /// </summary>
    public static EndConditions EndConditionsFromConfig(LevelConfig config, int startCash, int startReputation)
    {
        return new EndConditions(GoalsFromConfig(config, "win_criteria", startCash, startReputation), GoalsFromConfig(config, "lose_criteria", startCash, startReputation));
    }

    /// <summary>The level's <c>awards_trophies</c> settings on top of CorsixTH's base_config.lua.</summary>
    public static AwardRules AwardsFromConfig(LevelConfig config)
    {
        var given = new List<KeyValuePair<string, int>>();
        foreach (var name in AwardRules.Names)
        {
            if (config.GetInt("awards_trophies." + name) is { } value)
            {
                given.Add(new(name, value));
            }
        }

        return AwardRules.With(given);
    }

    /// <summary>
    /// The staff settings: <c>staff[n].MinSalary</c>, <c>gbv.SalaryAdd[3]</c> to <c>[8]</c>,
    /// <c>gbv.SalaryAbilityDivisor</c>, <c>gbv.DoctorThreshold</c>, <c>gbv.ConsultantThreshold</c> and
    /// the <c>staff_levels</c> entries. What the files leave out comes from CorsixTH's base_config.lua,
    /// on which CorsixTH reads the level files.
    /// </summary>
    public static StaffRules StaffFromConfig(LevelConfig config)
    {
        var defaults = StaffRules.Default;
        var minSalaries = new int[StaffClasses.Count];
        for (var i = 0; i < minSalaries.Length; i++)
        {
            minSalaries[i] = config.GetInt($"staff[{i}].MinSalary") ?? defaults.MinSalary((StaffClass)i);
        }

        var salaryAdds = new int[StaffRules.LastSalaryAdd - StaffRules.FirstSalaryAdd + 1];
        for (var i = 0; i < salaryAdds.Length; i++)
        {
            var index = StaffRules.FirstSalaryAdd + i;
            salaryAdds[i] = config.GetInt($"gbv.SalaryAdd[{index}]") ?? defaults.SalaryAdd(index);
        }

        // CorsixTH requires the divisor to be above 0; a file that breaks this keeps the default.
        var divisor = config.GetInt("gbv.SalaryAbilityDivisor") is { } given and > 0 ? given : defaults.SalaryAbilityDivisor;

        // staff_levels[0] is base_config's entry with the files' values on top; later entries exist as
        // far as the files give their months without a gap, as Lua's length operator counts them.
        var levels = new List<StaffLevel> { StaffLevelFromConfig(config, 0, defaults.Levels[0]) };
        while (config.GetInt($"staff_levels[{levels.Count}].Month") is not null)
        {
            levels.Add(StaffLevelFromConfig(config, levels.Count, null));
        }

        return new StaffRules(
            minSalaries,
            salaryAdds,
            divisor,
            config.GetInt("gbv.DoctorThreshold") ?? defaults.DoctorThreshold,
            config.GetInt("gbv.ConsultantThreshold") ?? defaults.ConsultantThreshold,
            levels);
    }

    /// <summary>
    /// The disease settings: <c>expertise[n].StartPrice</c>, <c>.Known</c> and <c>.MaxDiagDiff</c>, <c>visuals[n]</c>,
    /// <c>non_visuals[n]</c>, <c>visuals_available[n]</c>, <c>non_visuals_available[n]</c>, <c>gbv.HoldVisualMonths</c>,
    /// <c>gbv.HoldVisualPeepCount</c>, <c>gbv.StartRating</c> and <c>gbv.StartCost</c>, on top of CorsixTH's base_config.lua.
    /// </summary>
    public static DiseaseRules DiseasesFromConfig(LevelConfig config)
    {
        var defaults = DiseaseRules.Default;
        var count = DiseaseRules.ExpertiseCount + 1;
        var prices = new int[count];
        var known = new bool[count];
        var difficulties = new int[count];
        for (var i = 1; i < count; i++)
        {
            prices[i] = config.GetInt($"expertise[{i}].StartPrice") ?? defaults.StartPrice(i);
            known[i] = (config.GetInt($"expertise[{i}].Known") ?? (defaults.IsKnown(i) ? 1 : 0)) == 1;
            difficulties[i] = config.GetInt($"expertise[{i}].MaxDiagDiff") ?? defaults.MaxDiagDiff(i);
        }

        int[] Values(string table, int length, IReadOnlyList<int>? fallback) =>
            [.. Enumerable.Range(0, length).Select(i => config.GetInt($"{table}[{i}].Value") ?? fallback?[i] ?? 0)];

        return new DiseaseRules(
            prices,
            known,
            difficulties,
            Values("visuals", DiseaseRules.VisualsCount, null),
            Values("non_visuals", DiseaseRules.NonVisualsCount, null),
            Values("visuals_available", DiseaseRules.VisualsCount, BaseVisualsAvailable),
            Values("non_visuals_available", DiseaseRules.NonVisualsCount, null),
            config.GetInt("gbv.HoldVisualMonths") ?? BaseHoldVisualMonths,
            config.GetInt("gbv.HoldVisualPeepCount") ?? BaseHoldVisualPeepCount,
            config.GetInt("gbv.StartRating") ?? DiseaseRules.BaseDrugStartRating,
            config.GetInt("gbv.StartCost") ?? DiseaseRules.BaseDrugStartCost);
    }

    private static StaffLevel StaffLevelFromConfig(LevelConfig config, int index, StaffLevel? fallback)
    {
        int? Get(string field, int? value) => config.GetInt($"staff_levels[{index}].{field}") ?? value;
        return new StaffLevel(
            Get("Month", fallback?.Month) ?? 0,
            Get("Doctors", fallback?.Doctors),
            Get("Nurses", fallback?.Nurses),
            Get("Handymen", fallback?.Handymen),
            Get("Receptionists", fallback?.Receptionists),
            Get("ShrkRate", fallback?.ShrkRate),
            Get("SurgRate", fallback?.SurgRate),
            Get("RschRate", fallback?.RschRate),
            Get("JrRate", fallback?.JrRate),
            Get("ConsRate", fallback?.ConsRate));
    }

    /// <summary>The money settings for an original level map such as <c>LEVEL.L1</c>, or for any other map.</summary>
    public static LevelRules LoadOriginal(ThemeHospitalInstall install, string mapFileName, GameContent content, string difficulty = DefaultDifficulty)
    {
        var level = LevelNumber(mapFileName);
        return FromConfig(LoadConfig(install, level, difficulty), level, content);
    }

    [GeneratedRegex(@"^LEVEL\.L(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex OriginalLevel();

    private static List<EndGoal> GoalsFromConfig(LevelConfig config, string table, int startCash, int startReputation)
    {
        const int maxIndex = 100;
        var goals = new List<EndGoal>();
        for (var i = 0; i < maxIndex; i++)
        {
            if (config.GetInt($"{table}[{i}].Criteria") is not { } number || !Enum.IsDefined((EndCriterion)number) || number > byte.MaxValue)
            {
                continue;
            }

            var criterion = (EndCriterion)number;
            var start = criterion switch
            {
                EndCriterion.Balance => startCash,
                EndCriterion.Reputation => startReputation,
                _ => 0,
            };
            goals.Add(new EndGoal(
                criterion,
                config.GetInt($"{table}[{i}].Group") ?? 0,
                config.GetInt($"{table}[{i}].MaxMin") == 1,
                config.GetInt($"{table}[{i}].Value") ?? 0,
                config.GetInt($"{table}[{i}].Bound") ?? 0,
                start));
        }

        return goals;
    }

    /// <summary>A setting of the level's own <c>towns[n]</c> entry, else of <c>town</c>.</summary>
    private static int? TownInt(LevelConfig config, int? levelNumber, string name)
    {
        return (levelNumber is { } level ? config.GetInt($"towns[{level}].{name}") : null) ?? config.GetInt($"town.{name}");
    }
}
