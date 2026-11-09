using CorsixNet.Client.Ui;
using CorsixNet.Content;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Client.Tests;

/// <summary>A window host over a small hospital that runs each command at once.</summary>
internal sealed class WindowTestHost : IWindowHost
{
    private const int Size = 24;

    public WindowTestHost(LevelRules? rules = null) => Simulation = NewHospital(rules);

    public Simulation Simulation { get; }

    public int Player => 0;

    public OriginalTexts? Texts => null;

    public List<Command> Submitted { get; } = [];

    public List<UiWindow> Opened { get; } = [];

    public List<string> Sounds { get; } = [];

    public void Submit(Command command)
    {
        Submitted.Add(command);
        Simulation.Schedule(0, command);
        Simulation.Step();
    }

    public void Open(UiWindow window) => Opened.Add(window);

    public void Close(UiWindow window) => Opened.Remove(window);

    public void Notice(string text)
    {
    }

    public void PlaySound(string name) => Sounds.Add(name);

    /// <summary>Clicks the first enabled button of the window with this text.</summary>
    public void Click(UiWindow window, string text, int skip = 0)
    {
        var button = window.Rows(this).SelectMany(row => row.Cells).Where(cell => cell.Text == text && cell.OnClick is not null).Skip(skip).FirstOrDefault();
        Assert.NotNull(button);
        button.OnClick!();
    }

    /// <summary>The window's text, row by row, cells separated by '|'.</summary>
    public List<string> Lines(UiWindow window) => window.Rows(this).Select(row => string.Join("|", row.Cells.Select(cell => cell.Text))).ToList();

    private static Simulation NewHospital(LevelRules? rules)
    {
        var map = new LevelMap(Size, Size, playerCount: 1);
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                ref var tile = ref map[x, y];
                tile.Floor = 1;
                tile.Set(TileFlags.Passable, true);
            }
        }

        map.UpdatePathfinding();
        rules ??= new LevelRules(100_000, [], []);
        return new Simulation(map, seed: 3, BaseGameContent.Create(), rules);
    }
}
