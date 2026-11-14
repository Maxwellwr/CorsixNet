// Turns CorsixTH's Lua files into the YAML CorsixNet reads; the game itself reads no Lua.
//
//   dotnet run --project tools/CorsixNet.LuaConverter -- languages <folder of .lua files> <folder for the .yaml files>
//   dotnet run --project tools/CorsixNet.LuaConverter -- campaign <file.campaign> [<file.campaign.yaml>]
//   dotnet run --project tools/CorsixNet.LuaConverter -- config <config.txt> [<config.yaml>]
//   dotnet run --project tools/CorsixNet.LuaConverter -- hotkeys <hotkeys.txt> [<hotkeys.yaml>]
//
// languages also loads every language both ways and checks they give the same strings.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using System.Text;
using CorsixNet.Content.Languages;
using CorsixNet.Formats;
using CorsixNet.LuaConverter;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

const string Usage = """
    usage: CorsixNet.LuaConverter languages <folder of .lua language files> <folder for the .yaml files>
           CorsixNet.LuaConverter campaign <file.campaign> [<file.campaign.yaml>]
           CorsixNet.LuaConverter config <config.txt> [<config.yaml>]
           CorsixNet.LuaConverter hotkeys <hotkeys.txt> [<hotkeys.yaml>]
    """;

try
{
    switch (args)
    {
        case ["languages", var input, var output]:
            return Languages(input, output);
        case ["campaign", var input, ..] when args.Length <= 3:
            return Convert(input, args.Length > 2 ? args[2] : Path.ChangeExtension(input, null) + CustomCampaign.YamlExtension, LuaFiles.Campaign);
        case ["config", var input, ..] when args.Length <= 3:
            return Convert(input, args.Length > 2 ? args[2] : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input)) ?? ".", CorsixNet.Content.Settings.GameConfig.FileName), LuaFiles.Config);
        case ["hotkeys", var input, ..] when args.Length <= 3:
            return Convert(input, args.Length > 2 ? args[2] : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input)) ?? ".", CorsixNet.Content.Settings.Hotkeys.FileName), LuaFiles.Hotkeys);
        default:
            Console.Error.WriteLine(Usage);
            return 2;
    }
}
catch (InvalidDataException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

static int Convert(string input, string output, Func<string, string> convert)
{
    File.WriteAllText(output, convert(File.ReadAllText(input)), new UTF8Encoding(false));
    Console.WriteLine($"{input} -> {output}");
    return 0;
}

static int Languages(string input, string output)
{
    var files = Directory.EnumerateFiles(input, "*.lua").Select(path => (Path.GetFileName(path), File.ReadAllText(path))).ToList();
    var lua = new LuaLanguages(files);
    Directory.CreateDirectory(output);
    foreach (var file in lua.Record())
    {
        var path = Path.Combine(output, Path.ChangeExtension(file.FileName, CorsixLanguages.Extension));
        var header = LuaFiles.LicenceHeader(files.First(lua => lua.Item1 == file.FileName).Item2);
        File.WriteAllText(path, YamlWriter.Write(file, header), new UTF8Encoding(false));
        Console.WriteLine($"{file.FileName} -> {path}");
    }

    // The same strings both ways, with the original's strings and without.
    var yaml = CorsixLanguages.Load(output);
    var failures = 0;
    foreach (var language in lua.Languages)
    {
        foreach (var (label, originals) in new (string, Func<int, StringTable?>)[] { ("with LANG files", n => SyntheticStrings(n)), ("without", _ => null) })
        {
            var expected = lua.Load(language.EnglishName, originals).Strings;
            var actual = yaml.Load(language.EnglishName, originals).Strings;
            var differences = expected.Keys.Union(actual.Keys)
                .Where(key => !string.Equals(expected.GetValueOrDefault(key), actual.GetValueOrDefault(key), StringComparison.Ordinal))
                .ToList();
            if (differences.Count > 0)
            {
                failures++;
                Console.Error.WriteLine($"{language.EnglishName} ({label}): {differences.Count} differ, e.g. {string.Join(", ", differences.Take(5))}");
            }
        }
    }

    Console.WriteLine(failures == 0 ? $"{lua.Languages.Count} languages give the same strings from YAML." : $"{failures} differences.");
    return failures == 0 ? 0 : 1;
}

// Every string "n section.index", 64 sections of 230, as LANG-n.DAT stands in.
static StringTable SyntheticStrings(int number)
{
    var data = new List<byte> { 64, 0 };
    for (var section = 0; section < 64; section++)
    {
        data.Add(230);
        data.Add(0);
    }

    for (var section = 1; section <= 64; section++)
    {
        for (var index = 1; index <= 230; index++)
        {
            data.AddRange(Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{number} {section}.{index}")));
            data.Add(0);
        }
    }

    return StringTable.Parse([.. data]);
}

/// <summary>A recorded language file as YAML: consecutive steps of a kind together, strings nested by name.</summary>
internal static class YamlWriter
{
    public static string Write(RecordedFile file, string header)
    {
        var root = new YamlMappingNode();
        var names = new YamlSequenceNode(file.Names.Select(name => new YamlScalarNode(name))) { Style = YamlDotNet.Core.Events.SequenceStyle.Flow };
        root.Add("language", names);
        if (file.Font is { } font)
        {
            root.Add("font", font);
        }

        var steps = new YamlSequenceNode();
        root.Add("steps", steps);
        var i = 0;
        while (i < file.Steps.Count)
        {
            var step = file.Steps[i];
            switch (step)
            {
                case Step.Inherit inherit:
                    steps.Add(One("inherit", new YamlScalarNode(inherit.Language)));
                    i++;
                    break;
                case Step.OriginalStrings original:
                    steps.Add(One("original_strings", new YamlScalarNode(original.Number.ToString(CultureInfo.InvariantCulture))));
                    i++;
                    break;
                case Step.Remove:
                    steps.Add(One("remove", new YamlSequenceNode(Run<Step.Remove>(file.Steps, ref i).Select(remove => new YamlScalarNode(remove.Path)))));
                    break;
                case Step.Copy:
                    var copies = new YamlMappingNode();
                    foreach (var copy in Run<Step.Copy>(file.Steps, ref i))
                    {
                        copies.Add(copy.To, copy.From);
                    }

                    steps.Add(One("copy", copies));
                    break;
                case Step.Original:
                    var originals = new YamlMappingNode();
                    foreach (var original in Run<Step.Original>(file.Steps, ref i))
                    {
                        if (originals.Children.ContainsKey(new YamlScalarNode(original.Path)))
                        {
                            // A name mapped twice keeps its first mapping (MapOriginal's TryAdd); the text is the last.
                            steps.Add(One("original", originals));
                            originals = new YamlMappingNode();
                        }

                        List<YamlNode> numbers = [new YamlScalarNode(original.Section.ToString(CultureInfo.InvariantCulture))];
                        if (original.Index is { } index)
                        {
                            numbers.Add(new YamlScalarNode(index.ToString(CultureInfo.InvariantCulture)));
                        }

                        originals.Add(original.Path, new YamlSequenceNode(numbers) { Style = YamlDotNet.Core.Events.SequenceStyle.Flow });
                    }

                    steps.Add(One("original", originals));
                    break;
                case Step.Set:
                    var tree = new Tree();
                    while (i < file.Steps.Count && file.Steps[i] is Step.Set set && tree.TryAdd(set.Path, set.Text))
                    {
                        i++;
                    }

                    steps.Add(One("strings", tree.ToYaml()));
                    break;
            }
        }

        var text = new StringWriter();
        text.WriteLine($"# CorsixTH's {file.FileName} (CorsixTH/Lua/languages, MIT), written out as YAML by tools/CorsixNet.LuaConverter.");
        text.WriteLine("# Fix a translation upstream in CorsixTH and import it again, or add CorsixNet's own strings in Languages/CorsixNet.");
        if (header.Length > 0)
        {
            text.WriteLine("#");
            foreach (var line in header.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                text.WriteLine(line.Length == 0 ? "#" : "# " + line.TrimEnd());
            }
        }

        var emitter = new Emitter(text, new EmitterSettings(2, int.MaxValue, false, 1024));
        new YamlStream(new YamlDocument(root)).Save(emitter, false);
        var result = text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        return result.EndsWith("...\n", StringComparison.Ordinal) ? result[..^4] : result;
    }

    private static YamlMappingNode One(string key, YamlNode value) => new() { { key, value } };

    private static List<T> Run<T>(IReadOnlyList<Step> steps, ref int i)
        where T : Step
    {
        var run = new List<T>();
        while (i < steps.Count && steps[i] is T step)
        {
            run.Add(step);
            i++;
        }

        return run;
    }

    /// <summary>Strings nested by the parts of their names, in the order they were set.</summary>
    private sealed class Tree
    {
        private readonly List<(string Key, object Value)> _entries = [];

        /// <summary>Adds a string, or refuses one that a string or a table already stands in the way of.</summary>
        public bool TryAdd(string path, string text)
        {
            var parts = path.Split('.');
            var node = this;
            for (var p = 0; p < parts.Length; p++)
            {
                var index = node._entries.FindIndex(entry => entry.Key == parts[p]);
                if (p == parts.Length - 1)
                {
                    if (index >= 0)
                    {
                        if (node._entries[index].Value is not string)
                        {
                            return false;
                        }

                        node._entries[index] = (parts[p], text);
                    }
                    else
                    {
                        node._entries.Add((parts[p], text));
                    }

                    return true;
                }

                if (index < 0)
                {
                    var child = new Tree();
                    node._entries.Add((parts[p], child));
                    node = child;
                }
                else if (node._entries[index].Value is Tree child)
                {
                    node = child;
                }
                else
                {
                    return false;
                }
            }

            return false;
        }

        public YamlNode ToYaml()
        {
            // Items 1, 2, 3... in order are a list, as CorsixTH writes { "a", "b" }.
            if (_entries.Count > 0 && _entries.Select((entry, i) => entry.Key == (i + 1).ToString(CultureInfo.InvariantCulture)).All(ok => ok))
            {
                return new YamlSequenceNode(_entries.Select(entry => Node(entry.Value)));
            }

            var mapping = new YamlMappingNode();
            foreach (var (key, value) in _entries)
            {
                mapping.Add(key, Node(value));
            }

            return mapping;
        }

        private static YamlNode Node(object value) => value is Tree tree ? tree.ToYaml() : new YamlScalarNode((string)value);
    }
}
