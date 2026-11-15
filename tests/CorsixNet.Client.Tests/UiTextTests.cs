using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using CorsixNet.Client.Ui;
using CorsixNet.Content.Languages;

namespace CorsixNet.Client.Tests;

/// <summary>The interface's own strings (<c>corsixnet.*</c>) and CorsixTH's placeholders.</summary>
public partial class UiTextTests
{
    [Theory]
    [InlineData("Save as \"%1%\"", "Save as \"x\"")]
    [InlineData("%2% before %1%", "y before x")]
    [InlineData("%s and %d", "x and y")]
    [InlineData("%d%% done", "x% done")]
    [InlineData("Heating: %1% %", "Heating: x %")]
    [InlineData("%1%%2%", "xy")]
    [InlineData("no placeholders", "no placeholders")]
    public void FillsCorsixThsPlaceholders(string template, string expected) =>
        Assert.Equal(expected, UiText.Fill(template, "x", "y"));

    [Fact]
    public void LabelsDropTheMenusHotkeysAndPadding() =>
        Assert.Equal("LOAD", UiText.Label("menu_file.load", "  (%1%) LOAD  "));

    /// <summary>The languages with a file of CorsixNet's own strings, by English name.</summary>
    public static TheoryData<string> OwnLanguages()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Languages", CorsixLanguages.OwnStringsFolder);
        var files = Directory.EnumerateFiles(folder, "*" + CorsixLanguages.Extension).Select(path => (Path.GetFileName(path), File.ReadAllText(path)));
        return [.. new CorsixLanguages(files).Languages.Select(language => language.EnglishName)];
    }

    [Theory]
    [MemberData(nameof(OwnLanguages))]
    public void ATranslationHasEveryOwnStringTheClientUses(string language)
    {
        var strings = CorsixLanguages.Load(Path.Combine(AppContext.BaseDirectory, "Languages")).Load(language, _ => null);
        var missing = OwnNames().Where(name => strings.Get(name) is not { Length: > 0 }).ToList();

        Assert.True(missing.Count == 0, $"Missing in Languages/CorsixNet for {language}: " + string.Join(", ", missing));
    }

    [Fact]
    public void OwnStringsAreLaidOverCorsixThsTranslation()
    {
        var russian = CorsixLanguages.Load(Path.Combine(AppContext.BaseDirectory, "Languages")).Load("Russian", _ => null);

        Assert.Equal("Звуковые эффекты", russian.Get("corsixnet.options.sound_effects"));
        Assert.Equal("Тряска экрана", russian.Get("accessibility_window.enable_screen_shake"));
        Assert.Equal("Настройки", russian.Get("options_window.caption"));
        Assert.Equal("Внимание! Больницей управляет мошенник!", russian.Get("subtitles.cheat002"));
    }

    /// <summary>The <c>corsixnet.*</c> names in the client's code.</summary>
    private static SortedSet<string> OwnNames()
    {
        // The build output may lie outside the checkout (--artifacts-path), so this file's own path is tried too.
        var directory = RepositoryRoot(AppContext.BaseDirectory) ?? RepositoryRoot(ThisFile());
        Assert.NotNull(directory);
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(directory.FullName, "src", "CorsixNet.Client"), "*.cs", SearchOption.AllDirectories))
        {
            foreach (Match match in OwnName().Matches(File.ReadAllText(file)))
            {
                names.Add(match.Groups[1].Value);
            }
        }

        Assert.NotEmpty(names);
        return names;
    }

    /// <summary>The checkout holding a path: the nearest folder above it with CorsixNet.slnx, or null.</summary>
    private static DirectoryInfo? RepositoryRoot(string path)
    {
        var directory = new DirectoryInfo(path);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CorsixNet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory;
    }

    private static string ThisFile([CallerFilePath] string path = "") => Path.GetDirectoryName(path) ?? path;

    [GeneratedRegex("\"(corsixnet\\.[a-z0-9_.]+)\"")]
    private static partial Regex OwnName();
}
