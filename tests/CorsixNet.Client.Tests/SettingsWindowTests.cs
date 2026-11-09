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
        host.Click(saving, host.Lines(saving)[0]);
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
