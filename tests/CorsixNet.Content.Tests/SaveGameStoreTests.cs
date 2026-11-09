using CorsixNet.Content.Saves;
using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Content.Tests;

/// <summary>The saves folder: quick save, autosaves and the save file's header (CorsixTH's app.lua and world.lua).</summary>
public sealed class SaveGameStoreTests : IDisposable
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
    public void ASavedGameLoadsBackWithItsLevel()
    {
        var store = new SaveGameStore(_folder);
        var simulation = NewSimulation();
        for (var i = 0; i < 120; i++)
        {
            simulation.Step();
        }

        SaveGameStore.Save(store.QuickSavePath, "LEVEL.L3", simulation);
        var saved = SaveGameStore.Load(store.QuickSavePath, BaseGameContent.Create());

        Assert.Equal("LEVEL.L3", saved.LevelName);
        Assert.Equal(simulation.Tick, saved.Simulation.Tick);
        Assert.Equal(simulation.ComputeStateHash(), saved.Simulation.ComputeStateHash());
        Assert.False(File.Exists(store.QuickSavePath + ".tmp"));
    }

    [Fact]
    public void SavingAgainReplacesTheFile()
    {
        var store = new SaveGameStore(_folder);
        var simulation = NewSimulation();
        SaveGameStore.Save(store.QuickSavePath, "LEVEL.L1", simulation);
        simulation.Step();
        SaveGameStore.Save(store.QuickSavePath, "LEVEL.L1", simulation);

        Assert.Equal(1, SaveGameStore.Load(store.QuickSavePath, BaseGameContent.Create()).Simulation.Tick);
    }

    [Fact]
    public void OtherFilesAreRefused()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "not-a-save.sav");
        File.WriteAllText(path, "Hello, hospital.");

        Assert.Throws<InvalidDataException>(() => SaveGameStore.Load(path, BaseGameContent.Create()));
    }

    [Fact]
    public void TheQuickSaveAndAutosavesHaveCorsixThsNames()
    {
        var store = new SaveGameStore(_folder);

        Assert.Equal(Path.Combine(_folder, "quicksave.qs"), store.QuickSavePath);
        Assert.Equal("Autosave-02-28.sav", SaveGameStore.AutosaveName(new GameDate(3, 2, 28, 0)));
        Assert.Equal(Path.Combine(_folder, "Autosaves", "Autosave-12-31.sav"), store.AutosavePath(new GameDate(1, 12, 31, 0)));
    }

    [Fact]
    public void TheAutosaveIsDueAsTheMonthsLastDayBegins()
    {
        Assert.True(SaveGameStore.IsAutosaveDue(new GameDate(1, 1, 30, 49), new GameDate(1, 1, 31, 0)));
        Assert.False(SaveGameStore.IsAutosaveDue(new GameDate(1, 1, 31, 0), new GameDate(1, 1, 31, 1)));
        Assert.False(SaveGameStore.IsAutosaveDue(new GameDate(1, 1, 29, 49), new GameDate(1, 1, 30, 0)));
        Assert.True(SaveGameStore.IsAutosaveDue(new GameDate(1, 2, 27, 49), new GameDate(1, 2, 28, 0)));
    }

    [Fact]
    public void TheNewestAutosaveIsFoundToContinue()
    {
        var store = new SaveGameStore(_folder);
        Assert.Null(store.MostRecentAutosave());

        var simulation = NewSimulation();
        var older = store.AutosavePath(new GameDate(1, 1, 31, 0));
        var newer = store.AutosavePath(new GameDate(1, 2, 28, 0));
        SaveGameStore.Save(newer, "LEVEL.L1", simulation);
        SaveGameStore.Save(older, "LEVEL.L1", simulation);
        File.SetLastWriteTimeUtc(older, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(newer, store.MostRecentAutosave());
    }

    private static Simulation NewSimulation()
    {
        var map = new LevelMap(4, 4, playerCount: 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Floor = 1;
                map[x, y].Set(TileFlags.Passable, true);
            }
        }

        return new Simulation(map, seed: 5, BaseGameContent.Create(), new LevelRules(1000, [], []));
    }
}
