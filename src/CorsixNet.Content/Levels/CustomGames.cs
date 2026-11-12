// The player's own levels and campaigns, as CorsixTH's main menu lists them: CorsixTH/Lua/dialogs/resizables/
// file_browsers/custom_game.lua (UICustomGame, the .level files of the level folders) and custom_campaign.lua
// (UICustomCampaign, the .campaign files of the campaign folders, those with a name already shown left out).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Text;
using CorsixNet.Formats;

namespace CorsixNet.Content.Levels;

/// <summary>A custom level file and what it says about itself.</summary>
public sealed record CustomLevelEntry(string Path, CustomLevelInfo Info);

/// <summary>A campaign file and what it says about itself.</summary>
public sealed record CustomCampaignEntry(string Path, CustomCampaign Campaign);

/// <summary>Finds the custom levels and campaigns in folders.</summary>
public static class CustomGames
{
    /// <summary>The <c>.level</c> files in the folders and their subfolders, by name; unreadable ones are left out.</summary>
    public static IReadOnlyList<CustomLevelEntry> FindLevels(params IEnumerable<string> folders) =>
        [.. Files(folders, "*.level")
            .Select(path => Read(path, text => new CustomLevelEntry(path, CustomLevelInfo.Parse(text))))
            .OfType<CustomLevelEntry>()
            .OrderBy(entry => entry.Info.Name, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// The <c>.campaign</c> files in the folders and their subfolders, by name; unreadable ones, those naming no level and
    /// a second campaign of a name already found are left out (<c>duplicates_warning</c>).
    /// </summary>
    public static IReadOnlyList<CustomCampaignEntry> FindCampaigns(params IEnumerable<string> folders)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = new List<CustomCampaignEntry>();
        foreach (var path in Files(folders, "*.campaign"))
        {
            if (Read(path, text => CustomCampaign.Parse(text)) is { Levels.Count: > 0 } campaign
                && seen.Add(campaign.Name ?? System.IO.Path.GetFileNameWithoutExtension(path)))
            {
                found.Add(new CustomCampaignEntry(path, campaign));
            }
        }

        return [.. found.OrderBy(entry => entry.Campaign.Name ?? System.IO.Path.GetFileNameWithoutExtension(entry.Path), StringComparer.OrdinalIgnoreCase)];
    }

    private static IEnumerable<string> Files(IEnumerable<string> folders, string pattern) =>
        folders.Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories).Order(StringComparer.Ordinal));

    private static T? Read<T>(string path, Func<string, T> parse)
        where T : class
    {
        try
        {
            return parse(Encoding.UTF8.GetString(File.ReadAllBytes(path)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }
}
