// CorsixTH's own translations: the language files (CorsixTH/Lua/languages/*.lua) evaluated as CorsixTH's strings.lua
// does (Strings:init's Language() names, Strings:load's merging environment, Inherit, utf8/cp437, LoadStrings), and
// original_strings.lua's mapping of string names to the original's S[section][index].
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Content.Languages;
using CorsixNet.Formats;

namespace CorsixNet.LuaConverter;

/// <summary>
/// The language files, read once: which language each declares, and their statements to evaluate when one is loaded.
/// </summary>
public sealed class LuaLanguages
{
    /// <summary>The pseudo language that maps names to the original's strings.</summary>
    public const string OriginalStrings = "original_strings";

    /// <summary>The folder beside CorsixTH's files that holds CorsixNet's own strings, in the same form.</summary>
    public const string OwnStringsFolder = "CorsixNet";

    private readonly Dictionary<string, Chunk> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CorsixLanguage> _languages = [];
    private readonly LuaLanguages? _own;

    /// <param name="files">CorsixTH's language files.</param>
    /// <param name="ownFiles">
    /// CorsixNet's own strings for the interface CorsixTH has no text for, one file per language declared with
    /// <c>Language(...)</c> as CorsixTH's are; a loaded language gets its file's strings over CorsixTH's.
    /// </param>
    public LuaLanguages(IEnumerable<(string FileName, string Text)> files, IEnumerable<(string FileName, string Text)>? ownFiles = null)
    {
        _own = ownFiles is null ? null : new LuaLanguages(ownFiles);
        foreach (var (fileName, text) in files.OrderBy(file => file.FileName, StringComparer.Ordinal))
        {
            IReadOnlyList<LuaStatement> statements;
            try
            {
                statements = LuaChunk.Parse(text);
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException($"{fileName}: {exception.Message}", exception);
            }

            // Strings:init runs each file with Language() and Font() recording the names, the rest doing nothing.
            string? font = null;
            List<string>? names = null;
            foreach (var call in statements.OfType<LuaCallStatement>().Select(statement => statement.Call))
            {
                switch ((call.Function as LuaName)?.Name)
                {
                    case "Font" when call.Arguments is [LuaStringLiteral value, ..]:
                        font = value.Value;
                        break;
                    case "Language" when names is null:
                        names = [.. call.Arguments.Select(ConstantText).OfType<string>()];
                        break;
                }
            }

            if (names is not { Count: > 0 })
            {
                continue;
            }

            var chunk = new Chunk(fileName, statements);
            foreach (var name in names)
            {
                _byName.TryAdd(name, chunk);
            }

            if (!string.Equals(names[0], OriginalStrings, StringComparison.Ordinal))
            {
                _languages.Add(new CorsixLanguage(names, font));
            }
        }
    }

    /// <summary>The languages that can be chosen, by file name (original_strings left out).</summary>
    public IReadOnlyList<CorsixLanguage> Languages => _languages;

    /// <summary>
    /// Reads every <c>*.lua</c> file of a folder, and CorsixNet's own strings from its <c>CorsixNet</c> folder; none
    /// when a folder is missing.
    /// </summary>
    public static LuaLanguages Load(string directory) =>
        new(Files(directory), Files(Path.Combine(directory, OwnStringsFolder)));

    private static List<(string FileName, string Text)> Files(string directory) =>
        Directory.Exists(directory)
            ? [.. Directory.EnumerateFiles(directory, "*.lua").Select(path => (Path.GetFileName(path), File.ReadAllText(path)))]
            : [];

    /// <summary>The language a name or abbreviation means (case ignored), or null.</summary>
    public CorsixLanguage? Find(string name) =>
        _languages.FirstOrDefault(language => language.Names.Any(n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// The number of the original's string file a language inherits last (<c>Inherit("original_strings", n)</c>, its own
    /// or through the languages it inherits), or null.
    /// </summary>
    public int? OriginalStringsNumber(string language)
    {
        int? number = null;
        Walk(language, []);
        return number;

        void Walk(string name, HashSet<string> seen)
        {
            if (!_byName.TryGetValue(name, out var chunk) || !seen.Add(chunk.FileName))
            {
                return;
            }

            foreach (var call in chunk.Statements.OfType<LuaCallStatement>().Select(statement => statement.Call))
            {
                if (call.Function is LuaName { Name: "Inherit" } && call.Arguments is [LuaStringLiteral parent, ..] arguments)
                {
                    if (string.Equals(parent.Value, OriginalStrings, StringComparison.OrdinalIgnoreCase))
                    {
                        number = arguments.Count > 1 && arguments[1] is LuaNumberLiteral n ? (int)n.Value : 0;
                    }
                    else
                    {
                        Walk(parent.Value, seen);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Evaluates a language (CorsixTH's <c>Strings:load</c>): its file in an environment where assignments merge into
    /// the strings and <c>Inherit</c> evaluates another language first. <paramref name="originalStrings"/> gives the
    /// original's strings for <c>Inherit("original_strings", n)</c>; when it gives none, names mapped to them keep
    /// what was assigned before. An unknown language falls back to English, as CorsixTH's <c>_loadPrivate</c>.
    /// </summary>
    public LanguageStrings Load(string language, Func<int, StringTable?> originalStrings)
    {
        var strings = new LanguageStrings();
        Evaluate(language, strings, originalStrings, [], []);

        // CorsixNet's own strings for the language, by any of its names; none for one without a file of them.
        var names = Find(language)?.Names ?? [language];
        if (_own is not null && names.Select(name => _own._byName.GetValueOrDefault(name)).FirstOrDefault(chunk => chunk is not null) is { } own)
        {
            new Evaluator(_own, strings, originalStrings, [], [own.FileName], null).Run(own.Statements);
        }

        return strings;
    }

    private void Evaluate(string language, LanguageStrings strings, Func<int, StringTable?> originalStrings, object?[] arguments, HashSet<string> running)
    {
        if (!_byName.TryGetValue(language, out var chunk) && !_byName.TryGetValue("English", out chunk))
        {
            return;
        }

        if (!running.Add(chunk.FileName))
        {
            return;
        }

        new Evaluator(this, strings, originalStrings, arguments, running, null).Run(chunk.Statements);
        running.Remove(chunk.FileName);
    }

    /// <summary>
    /// What each file does in order, for writing it out as YAML: its inherits, the strings it sets (values worked out
    /// as CorsixTH would), removes, copies and maps to the original's strings.
    /// </summary>
    public IEnumerable<RecordedFile> Record()
    {
        foreach (var chunk in _byName.Values.Distinct().OrderBy(chunk => chunk.FileName, StringComparer.Ordinal))
        {
            string? font = null;
            List<string>? names = null;
            foreach (var call in chunk.Statements.OfType<LuaCallStatement>().Select(statement => statement.Call))
            {
                switch ((call.Function as LuaName)?.Name)
                {
                    case "Font" when call.Arguments is [LuaStringLiteral value, ..]:
                        font = value.Value;
                        break;
                    case "Language" when names is null:
                        names = [.. call.Arguments.Select(ConstantText).OfType<string>()];
                        break;
                }
            }

            var steps = new List<Step>();
            object?[] arguments = names is [OriginalStrings, ..] ? [0m] : [];
            new Evaluator(this, new LanguageStrings(), _ => null, arguments, [chunk.FileName], steps).Run(chunk.Statements);
            yield return new RecordedFile(chunk.FileName, names ?? [], font, steps);
        }
    }

    private static string? ConstantText(LuaExpression expression) => expression switch
    {
        LuaStringLiteral text => text.Value,
        LuaCall { Function: LuaName { Name: "utf8" or "cp437" }, Arguments: [LuaStringLiteral text] } => text.Value,
        _ => null,
    };

    private sealed record Chunk(string FileName, IReadOnlyList<LuaStatement> Statements);

    /// <summary>A table of the strings environment, by its dotted path.</summary>
    private sealed record EnvironmentTable(string Path);

    /// <summary>A table constructor's value: its entries in order.</summary>
    private sealed record TableValue(List<(string Key, object? Value)> Entries);

    /// <summary>A string from the original's strings, and where it came from.</summary>
    private sealed record OriginalString(int Section, int Index, string? Text);

    /// <summary><c>LoadStrings(...)</c>'s table, or one of its sections.</summary>
    private sealed record OriginalTable(StringTable? Strings, int? Section);

    /// <summary>One of the environment's functions.</summary>
    private sealed record Function(string Name);

    /// <summary>What this reader does not work out (loops, local functions' results, arithmetic).</summary>
    private sealed record Unknown
    {
        public static readonly Unknown Value = new();
    }

    private sealed class Evaluator(LuaLanguages languages, LanguageStrings strings, Func<int, StringTable?> originalStrings, object?[] arguments, HashSet<string> running, List<Step>? record)
    {
        private static readonly HashSet<string> Functions = new(StringComparer.Ordinal)
        {
            "utf8", "cp437", "Language", "Font", "Inherit", "Encoding", "LoadStrings", "SetSpeechFile", "IsArabicNumerals", "ipairs", "pairs", "type",
        };

        private readonly Dictionary<string, object?> _locals = new(StringComparer.Ordinal);

        public void Run(IReadOnlyList<LuaStatement> statements)
        {
            foreach (var statement in statements)
            {
                switch (statement)
                {
                    case LuaCallStatement { Call: var call }:
                        Call(call);
                        break;
                    case LuaAssignment { IsLocal: true } local:
                        for (var i = 0; i < local.Targets.Count; i++)
                        {
                            _locals[((LuaName)local.Targets[i]).Name] = i < local.Values.Count ? Evaluate(local.Values[i]) : null;
                        }

                        break;
                    case LuaAssignment assignment:
                        for (var i = 0; i < assignment.Targets.Count && i < assignment.Values.Count; i++)
                        {
                            Assign(assignment.Targets[i], Evaluate(assignment.Values[i]));
                        }

                        break;
                }
            }
        }

        private object? Call(LuaCall call)
        {
            var function = call.Function is LuaName { Name: var name } && !_locals.ContainsKey(name) && Functions.Contains(name) ? name : null;
            switch (function)
            {
                case "utf8" or "cp437":
                    return call.Arguments is [var argument] ? Text(Evaluate(argument)) : Unknown.Value;
                case "Inherit" when call.Arguments.Count > 0 && Text(Evaluate(call.Arguments[0])) is { } parent:
                    if (string.Equals(parent, OriginalStrings, StringComparison.OrdinalIgnoreCase))
                    {
                        record?.Add(new Step.OriginalStrings(call.Arguments.Count > 1 && Evaluate(call.Arguments[1]) is decimal n ? (int)n : 0));
                    }
                    else
                    {
                        record?.Add(new Step.Inherit(parent));
                    }

                    languages.Evaluate(parent, strings, originalStrings, [.. call.Arguments.Skip(1).Select(Evaluate)], running);
                    return null;
                case "LoadStrings":
                    // original_strings.lua loads "Lang-" .. n .. ".dat" with the number it was inherited with.
                    return new OriginalTable(arguments is [decimal number, ..] ? originalStrings((int)number) : null, null);
                case "Language" or "Font" or "Encoding" or "SetSpeechFile" or "IsArabicNumerals" or "Inherit":
                    return null;
                default:
                    return Unknown.Value;
            }
        }

        private object? Evaluate(LuaExpression expression)
        {
            switch (expression)
            {
                case LuaStringLiteral text:
                    return text.Value;
                case LuaNumberLiteral number:
                    return number.Value;
                case LuaBooleanLiteral boolean:
                    return boolean.Value;
                case LuaNilLiteral:
                    return null;
                case LuaVarArgs:
                    return arguments.Length > 0 ? arguments[0] : null;
                case LuaName { Name: var name }:
                    if (_locals.TryGetValue(name, out var local))
                    {
                        return local;
                    }

                    return Functions.Contains(name) ? new Function(name) : Lookup(name);
                case LuaIndex index:
                    return Index(Evaluate(index.Target), Evaluate(index.Key));
                case LuaCall call:
                    return Call(call);
                case LuaBinary { Operator: "..", Left: var left, Right: var right }:
                    return Text(Evaluate(left)) is { } a && Text(Evaluate(right)) is { } b ? a + b : Unknown.Value;
                case LuaTableConstructor table:
                    return Table(table);
                default:
                    return Unknown.Value;
            }
        }

        private TableValue Table(LuaTableConstructor table)
        {
            var entries = new List<(string, object?)>();
            var position = 1;
            foreach (var field in table.Fields)
            {
                var value = Evaluate(field.Value);
                if (field.Key is null)
                {
                    entries.Add((position++.ToString(CultureInfo.InvariantCulture), value));
                }
                else if (Key(Evaluate(field.Key)) is { } key)
                {
                    entries.Add((key, value));
                }
            }

            return new TableValue(entries);
        }

        private object? Index(object? target, object? key)
        {
            switch (target)
            {
                case OriginalTable { Section: null } table when key is decimal section:
                    return table with { Section = (int)section };
                case OriginalTable { Section: { } section } table when key is decimal index:
                    var text = table.Strings?.Get(section - 1, (int)index - 1);
                    return new OriginalString(section, (int)index, text);
                case EnvironmentTable table when Key(key) is { } name:
                    return Lookup(table.Path + "." + name);
                case TableValue table when Key(key) is { } name:
                    return table.Entries.LastOrDefault(entry => entry.Key == name).Value;
                default:
                    return Unknown.Value;
            }
        }

        /// <summary>The environment's value at a path: a string, or the (endless) table there.</summary>
        private object Lookup(string path) => strings.Get(path) is { } text ? text : new EnvironmentTable(path);

        private void Assign(LuaExpression target, object? value)
        {
            switch (target)
            {
                case LuaName { Name: var name } when _locals.ContainsKey(name):
                    _locals[name] = value;
                    break;
                case LuaName { Name: var name }:
                    Merge(name, value);
                    break;
                case LuaIndex index when Evaluate(index.Target) is EnvironmentTable table && Key(Evaluate(index.Key)) is { } key:
                    Merge(table.Path + "." + key, value);
                    break;
            }
        }

        /// <summary>The environment's <c>__newindex</c>: strings are set, tables merged into the table there.</summary>
        private void Merge(string path, object? value)
        {
            switch (value)
            {
                case string text:
                    record?.Add(new Step.Set(path, text));
                    strings.Set(path, text);
                    break;
                case decimal number:
                    record?.Add(new Step.Set(path, number.ToString(CultureInfo.InvariantCulture)));
                    strings.Set(path, number.ToString(CultureInfo.InvariantCulture));
                    break;
                case OriginalString original:
                    record?.Add(new Step.Original(path, original.Section, original.Index));
                    strings.MapOriginal(original.Section, original.Index, path);
                    if (original.Text is { } originalText)
                    {
                        strings.Set(path, originalText);
                    }

                    break;
                case null:
                    // staff_descriptions.misc[38] = nil
                    record?.Add(new Step.Remove(path));
                    strings.Remove(path);
                    break;
                case OriginalTable { Section: { } wholeSection } when record is not null:
                    record.Add(new Step.Original(path, wholeSection, null));
                    break;
                case OriginalTable { Strings: { } original, Section: { } section }:
                    // humanoid_name_starts = S[9]: the whole section as a list.
                    for (var index = 1; index <= original.SectionSize(section - 1); index++)
                    {
                        Merge(path + "." + index.ToString(CultureInfo.InvariantCulture), new OriginalString(section, index, original.Get(section - 1, index - 1)));
                    }

                    break;
                case TableValue table:
                    foreach (var (key, entry) in table.Entries)
                    {
                        Merge(path + "." + key, entry);
                    }

                    break;
                case EnvironmentTable source when source.Path != path:
                    record?.Add(new Step.Copy(path, source.Path));
                    foreach (var (key, copied) in strings.Below(source.Path))
                    {
                        strings.Set(path + key, copied);
                    }

                    break;
            }
        }

        private static string? Text(object? value) => value switch
        {
            string text => text,
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            OriginalString original => original.Text,
            _ => null,
        };

        private static string? Key(object? value) => value switch
        {
            string text => text,
            decimal number when number == decimal.Truncate(number) => number.ToString("0", CultureInfo.InvariantCulture),
            _ => null,
        };
    }
}


/// <summary>A language file's declaration and what it does, in order.</summary>
public sealed record RecordedFile(string FileName, IReadOnlyList<string> Names, string? Font, IReadOnlyList<Step> Steps);

/// <summary>One thing a language file does.</summary>
public abstract record Step
{
    /// <summary><c>Inherit("language")</c>: that language first.</summary>
    public sealed record Inherit(string Language) : Step;

    /// <summary><c>Inherit("original_strings", n)</c>: the original's strings of LANG-n.DAT under their names.</summary>
    public sealed record OriginalStrings(int Number) : Step;

    /// <summary>A string set.</summary>
    public sealed record Set(string Path, string Text) : Step;

    /// <summary>A string removed (<c>= nil</c>).</summary>
    public sealed record Remove(string Path) : Step;

    /// <summary>A table copied from another name.</summary>
    public sealed record Copy(string To, string From) : Step;

    /// <summary>A name given to the original's <c>S[section][index]</c>, or to a whole section as a list.</summary>
    public sealed record Original(string Path, int Section, int? Index) : Step;
}
