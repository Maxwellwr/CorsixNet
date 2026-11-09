// Ported from CorsixTH CorsixTH/Lua/app.lua (App:readLevelFile).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Text.RegularExpressions;

namespace CorsixNet.Formats;

/// <summary>
/// The general information of a CorsixTH <c>.level</c> file: lines such as <c>%Name = "Avatar"</c>, whose quoted text may
/// run over several lines. The settings themselves (<c>#</c> lines) are read by <see cref="LevelConfig"/>.
/// </summary>
/// <param name="Name">The level's name (<c>%Name</c>), or "Unknown name".</param>
/// <param name="MapFile">The map file (<c>%MapFile</c>, or the deprecated <c>%LevelFile</c>), or null if the file names none.</param>
/// <param name="Briefing">The text shown before the level (<c>%LevelBriefingTable.en</c> or <c>%LevelBriefing</c>), or null.</param>
/// <param name="Debriefing">The text shown on winning (<c>%LevelDebriefingTable.en</c> or <c>%LevelDebriefing</c>), or null.</param>
public sealed partial record CustomLevelInfo(string Name, string? MapFile, string? Briefing, string? Debriefing)
{
    /// <summary>Reads the information from a level file's text.</summary>
    public static CustomLevelInfo Parse(string text)
    {
        return new CustomLevelInfo(
            Find(text, "Name") ?? "Unknown name",
            Find(text, "MapFile") ?? Find(text, "LevelFile"),
            Find(text, @"LevelBriefingTable\.en") ?? Find(text, "LevelBriefing"),
            Find(text, @"LevelDebriefingTable\.en") ?? Find(text, "LevelDebriefing"));
    }

    /// <summary>
    /// The first <c>%key ?= ?"text"</c>, as Lua's <c>contents:match("%key ?= ?\"(.-)\"")</c> finds it: the shortest text up
    /// to the next quote, over line breaks too.
    /// </summary>
    private static string? Find(string text, string key)
    {
        var match = Regex.Match(text, "%" + key + " ?= ?\"(.*?)\"", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups[1].Value : null;
    }
}
