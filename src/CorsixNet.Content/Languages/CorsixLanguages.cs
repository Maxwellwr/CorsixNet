// CorsixTH's own translations: the language files (CorsixTH/Lua/languages/*.lua) evaluated as CorsixTH's strings.lua
// does (Strings:init's Language() names, Strings:load's merging environment, Inherit, utf8/cp437, LoadStrings), and
// original_strings.lua's mapping of string names to the original's S[section][index].
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Formats;

namespace CorsixNet.Content.Languages;

/// <summary>
/// A language CorsixTH's files declare: its names (<c>Language(...)</c>, the first in the language itself, the second in
/// English, then abbreviations) and its font (<c>Font(...)</c>: "cp437" or none for the game's own font, "mik" for the
/// Russian original's, "unicode" for a TrueType font).
/// </summary>
public sealed record CorsixLanguage(IReadOnlyList<string> Names, string? Font)
{
    /// <summary>The name in the language itself.</summary>
    public string NativeName => Names[0];

    /// <summary>The English name, as config.txt's <c>language</c> takes it.</summary>
    public string EnglishName => Names.Count > 1 ? Names[1] : Names[0];

    /// <summary>Whether the original's bitmap font draws it (code page 437).</summary>
    public bool UsesGameFont => Font is null or "cp437";
}

/// <summary>
/// The language files, read once: which language each declares, and their statements to evaluate when one is loaded.
/// </summary>
public sealed class CorsixLanguages
{
    /// <summary>The pseudo language that maps names to the original's strings.</summary>
    public const string OriginalStrings = "original_strings";

    private readonly Dictionary<string, Chunk> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CorsixLanguage> _languages = [];

    public CorsixLanguages(IEnumerable<(string FileName, string Text)> files)
    {
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

    /// <summary>Reads every <c>*.lua</c> file of a folder; none when the folder is missing.</summary>
    public static CorsixLanguages Load(string directory) =>
        new(Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.lua").Select(path => (Path.GetFileName(path), File.ReadAllText(path)))
            : []);

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

        new Evaluator(this, strings, originalStrings, arguments, running).Run(chunk.Statements);
        running.Remove(chunk.FileName);
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

    private sealed class Evaluator(CorsixLanguages languages, LanguageStrings strings, Func<int, StringTable?> originalStrings, object?[] arguments, HashSet<string> running)
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
                    strings.Set(path, text);
                    break;
                case decimal number:
                    strings.Set(path, number.ToString(CultureInfo.InvariantCulture));
                    break;
                case OriginalString original:
                    strings.MapOriginal(original.Section, original.Index, path);
                    if (original.Text is { } originalText)
                    {
                        strings.Set(path, originalText);
                    }

                    break;
                case TableValue table:
                    foreach (var (key, entry) in table.Entries)
                    {
                        Merge(path + "." + key, entry);
                    }

                    break;
                case EnvironmentTable source when source.Path != path:
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

    internal void MapOriginal(int section, int index, string name) => _originalNames.TryAdd((section, index), name);

    /// <summary>The strings under a table's name, keyed by the rest of their names (starting with a dot).</summary>
    internal List<(string Key, string Text)> Below(string name)
    {
        var prefix = name + ".";
        return [.. _strings.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(pair => (pair.Key[name.Length..], pair.Value))];
    }
}
