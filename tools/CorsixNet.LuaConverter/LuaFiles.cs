// CorsixTH's campaign, settings and hotkeys files (Lua assignments, read by LuaAssignments) as the YAML CorsixNet
// reads: CorsixTH/Lua/app.lua (App:readCampaignFile) and strings.lua (Strings:getLocalisedText) for campaigns,
// config_finder.lua for config.txt and hotkeys.txt.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Text;
using System.Text.RegularExpressions;
using CorsixNet.Content.Settings;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace CorsixNet.LuaConverter;

/// <summary>Converts CorsixTH's Lua data files to CorsixNet's YAML.</summary>
internal static class LuaFiles
{
    /// <summary>
    /// A <c>.campaign</c> as a <c>.campaign.yaml</c>: name, description and winning text (a mapping by language where
    /// the file has a <c>_table</c> of them, with the plain text as English where the table has none), levels and
    /// movie. The file's opening comment (its author's copyright and licence) is kept.
    /// </summary>
    /// <exception cref="InvalidDataException">The text is not a campaign file of the kind CorsixTH ships.</exception>
    public static string Campaign(string text)
    {
        var globals = LuaAssignments.Parse(text);
        var root = new YamlMappingNode();
        if (globals.GetValueOrDefault("name") is string name)
        {
            root.Add("name", name);
        }

        AddLocalised(root, globals, "description");
        var levels = globals.GetValueOrDefault("levels") is LuaTable table ? table.Items.OfType<string>().ToList() : [];
        root.Add("levels", new YamlSequenceNode(levels.Select(level => new YamlScalarNode(level))));
        AddLocalised(root, globals, "winning_text");
        root.Add("movie", globals.GetValueOrDefault("movie") is true ? "true" : "false");
        return Comment(LicenceHeader(text)) + Yaml(root);
    }

    /// <summary>CorsixTH's <c>config.txt</c> as <c>config.yaml</c>, with the settings CorsixNet keeps.</summary>
    /// <exception cref="InvalidDataException">The text is not a settings file.</exception>
    public static string Config(string text) => GameConfig.FromValues(Values(text)).ToText();

    /// <summary>CorsixTH's <c>hotkeys.txt</c> as <c>hotkeys.yaml</c>.</summary>
    /// <exception cref="InvalidDataException">The text is not a hotkeys file.</exception>
    public static string Hotkeys(string text) => Content.Settings.Hotkeys.FromValues(Values(text)).ToText();

    /// <summary>The comment a Lua file starts with: its authors' copyright and licence.</summary>
    public static string LicenceHeader(string text)
    {
        var trimmed = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
        var block = Regex.Match(trimmed, @"^--\[(=*)\[(.*?)\]\1\]", RegexOptions.Singleline);
        if (block.Success)
        {
            return block.Groups[2].Value.Trim().TrimEnd('-').TrimEnd();
        }

        var lines = trimmed.Split('\n').TakeWhile(line => line.StartsWith("--", StringComparison.Ordinal)).Select(line => line[2..].TrimEnd('\r'));
        return string.Join('\n', lines).Trim();
    }

    /// <summary>A text as YAML comment lines, or nothing for none.</summary>
    public static string Comment(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var comment = new StringBuilder();
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            comment.Append(line.Length == 0 ? "#" : "# " + line.TrimEnd()).Append('\n');
        }

        return comment.ToString();
    }

    /// <summary>A YAML document's text, without the end marker.</summary>
    public static string Yaml(YamlNode root)
    {
        var text = new StringWriter();
        new YamlStream(new YamlDocument(root)).Save(new Emitter(text, new EmitterSettings(2, int.MaxValue, false, 1024)), false);
        var result = text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        return result.EndsWith("...\n", StringComparison.Ordinal) ? result[..^4] : result;
    }

    private static void AddLocalised(YamlMappingNode root, IReadOnlyDictionary<string, object?> globals, string name)
    {
        var plain = globals.GetValueOrDefault(name) as string;
        if (globals.GetValueOrDefault(name + "_table") is LuaTable { Fields.Count: > 0 } table)
        {
            var byLanguage = new YamlMappingNode();
            foreach (var (language, value) in table.Fields)
            {
                if (value is string localised)
                {
                    byLanguage.Add(language, localised);
                }
            }

            if (plain is not null && !table.Fields.ContainsKey("en"))
            {
                byLanguage.Add("en", plain);
            }

            root.Add(name, byLanguage);
        }
        else if (plain is not null)
        {
            root.Add(name, plain);
        }
    }

    /// <summary>The assignments' values in the shapes the settings take: lists for list tables, else mappings.</summary>
    private static Dictionary<string, object?> Values(string text) =>
        LuaAssignments.Parse(text).ToDictionary(pair => pair.Key, pair => Value(pair.Value), StringComparer.Ordinal);

    private static object? Value(object? value) => value switch
    {
        LuaTable { Fields.Count: 0 } table => table.Items.Select(Value).ToList(),
        LuaTable table => table.Fields.ToDictionary(pair => pair.Key, pair => Value(pair.Value), StringComparer.Ordinal),
        _ => value,
    };
}
