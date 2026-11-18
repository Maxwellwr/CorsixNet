namespace OpenWards.Content.Settings;

/// <summary>
/// The game was called CorsixNet before it became OpenWards. Its settings and saves lived in a <c>CorsixNet</c>
/// folder and its environment variables began with <c>CORSIXNET_</c>; this carries both over so that a player who
/// updates keeps their settings, hotkeys and saved games.
/// </summary>
public static class LegacyFolders
{
    /// <summary>The settings and saves folder's name.</summary>
    public const string FolderName = "OpenWards";

    /// <summary>The folder's name before the game was renamed.</summary>
    public const string LegacyFolderName = "CorsixNet";

    private const string VariablePrefix = "OPENWARDS_";
    private const string LegacyVariablePrefix = "CORSIXNET_";

    /// <summary>
    /// An <c>OPENWARDS_</c> environment variable, or the same <c>CORSIXNET_</c> one if only that is set.
    /// </summary>
    public static string? EnvironmentVariable(string name)
    {
        if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
        {
            return value;
        }

        return name.StartsWith(VariablePrefix, StringComparison.Ordinal)
            && Environment.GetEnvironmentVariable(LegacyVariablePrefix + name[VariablePrefix.Length..]) is { Length: > 0 } legacy
                ? legacy
                : null;
    }

    /// <summary>
    /// The folders that hold the default settings and saves folders: <c>%AppData%</c> on Windows, else
    /// <c>$XDG_CONFIG_HOME</c> or <c>~/.config</c> (<see cref="GameConfig.DefaultPath"/>), and the user's application
    /// data (<see cref="Saves.SaveGameStore.DefaultDirectory"/>). Usually the same folder.
    /// </summary>
    public static IEnumerable<string> UserFolders()
    {
        var folders = new List<string>();
        void Add(string? folder)
        {
            if (folder is { Length: > 0 } && !folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                folders.Add(folder);
            }
        }

        if (OperatingSystem.IsWindows())
        {
            Add(Environment.GetEnvironmentVariable("AppData"));
        }
        else
        {
            Add(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
                ? xdg
                : Environment.GetEnvironmentVariable("HOME") is { Length: > 0 } home ? Path.Combine(home, ".config") : null);
        }

        Add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        return folders;
    }

    /// <summary>Moves the old folder to the new name in each of <see cref="UserFolders"/>.</summary>
    /// <returns>A line for each folder carried over, for the console.</returns>
    public static IReadOnlyList<string> MigrateUserFolders()
    {
        var messages = new List<string>();
        foreach (var parent in UserFolders())
        {
            if (Migrate(parent) is { } message)
            {
                messages.Add(message);
            }
        }

        return messages;
    }

    /// <summary>
    /// Moves <c><paramref name="parent"/>/CorsixNet</c> to <c><paramref name="parent"/>/OpenWards</c> if only the old
    /// one is there, or copies it if it cannot be moved (a file in it is open, say), and points paths in its
    /// <c>config.yaml</c> that led into the old folder at the new one. Nothing happens once the new folder exists.
    /// </summary>
    /// <returns>What was done, or null if nothing was.</returns>
    public static string? Migrate(string parent)
    {
        var legacy = Path.Combine(parent, LegacyFolderName);
        var current = Path.Combine(parent, FolderName);
        if (!Directory.Exists(legacy) || Directory.Exists(current) || File.Exists(current))
        {
            return null;
        }

        string done;
        try
        {
            Directory.Move(legacy, current);
            done = $"Moved {legacy} to {current}.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try
            {
                CopyFolder(legacy, current);
                done = $"Copied {legacy} to {current} ({e.Message}); the old folder can be deleted.";
            }
            catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException)
            {
                return $"Could not carry {legacy} over to {current}: {copyError.Message}";
            }
        }

        RepointConfig(Path.Combine(current, GameConfig.FileName), legacy, current);
        return done;
    }

    /// <summary>Replaces paths in the settings file that begin with the old folder by the new one.</summary>
    internal static void RepointConfig(string configPath, string legacy, string current)
    {
        if (!File.Exists(configPath))
        {
            return;
        }

        try
        {
            var text = File.ReadAllText(configPath);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var repointed = ReplaceFolder(text, legacy, current, comparison);
            if (!string.Equals(repointed, text, StringComparison.Ordinal))
            {
                File.WriteAllText(configPath, repointed);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The settings still load; a path the player set into the old folder then needs setting again.
        }
    }

    /// <summary>
    /// Replaces <paramref name="legacy"/> in <paramref name="text"/> where it is a whole folder: followed by a path
    /// separator, a quote or the end of a line.
    /// </summary>
    internal static string ReplaceFolder(string text, string legacy, string current, StringComparison comparison)
    {
        var result = new System.Text.StringBuilder(text.Length);
        var start = 0;
        while (true)
        {
            var found = text.IndexOf(legacy, start, comparison);
            if (found < 0)
            {
                result.Append(text, start, text.Length - start);
                return result.ToString();
            }

            var end = found + legacy.Length;
            var whole = end == text.Length || text[end] is '/' or '\\' or '\'' or '"' or '\r' or '\n';
            result.Append(text, start, found - start).Append(whole ? current : text.Substring(found, legacy.Length));
            start = end;
        }
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var folder in Directory.EnumerateDirectories(from))
        {
            CopyFolder(folder, Path.Combine(to, Path.GetFileName(folder)));
        }
    }
}
