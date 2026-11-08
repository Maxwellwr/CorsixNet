// Line format ported from CorsixTH CorsixTH/Lua/map.lua (Map:loadMapConfig).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CorsixNet.Formats;

/// <summary>
/// Level settings from the original game's <c>LEVELS/*.SAM</c> files (and CorsixTH's <c>.level</c>
/// files, which use the same format), as flat keys such as <c>towns[1].StartCash</c>.
/// </summary>
/// <remarks>
/// Only lines starting with <c>#</c> carry settings. The first word names a table entry and may be
/// followed by several field names; the values follow in the same order, and anything after them
/// is a comment:
/// <code>#towns[1].StartCash.IllRate.InterestRate	40000	2	100	Level 1</code>
/// sets <c>towns[1].StartCash</c>, <c>towns[1].IllRate</c> and <c>towns[1].InterestRate</c>. A line
/// without field names sets the field <c>Value</c>. Later settings override earlier ones, so a
/// level's file can be read on top of its difficulty's base file.
/// </remarks>
public sealed partial class LevelConfig
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>Number of settings.</summary>
    public int Count => _values.Count;

    /// <summary>The setting's text, or null if it is not set.</summary>
    public string? this[string key] => _values.GetValueOrDefault(key);

    /// <summary>Reads a settings file. Text is ASCII (Latin-1 for comments).</summary>
    public static LevelConfig Parse(ReadOnlySpan<byte> data)
    {
        var config = new LevelConfig();
        config.Apply(data);
        return config;
    }

    /// <summary>Reads another settings file on top of this one.</summary>
    public void Apply(ReadOnlySpan<byte> data)
    {
        var text = Encoding.Latin1.GetString(data);
        foreach (var line in text.Split('\n', '\r'))
        {
            if (line.StartsWith('#'))
            {
                ApplyLine(line);
            }
        }
    }

    /// <summary>The setting as an integer, or null if it is not set or not a number.</summary>
    public int? GetInt(string key)
    {
        return this[key] is { } text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? (int)value
            : null;
    }

    private void ApplyLine(string line)
    {
        // Words as Lua's pattern "%.?[-?a-zA-Z0-9%[_%]]+" finds them; field names start with a dot.
        var parts = Word().Matches(line).Select(m => m.Value).ToList();
        var keys = 0;
        while (keys + 1 < parts.Count && parts[keys + 1].StartsWith('.'))
        {
            keys++;
        }

        if (parts.Count == 0)
        {
            return;
        }

        if (keys == 0)
        {
            parts.Insert(1, ".Value");
            keys = 1;
        }

        for (var i = 1; i <= keys; i++)
        {
            if (keys + i < parts.Count)
            {
                _values[parts[0] + parts[i]] = parts[keys + i];
            }
        }
    }

    [GeneratedRegex(@"\.?[-?a-zA-Z0-9\[_\]]+")]
    private static partial Regex Word();
}
