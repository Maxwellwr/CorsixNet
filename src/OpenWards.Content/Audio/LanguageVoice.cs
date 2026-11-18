namespace OpenWards.Content.Audio;

/// <summary>
/// The announcer's speech in the chosen language, beside the game (<c>Languages/Voice/&lt;language&gt;</c>): WAV files
/// named as the original's sounds, such as <c>EMERG006.WAV</c>, each heard instead of the original's sound of that
/// name. A sound the folder lacks, or a language without a folder, keeps the original's.
/// </summary>
public sealed class LanguageVoice
{
    /// <summary>No speech of its own: every sound is the original's.</summary>
    public static readonly LanguageVoice None = new(new Dictionary<string, string>());

    private readonly Dictionary<string, string> _files;

    private LanguageVoice(Dictionary<string, string> files) => _files = files;

    /// <summary>How many sounds the language speaks itself.</summary>
    public int Count => _files.Count;

    /// <summary>The WAV files of a folder (none when it is missing or null).</summary>
    public static LanguageVoice Load(string? folder)
    {
        if (folder is null || !Directory.Exists(folder))
        {
            return None;
        }

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            if (string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
            {
                files.TryAdd(Path.GetFileName(path), path);
            }
        }

        return new LanguageVoice(files);
    }

    /// <summary>The file to hear instead of the original's sound <paramref name="name"/> (case ignored), or null.</summary>
    public string? Find(string name) => _files.GetValueOrDefault(name);
}
