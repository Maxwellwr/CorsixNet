using System.Collections;

namespace OpenWards.Content.Settings;

/// <summary>
/// The font file chosen for each language, by the language's name (case not counted); equal to another with the same
/// choices, so that settings compare by what they hold.
/// </summary>
public sealed class LanguageFonts : IReadOnlyDictionary<string, string>, IEquatable<LanguageFonts>
{
    private readonly Dictionary<string, string> _fonts;

    public LanguageFonts(IEnumerable<KeyValuePair<string, string>>? fonts = null)
    {
        _fonts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (language, path) in fonts ?? [])
        {
            if (!string.IsNullOrWhiteSpace(language) && !string.IsNullOrWhiteSpace(path))
            {
                _fonts[language.Trim()] = path;
            }
        }
    }

    public static LanguageFonts None { get; } = new();

    public int Count => _fonts.Count;

    public IEnumerable<string> Keys => _fonts.Keys;

    public IEnumerable<string> Values => _fonts.Values;

    public string this[string key] => _fonts[key];

    public bool ContainsKey(string key) => _fonts.ContainsKey(key);

    public bool TryGetValue(string key, out string value) => _fonts.TryGetValue(key, out value!);

    /// <summary>These choices with the language's font set, or taken away (null or empty).</summary>
    public LanguageFonts With(string language, string? path)
    {
        var fonts = new Dictionary<string, string>(_fonts, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(path))
        {
            fonts.Remove(language.Trim());
        }
        else
        {
            fonts[language.Trim()] = path;
        }

        return new LanguageFonts(fonts);
    }

    public bool Equals(LanguageFonts? other) =>
        other is not null && other.Count == Count
        && _fonts.All(pair => other.TryGetValue(pair.Key, out var path) && string.Equals(path, pair.Value, StringComparison.Ordinal));

    public override bool Equals(object? obj) => Equals(obj as LanguageFonts);

    public override int GetHashCode()
    {
        var hash = 0;
        foreach (var (language, path) in _fonts)
        {
            hash ^= HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(language), path);
        }

        return hash;
    }

    public override string ToString() => string.Join(", ", _fonts.Select(pair => $"{pair.Key} = {pair.Value}"));

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _fonts.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
