// Which level files are read, and where starting cash and costs come from, ported from CorsixTH
// CorsixTH/Lua/map.lua (Map:_loadOriginalCampaignLevel), CorsixTH/Lua/hospital.lua (Hospital:Hospital,
// Hospital:getObjectBuildCost) and CorsixTH/Lua/world.lua (World:getAvailableRooms); the fallback
// starting cash is CorsixTH/Lua/base_config.lua's town.StartCash, and the staff settings fall back
// to the same file's staff, gbv and staff_levels.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using System.Text.RegularExpressions;
using CorsixNet.Formats;
using CorsixNet.Sim.Definitions;
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

        var rooms = content.Rooms
            .Where(room => room.LevelConfigId != 0 && config.GetInt($"rooms[{room.LevelConfigId}].Cost") is not null)
            .Select(room => KeyValuePair.Create(room.Id, config.GetInt($"rooms[{room.LevelConfigId}].Cost")!.Value));
        var objects = content.Objects
            .Where(definition => config.GetInt($"objects[{definition.ObjectType}].StartCost") is not null)
            .Select(definition => KeyValuePair.Create(definition.Id, config.GetInt($"objects[{definition.ObjectType}].StartCost")!.Value));
        return new LevelRules(startCash, rooms, objects, StaffFromConfig(config), PopulationFromConfig(config));
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
}
