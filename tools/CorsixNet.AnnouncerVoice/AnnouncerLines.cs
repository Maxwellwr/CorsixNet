using CorsixNet.Content.Languages;

namespace CorsixNet.AnnouncerVoice;

/// <summary>What the announcer says in a language: its subtitles (<c>subtitles.emerg006</c> for EMERG006.WAV).</summary>
internal static class AnnouncerLines
{
    private const string Prefix = "subtitles.";

    /// <summary>
    /// The language's own subtitles by name (emerg006), with <paramref name="overrides"/> said instead where given;
    /// subtitles it inherits unchanged from English are left out, as there is nothing to say in the language, and so are
    /// those that only describe a sound between asterisks (machwarn's "*Machine alarm sound*").
    /// </summary>
    public static SortedDictionary<string, string> Of(CorsixLanguages languages, string language, IReadOnlyDictionary<string, string> overrides)
    {
        var english = languages.Load("English", _ => null);
        var strings = languages.Load(language, _ => null);
        var isEnglish = string.Equals(language, "English", StringComparison.OrdinalIgnoreCase);
        var lines = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, text) in strings.Strings)
        {
            if (!key.StartsWith(Prefix, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(text) || (!isEnglish && english.Get(key) == text))
            {
                continue;
            }

            var name = key[Prefix.Length..];
            var said = overrides.GetValueOrDefault(name) ?? text;
            if (!IsSoundOnly(said))
            {
                lines[name] = said;
            }
        }

        return lines;
    }

    /// <summary>Whether a subtitle only describes a sound: "*Machine alarm sound*." and the like.</summary>
    public static bool IsSoundOnly(string text)
    {
        var trimmed = text.Trim().TrimEnd('.', ' ');
        return trimmed.Length >= 2 && trimmed[0] == '*' && trimmed[^1] == '*';
    }

    /// <summary>The sound a subtitle belongs to: EMERG006.WAV for emerg006.</summary>
    public static string SoundFile(string name) => name.ToUpperInvariant() + ".WAV";
}
