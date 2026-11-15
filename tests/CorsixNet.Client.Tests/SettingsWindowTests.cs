using CorsixNet.Client.Ui.Windows;
using CorsixNet.Content.Saves;
using CorsixNet.Content.Settings;

namespace CorsixNet.Client.Tests;

/// <summary>The save and load windows, the options and the menu.</summary>
public sealed class SettingsWindowTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "corsixnet-saves-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void SavingMakesAFileThatLoadingLists()
    {
        var host = new WindowTestHost();
        var store = new SaveGameStore(_folder);
        var saved = new List<string>();
        var loaded = new List<string>();
        bool Save(string path)
        {
            SaveGameStore.Save(path, "TEST", host.Simulation);
            saved.Add(path);
            return true;
        }

        var saving = new SaveLoadWindow(store, saving: true, Save, path => false);
        host.Opened.Add(saving);
        Assert.Contains("No saved games yet.", host.Lines(saving));
        host.Click(saving, host.Lines(saving).First(line => line.StartsWith("Save as", StringComparison.Ordinal)));
        Assert.True(File.Exists(Assert.Single(saved)));
        Assert.Empty(host.Opened);

        var loading = new SaveLoadWindow(store, saving: false, path => false, path =>
        {
            loaded.Add(path);
            return true;
        });
        host.Click(loading, "Load");
        Assert.Equal(saved, loaded);
    }

    [Fact]
    public void ANewSaveCanBeGivenAName()
    {
        var host = new WindowTestHost();
        var store = new SaveGameStore(_folder);
        var saved = new List<string>();
        var window = new SaveLoadWindow(store, saving: true, p =>
        {
            saved.Add(p);
            return true;
        }, p => false);
        host.Opened.Add(window);

        host.Click(window, "Type a name for a new save");
        Assert.True(window.IsCapturing);
        foreach (var key in new[] { new Hotkey("m", Shift: true), new("y"), new("space"), new("x"), new("backspace"), new("2"), new("-", Shift: true), new("a") })
        {
            window.Capture(key);
        }

        Assert.Equal("My 2_a", window.TypedName);
        window.Capture(new Hotkey("return"));
        Assert.False(window.IsCapturing);
        _ = host.Lines(window);
        Assert.Equal([Path.Combine(_folder, "My 2_a.sav")], saved);
    }
    [Fact]
    public void OverwritingAsksFirst()
    {
        var host = new WindowTestHost();
        var store = new SaveGameStore(_folder);
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "Mine.sav");
        SaveGameStore.Save(path, "TEST", host.Simulation);
        var saved = new List<string>();
        var window = new SaveLoadWindow(store, saving: true, p =>
        {
            saved.Add(p);
            return true;
        }, p => false);

        host.Click(window, "Overwrite");
        Assert.Empty(saved);
        host.Click(window, "Sure? Overwrite");
        Assert.Equal([path], saved);
    }

    [Fact]
    public void TheOptionsChangeADraftUntilApply()
    {
        var host = new WindowTestHost();
        GameConfig? applied = null;
        var window = new OptionsWindow(new GameConfig(), config => applied = config);
        Assert.Contains(window.Rows(host).SelectMany(row => row.Cells), cell => cell.Text == "Apply" && cell.OnClick is null);

        host.Click(window, "Off");
        host.Click(window, "+", skip: 1);
        host.Click(window, "Apply");

        Assert.NotNull(applied);
        Assert.False(applied.PlaySounds);
        Assert.Equal(new GameConfig().ScrollSpeed + 1, applied.ScrollSpeed);
    }

    [Fact]
    public void TheOptionsHaveCustomisesSwitches()
    {
        var host = new WindowTestHost();
        GameConfig? applied = null;
        var window = new OptionsWindow(new GameConfig(), config => applied = config);
        Assert.Contains(window.Rows(host).SelectMany(row => row.Cells), cell => cell.Text == "Average Contents" && cell.Tooltip is { Length: > 0 });

        // Both are off by default: their On buttons are the only ones to click.
        host.Click(window, "On");
        host.Click(window, "On");
        host.Click(window, "Apply");

        Assert.True(applied!.RemoveDestroyedRooms);
        Assert.True(applied.EnableAverageContents);
    }

    [Fact]
    public void TheOptionsGoThroughTheInstalledLanguages()
    {
        var host = new WindowTestHost();
        GameConfig? applied = null;
        var window = new OptionsWindow(new GameConfig { Language = "fr" }, config => applied = config, ["English", "Français", "Deutsch"]);

        // "fr" is not a name on the list, so the first step goes to its first language, then on round the list.
        host.Click(window, "+", skip: 4);
        Assert.Contains(window.Rows(host).SelectMany(row => row.Cells), cell => cell.Text == "English");
        host.Click(window, "-", skip: 4);
        host.Click(window, "Apply");

        Assert.Equal("Deutsch", applied?.Language);
        Assert.Equal("Français", GameConfig.OriginalLanguageName(1));
        Assert.Null(GameConfig.OriginalLanguageName(6));
    }

    [Fact]
    public void TheLanguageIsTriedOutAtOnceAndGivenBackOnCancel()
    {
        var host = new WindowTestHost();
        var shown = new List<GameConfig>();
        GameConfig? applied = null;
        var current = new GameConfig { Language = "English" };
        var window = new OptionsWindow(current, config => applied = config, ["English", "Français"], preview: shown.Add);

        host.Click(window, "Off");
        host.Click(window, "+", skip: 4);
        var tried = Assert.Single(shown);
        Assert.Equal("Français", tried.Language);
        Assert.True(tried.PlaySounds); // only the language is tried out, not the rest of the draft

        host.Click(window, "Cancel");
        window.Closed();
        Assert.Equal(2, shown.Count);
        Assert.Equal(current, shown[1]);
        Assert.Null(applied);
    }

    [Fact]
    public void AnAppliedLanguageIsNotGivenBack()
    {
        var host = new WindowTestHost();
        var shown = new List<GameConfig>();
        GameConfig? applied = null;
        var window = new OptionsWindow(new GameConfig { Language = "English" }, config => applied = config, ["English", "Français"], preview: shown.Add);

        host.Click(window, "+", skip: 4);
        host.Click(window, "Apply");
        window.Closed();

        Assert.Single(shown);
        Assert.Equal("Français", applied?.Language);
    }

    [Fact]
    public void ALanguageTheGameFontDrawsCanHaveAFontChosenAndGoBackToTheGameFont()
    {
        var host = new WindowTestHost();
        GameConfig? applied = null;
        var window = new OptionsWindow(
            new GameConfig { Language = "Spanish" },
            config => applied = config,
            ["Spanish"],
            fonts: _ => new LanguageFontChoices(LanguageFontChoices.GameFont, [("Arial", @"C:\Fonts\arial.ttf")]));

        Assert.Contains(window.Rows(host).SelectMany(row => row.Cells), cell => cell.Text == "Game font");
        host.Click(window, "+", skip: 4);
        Assert.Contains(window.Rows(host).SelectMany(row => row.Cells), cell => cell.Text == "Arial");
        host.Click(window, "Apply");
        Assert.Equal(@"C:\Fonts\arial.ttf", applied?.LanguageFont(["Spanish"]));

        var again = new OptionsWindow(applied!, config => applied = config, ["Spanish"], fonts: _ => new LanguageFontChoices(LanguageFontChoices.GameFont, [("Arial", @"C:\Fonts\arial.ttf")]));
        host.Click(again, "+", skip: 4);
        Assert.Contains(again.Rows(host).SelectMany(row => row.Cells), cell => cell.Text == "Game font");
        host.Click(again, "Apply");
        Assert.Null(applied?.LanguageFont(["Spanish"]));
    }

    [Fact]
    public void TheMenuPassesOnQuitting()
    {
        var host = new WindowTestHost();
        var actions = new List<string>();
        var menu = new MenuWindow(actions.Add);
        host.Opened.Add(menu);

        host.Click(menu, "Quit");

        Assert.Equal(["quit"], actions);
        Assert.Empty(host.Opened);
    }
}
