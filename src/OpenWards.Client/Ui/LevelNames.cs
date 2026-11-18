using System.Globalization;

namespace OpenWards.Client.Ui;

/// <summary>How levels are named to the player: each original level is a ward of the hospital, "Ward No. n".</summary>
internal static class LevelNames
{
    /// <summary>A level's name for the player: an original level (<c>LEVEL.Ln</c>) is "Ward No. n", others keep their name.</summary>
    public static string WardName(string levelName) =>
        OriginalLevelNumber(levelName) is { } number ? UiText.Format("openwards.status.ward", "Ward No. %1%", number) : levelName;

    /// <summary>The number of an original level file (<c>LEVEL.L3</c> is 3), or null.</summary>
    public static int? OriginalLevelNumber(string levelName) =>
        levelName.StartsWith("LEVEL.L", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(levelName.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
}
