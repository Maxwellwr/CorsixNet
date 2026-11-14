// What a campaign file holds, ported from CorsixTH CorsixTH/Lua/app.lua (App:readCampaignFile, App:loadCampaign),
// CorsixTH/Lua/strings.lua (Strings:getLocalisedText) and CorsixTH/Campaigns/example.campaign.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace CorsixNet.Formats;

/// <summary>
/// A CorsixTH campaign (<c>.campaign</c>): a name, a description, the level files to play in order, the text shown on
/// winning the last one and whether the level advance movies play.
/// </summary>
/// <remarks>
/// CorsixNet's own campaign files are YAML (<c>.campaign.yaml</c>):
/// <code>
/// name: Example campaign
/// description: {en: English description, fr: Description française}
/// levels: [finisham.level, avatar.level]
/// winning_text: Congratulations!
/// movie: true
/// </code>
/// where a description or winning text is a text or a mapping of texts by language. CorsixTH's own files
/// (<c>.campaign</c>) are still read: CorsixTH runs them as Lua, and they only assign strings (joined with <c>..</c>),
/// numbers, booleans and tables of them to names and table fields, so that much of Lua is read and anything else
/// refused.
/// </remarks>
public sealed record CustomCampaign(string? Name, string? Description, IReadOnlyList<string> Levels, string? WinningText, bool Movie)
{
    /// <summary>CorsixTH's campaign files, Lua.</summary>
    public const string LuaExtension = ".campaign";

    /// <summary>CorsixNet's campaign files, YAML.</summary>
    public const string YamlExtension = ".campaign.yaml";

    /// <summary>Reads a campaign file of either kind, by its name.</summary>
    /// <exception cref="InvalidDataException">The file is not a campaign file.</exception>
    public static CustomCampaign Read(string path, string text, string language = "en") =>
        path.EndsWith(YamlExtension, StringComparison.OrdinalIgnoreCase) ? ParseYaml(text, language) : Parse(text, language);

    /// <summary>
    /// Reads a YAML campaign file, with the description and winning text in <paramref name="language"/> where the file
    /// gives them by language, else in English, else the first given.
    /// </summary>
    /// <exception cref="InvalidDataException">The text is not a YAML campaign file.</exception>
    public static CustomCampaign ParseYaml(string text, string language = "en")
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

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidDataException("A campaign file is a mapping of name, description, levels, winning_text and movie.");
        }

        YamlNode? Field(string name) => root.Children.TryGetValue(new YamlScalarNode(name), out var node) ? node : null;
        string? Localised(string name) => Field(name) switch
        {
            YamlScalarNode scalar => scalar.Value,
            YamlMappingNode byLanguage => Text(byLanguage, language) ?? Text(byLanguage, "en") ?? byLanguage.Children.Values.OfType<YamlScalarNode>().FirstOrDefault()?.Value,
            _ => null,
        };

        var levels = Field("levels") is YamlSequenceNode list ? list.Children.OfType<YamlScalarNode>().Select(level => level.Value ?? string.Empty).Where(level => level.Length > 0).ToList() : [];
        var movie = Field("movie") is YamlScalarNode { Value: { } flag } && bool.TryParse(flag, out var on) && on;
        return new CustomCampaign((Field("name") as YamlScalarNode)?.Value, Localised("description"), levels, Localised("winning_text"), movie);

        static string? Text(YamlMappingNode mapping, string key) =>
            mapping.Children.TryGetValue(new YamlScalarNode(key.ToLower(CultureInfo.InvariantCulture)), out var node) ? (node as YamlScalarNode)?.Value : null;
    }

    /// <summary>
    /// Reads a campaign file, with the description and winning text in <paramref name="language"/> where the file gives
    /// them (<c>description_table</c>, <c>winning_text_table</c>), else in English, else the plain ones.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a campaign file of the kind CorsixTH ships.</exception>
    public static CustomCampaign Parse(string text, string language = "en")
    {
        var globals = LuaAssignments.Parse(text);
        string? Localised(string plain, string table)
        {
            var value = globals.GetValueOrDefault(plain) as string;
            if (globals.GetValueOrDefault(table) is not LuaTable localised)
            {
                return value;
            }

            return localised.Fields.GetValueOrDefault(language) as string ?? localised.Fields.GetValueOrDefault("en") as string ?? value;
        }

        var levels = globals.GetValueOrDefault("levels") is LuaTable table ? table.Items.OfType<string>().ToList() : [];
        return new CustomCampaign(
            globals.GetValueOrDefault("name") as string,
            Localised("description", "description_table"),
            levels,
            Localised("winning_text", "winning_text_table"),
            globals.GetValueOrDefault("movie") is true);
    }
}
