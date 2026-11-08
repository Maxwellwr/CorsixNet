// Which level files are read, and where starting cash and costs come from, ported from CorsixTH
// CorsixTH/Lua/map.lua (Map:_loadOriginalCampaignLevel), CorsixTH/Lua/hospital.lua (Hospital:Hospital,
// Hospital:getObjectBuildCost) and CorsixTH/Lua/world.lua (World:getAvailableRooms); the fallback
// starting cash is CorsixTH/Lua/base_config.lua's town.StartCash.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using System.Text.RegularExpressions;
using CorsixNet.Formats;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Economy;

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
    public static int? LevelNumber(string mapFileName)
    {
        var match = OriginalLevel().Match(Path.GetFileName(mapFileName));
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
        return new LevelRules(startCash, rooms, objects);
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
