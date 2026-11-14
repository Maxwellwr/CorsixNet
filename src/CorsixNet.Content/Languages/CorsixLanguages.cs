// CorsixTH's own translations and CorsixNet's own strings, as YAML: each file declares its language and the steps
// CorsixTH's strings.lua takes for it (Inherit, the original's strings by name, strings set, removed and copied), in
// order. tools/CorsixNet.LuaConverter writes CorsixTH's files (CorsixTH/Lua/languages/*.lua) out this way.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Formats;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace CorsixNet.Content.Languages;

/// <summary>
/// A language the files declare: its names (<c>language</c>, the first in the language itself, the second in
/// English, then abbreviations) and its font (<c>font</c>: "cp437" or none for the game's own font, "mik" for the
/// Russian original's, "unicode" for a TrueType font).
/// </summary>
public sealed record CorsixLanguage(IReadOnlyList<string> Names, string? Font)
{
    /// <summary>The name in the language itself.</summary>
    public string NativeName => Names[0];

    /// <summary>The English name, as config.yaml's <c>language</c> takes it.</summary>
    public string EnglishName => Names.Count > 1 ? Names[1] : Names[0];

    /// <summary>Whether the original's bitmap font draws it (code page 437).</summary>
    public bool UsesGameFont => Font is null or "cp437";
}

/// <summary>
/// The language files, read once: which language each declares, and their steps to take when one is loaded.
/// </summary>
/// <remarks>
/// A file is a YAML mapping:
/// <code>
/// language: [Français, French, fr, fre, fra]
/// font: cp437
/// steps:
///   - inherit: English
///   - original_strings: 1
///   - strings:
///       tutorial:
///         build_reception: Construisez une réception
///   - remove: [staff_descriptions.misc.38]
///   - copy: {menu_file_save: menu_file_load}
/// strings:
///   corsixnet: ...
/// </code>
/// Nested mappings join their keys with dots and lists number their items from 1, as CorsixTH's tables do. The
/// pseudo language original_strings has <c>original</c> steps naming the original's strings: <c>name: [section,
/// index]</c>, or <c>name: [section]</c> for a whole section as a list. A <c>strings</c> mapping beside <c>steps</c>
/// is a last step.
/// </remarks>
public sealed class CorsixLanguages
{
    /// <summary>The pseudo language that maps names to the original's strings.</summary>
    public const string OriginalStrings = "original_strings";

    /// <summary>The folder beside CorsixTH's files that holds CorsixNet's own strings, in the same form.</summary>
    public const string OwnStringsFolder = "CorsixNet";

    /// <summary>The language files' extension.</summary>
    public const string Extension = ".yaml";

    /// <summary>
    /// The folder beside CorsixTH's files that holds the announcer's speech in other languages: a folder per language
    /// file, named as the file is (<c>Voice/russian</c> for russian.yaml), of WAV files named as the original's sounds.
    /// </summary>
    public const string VoiceFolder = "Voice";

    private readonly Dictionary<string, LanguageFile> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CorsixLanguage> _languages = [];
    private readonly CorsixLanguages? _own;

    /// <param name="files">CorsixTH's language files.</param>
    /// <param name="ownFiles">
    /// CorsixNet's own strings for the interface CorsixTH has no text for, one file per language declared as CorsixTH's
    /// are; a loaded language gets its file's strings over CorsixTH's.
    /// </param>
    public CorsixLanguages(IEnumerable<(string FileName, string Text)> files, IEnumerable<(string FileName, string Text)>? ownFiles = null)
    {
        _own = ownFiles is null ? null : new CorsixLanguages(ownFiles);
        foreach (var (fileName, text) in files.OrderBy(file => file.FileName, StringComparer.Ordinal))
        {
            LanguageFile file;
            try
            {
                file = LanguageFile.Parse(fileName, text);
            }
            catch (YamlException exception)
            {
                throw new InvalidDataException($"{fileName}: {exception.Message}", exception);
            }

            if (file.Names.Count == 0)
            {
                continue;
            }

            foreach (var name in file.Names)
            {
                _byName.TryAdd(name, file);
            }

            if (!string.Equals(file.Names[0], OriginalStrings, StringComparison.Ordinal))
            {
                _languages.Add(new CorsixLanguage(file.Names, file.Font));
            }
        }
    }

    /// <summary>The languages that can be chosen, by file name (original_strings left out).</summary>
    public IReadOnlyList<CorsixLanguage> Languages => _languages;

    /// <summary>
    /// Reads every language file of a folder, and CorsixNet's own strings from its <c>CorsixNet</c> folder; none when
    /// a folder is missing.
    /// </summary>
    public static CorsixLanguages Load(string directory) =>
        new(Files(directory), Files(Path.Combine(directory, OwnStringsFolder)));

    private static List<(string FileName, string Text)> Files(string directory) =>
        Directory.Exists(directory)
            ? [.. Directory.EnumerateFiles(directory, "*" + Extension).Select(path => (Path.GetFileName(path), File.ReadAllText(path)))]
            : [];

    /// <summary>The language a name or abbreviation means (case ignored), or null.</summary>
    public CorsixLanguage? Find(string name) =>
        _languages.FirstOrDefault(language => language.Names.Any(n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase)));

    /// <summary>The name of the file that declares a language (by any of its names), without its extension, or null.</summary>
    public string? FileStem(string language) =>
        _byName.TryGetValue(language.Trim(), out var file) ? Path.GetFileNameWithoutExtension(file.FileName) : null;

    /// <summary>
    /// The folder of a language's announcer speech under <paramref name="directory"/>, the folder of the language
    /// files (see <see cref="VoiceFolder"/>), or null for a language no file declares.
    /// </summary>
    public string? VoiceDirectory(string directory, string language) =>
        FileStem(language) is { } stem ? Path.Combine(directory, VoiceFolder, stem) : null;

    /// <summary>
    /// The number of the original's string file a language inherits last (<c>original_strings: n</c>, its own or
    /// through the languages it inherits), or null.
    /// </summary>
    public int? OriginalStringsNumber(string language)
    {
        int? number = null;
        Walk(language, []);
        return number;

        void Walk(string name, HashSet<string> seen)
        {
            if (!_byName.TryGetValue(name, out var file) || !seen.Add(file.FileName))
            {
                return;
            }

            foreach (var step in file.Steps)
            {
                switch (step)
                {
                    case InheritStep inherit:
                        Walk(inherit.Language, seen);
                        break;
                    case OriginalStringsStep original:
                        number = original.Number;
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Loads a language (CorsixTH's <c>Strings:load</c>): its steps in order, an inherited language's first where it
    /// is inherited. <paramref name="originalStrings"/> gives the original's strings for <c>original_strings: n</c>;
    /// when it gives none, names mapped to them keep what was set before. An unknown language falls back to English,
    /// as CorsixTH's <c>_loadPrivate</c>. CorsixNet's own strings for the language come last.
    /// </summary>
    public LanguageStrings Load(string language, Func<int, StringTable?> originalStrings)
    {
        var strings = new LanguageStrings();
        Evaluate(language, strings, originalStrings, null, []);

        // CorsixNet's own strings for the language, by any of its names; none for one without a file of them.
        var names = Find(language)?.Names ?? [language];
        if (_own is not null && names.Select(name => _own._byName.GetValueOrDefault(name)).FirstOrDefault(file => file is not null) is { } own)
        {
            _own.Run(own, strings, originalStrings, null, [own.FileName]);
        }

        return strings;
    }

    private void Evaluate(string language, LanguageStrings strings, Func<int, StringTable?> originalStrings, int? number, HashSet<string> running)
    {
        if (!_byName.TryGetValue(language, out var file) && !_byName.TryGetValue("English", out file))
        {
            return;
        }

        if (!running.Add(file.FileName))
        {
            return;
        }

        Run(file, strings, originalStrings, number, running);
        running.Remove(file.FileName);
    }

    private void Run(LanguageFile file, LanguageStrings strings, Func<int, StringTable?> originalStrings, int? number, HashSet<string> running)
    {
        foreach (var step in file.Steps)
        {
            switch (step)
            {
                case InheritStep inherit:
                    Evaluate(inherit.Language, strings, originalStrings, null, running);
                    break;
                case OriginalStringsStep original:
                    Evaluate(OriginalStrings, strings, originalStrings, original.Number, running);
                    break;
                case SetStep set:
                    foreach (var (path, text) in set.Strings)
                    {
                        strings.Set(path, text);
                    }

                    break;
                case RemoveStep remove:
                    foreach (var path in remove.Paths)
                    {
                        strings.Remove(path);
                    }

                    break;
                case CopyStep copy:
                    foreach (var (to, from) in copy.Copies)
                    {
                        foreach (var (key, text) in strings.Below(from))
                        {
                            strings.Set(to + key, text);
                        }
                    }

                    break;
                case OriginalStep names:
                    // original_strings.lua loads "Lang-" .. n .. ".dat" with the number it was inherited with.
                    Name(names, number is { } n ? originalStrings(n) : null, strings);
                    break;
            }
        }
    }

    /// <summary>The original's strings under their names: each mapped, and set where the table has it.</summary>
    private static void Name(OriginalStep names, StringTable? table, LanguageStrings strings)
    {
        foreach (var (path, section, index) in names.Names)
        {
            if (index is { } single)
            {
                strings.MapOriginal(section, single, path);
                if (table?.Get(section - 1, single - 1) is { } text)
                {
                    strings.Set(path, text);
                }
            }
            else if (table is not null)
            {
                // humanoid_name_starts = S[9]: the whole section as a list.
                for (var i = 1; i <= table.SectionSize(section - 1); i++)
                {
                    var item = path + "." + i.ToString(CultureInfo.InvariantCulture);
                    strings.MapOriginal(section, i, item);
                    if (table.Get(section - 1, i - 1) is { } text)
                    {
                        strings.Set(item, text);
                    }
                }
            }
        }
    }

    private abstract record Step;

    private sealed record InheritStep(string Language) : Step;

    private sealed record OriginalStringsStep(int Number) : Step;

    private sealed record SetStep(IReadOnlyList<(string Path, string Text)> Strings) : Step;

    private sealed record RemoveStep(IReadOnlyList<string> Paths) : Step;

    private sealed record CopyStep(IReadOnlyList<(string To, string From)> Copies) : Step;

    private sealed record OriginalStep(IReadOnlyList<(string Path, int Section, int? Index)> Names) : Step;

    /// <summary>A language file as read: its names, font and steps.</summary>
    private sealed record LanguageFile(string FileName, IReadOnlyList<string> Names, string? Font, IReadOnlyList<Step> Steps)
    {
        public static LanguageFile Parse(string fileName, string text)
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                return new LanguageFile(fileName, [], null, []);
            }

            List<string> names = [];
            string? font = null;
            var steps = new List<Step>();
            foreach (var (keyNode, value) in root.Children)
            {
                switch (Scalar(keyNode))
                {
                    case "language":
                        names = [.. List(value)];
                        break;
                    case "font":
                        font = Scalar(value);
                        break;
                    case "steps" when value is YamlSequenceNode sequence:
                        foreach (var item in sequence)
                        {
                            if (item is YamlMappingNode { Children: [var (stepKey, stepValue)] })
                            {
                                steps.Add(ReadStep(fileName, Scalar(stepKey), stepValue));
                            }
                            else
                            {
                                throw new InvalidDataException($"{fileName}, line {item.Start.Line}: a step is a mapping of one key.");
                            }
                        }

                        break;
                    case "strings":
                        steps.Add(new SetStep(Flatten(value)));
                        break;
                    default:
                        throw new InvalidDataException($"{fileName}, line {keyNode.Start.Line}: unknown key '{Scalar(keyNode)}'.");
                }
            }

            return new LanguageFile(fileName, names, font, steps);
        }

        private static Step ReadStep(string fileName, string kind, YamlNode value) => kind switch
        {
            "inherit" => new InheritStep(Scalar(value)),
            "original_strings" => new OriginalStringsStep(int.Parse(Scalar(value), NumberStyles.Integer, CultureInfo.InvariantCulture)),
            "strings" => new SetStep(Flatten(value)),
            "remove" => new RemoveStep([.. List(value)]),
            "copy" => new CopyStep([.. Flatten(value)]),
            "original" => new OriginalStep([.. ((YamlMappingNode)value).Children.Select(pair => OriginalName(Scalar(pair.Key), [.. List(pair.Value)]))]),
            _ => throw new InvalidDataException($"{fileName}, line {value.Start.Line}: unknown step '{kind}'."),
        };

        private static (string Path, int Section, int? Index) OriginalName(string path, List<string> numbers) =>
            (path, int.Parse(numbers[0], CultureInfo.InvariantCulture), numbers.Count > 1 ? int.Parse(numbers[1], CultureInfo.InvariantCulture) : null);

        private static string Scalar(YamlNode node) => node is YamlScalarNode { Value: { } value } ? value : string.Empty;

        private static IEnumerable<string> List(YamlNode node) =>
            node is YamlSequenceNode sequence ? sequence.Select(Scalar) : [Scalar(node)];

        /// <summary>The strings under a node by dotted name: mappings join keys, lists number items from 1.</summary>
        private static List<(string, string)> Flatten(YamlNode node)
        {
            var result = new List<(string, string)>();
            Walk(node, string.Empty);
            return result;

            void Walk(YamlNode current, string path)
            {
                switch (current)
                {
                    case YamlMappingNode mapping:
                        foreach (var (key, value) in mapping.Children)
                        {
                            Walk(value, Join(path, Scalar(key)));
                        }

                        break;
                    case YamlSequenceNode sequence:
                        for (var i = 0; i < sequence.Children.Count; i++)
                        {
                            Walk(sequence.Children[i], Join(path, (i + 1).ToString(CultureInfo.InvariantCulture)));
                        }

                        break;
                    case YamlScalarNode scalar:
                        result.Add((path, scalar.Value ?? string.Empty));
                        break;
                }
            }

            static string Join(string path, string key) => path.Length == 0 ? key : path + "." + key;
        }
    }
}

/// <summary>
/// A loaded language's strings by CorsixTH's dotted names (<c>adviser.warnings.money_low</c>; list items numbered from
/// 1, <c>vip_names.1</c>), and which name each of the original's strings was given.
/// </summary>
public sealed class LanguageStrings
{
    private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private readonly Dictionary<(int Section, int Index), string> _originalNames = [];

    /// <summary>Every string by name.</summary>
    public IReadOnlyDictionary<string, string> Strings => _strings;

    /// <summary>The string with this name, or null.</summary>
    public string? Get(string name) => _strings.GetValueOrDefault(name);

    /// <summary>
    /// The string original_strings.lua names after the original's <c>S[section][index]</c> (both 1-based), as this
    /// language has it; null when no name maps to it or the language has nothing there.
    /// </summary>
    public string? Original(int section, int index) =>
        _originalNames.TryGetValue((section, index), out var name) ? Get(name) : null;

    /// <summary>The name original_strings.lua first gives the original's <c>S[section][index]</c>, or null.</summary>
    public string? OriginalName(int section, int index) => _originalNames.GetValueOrDefault((section, index));

    /// <summary>The list under a name, items 1, 2, ... until one is missing.</summary>
    public IReadOnlyList<string> List(string name)
    {
        var list = new List<string>();
        for (var i = 1; Get(name + "." + i.ToString(CultureInfo.InvariantCulture)) is { } item; i++)
        {
            list.Add(item);
        }

        return list;
    }

    internal void Set(string name, string text) => _strings[name] = text;

    internal void Remove(string name) => _strings.Remove(name);

    internal void MapOriginal(int section, int index, string name) => _originalNames.TryAdd((section, index), name);

    /// <summary>The strings under a table's name, keyed by the rest of their names (starting with a dot).</summary>
    internal List<(string Key, string Text)> Below(string name)
    {
        var prefix = name + ".";
        return [.. _strings.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(pair => (pair.Key[name.Length..], pair.Value))];
    }
}
