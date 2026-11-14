using System.Globalization;
using CorsixNet.Formats;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace CorsixNet.Content.Settings;

/// <summary>
/// The settings files' values (config.yaml, hotkeys.yaml) as YAML, read with YamlDotNet: a top-level mapping of
/// names to texts, numbers, true and false, lists and mappings; and the values of the files they replace
/// (config.txt, hotkeys.txt, Lua assignments) in the same shapes, for importing those once.
/// </summary>
internal static class SettingsYaml
{
    /// <summary>
    /// The values by name: <see cref="string"/>, <see cref="decimal"/>, <see cref="bool"/>, null, lists
    /// (<see cref="List{T}"/> of values) and mappings (<see cref="Dictionary{TKey,TValue}"/> of values). A quoted
    /// scalar is always text; an unquoted one is a number, true or false where it reads as one.
    /// </summary>
    /// <exception cref="InvalidDataException">The text is not YAML, or not a mapping of names.</exception>
    public static Dictionary<string, object?> Parse(string text)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(text));
        }
        catch (YamlException exception)
        {
            throw new InvalidDataException($"line {exception.Start.Line}: {exception.Message}", exception);
        }

        if (stream.Documents.Count == 0)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        return stream.Documents[0].RootNode is YamlMappingNode root
            ? Mapping(root)
            : throw new InvalidDataException("The settings are not a mapping of names to values.");
    }

    /// <summary>The values of a file of Lua assignments (the old config.txt and hotkeys.txt) in the same shapes.</summary>
    public static Dictionary<string, object?> FromLua(string text) =>
        LuaAssignments.Parse(text).ToDictionary(pair => pair.Key, pair => FromLuaValue(pair.Value), StringComparer.Ordinal);

    /// <summary>A text as a YAML single-quoted scalar, which takes backslashes as they are.</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static object? FromLuaValue(object? value) => value switch
    {
        LuaTable { Fields.Count: 0 } table => table.Items.Select(FromLuaValue).ToList(),
        LuaTable table => table.Fields.ToDictionary(pair => pair.Key, pair => FromLuaValue(pair.Value), StringComparer.Ordinal),
        _ => value,
    };

    private static Dictionary<string, object?> Mapping(YamlMappingNode mapping)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in mapping.Children)
        {
            values[(key as YamlScalarNode)?.Value ?? string.Empty] = Value(value);
        }

        return values;
    }

    private static object? Value(YamlNode node) => node switch
    {
        YamlMappingNode mapping => Mapping(mapping),
        YamlSequenceNode sequence => sequence.Children.Select(Value).ToList(),
        YamlScalarNode { Style: ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted or ScalarStyle.Literal or ScalarStyle.Folded } scalar => scalar.Value ?? string.Empty,
        YamlScalarNode scalar => Plain(scalar.Value ?? string.Empty),
        _ => null,
    };

    private static object? Plain(string text) =>
        text.ToLowerInvariant() switch
        {
            "true" => true,
            "false" => false,
            "" or "~" or "null" => null,
            _ when decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
            _ => text,
        };
}
