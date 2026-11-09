using System.Globalization;
using CorsixNet.Content.Saves;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;

namespace CorsixNet.Client.Sessions;

/// <summary>
/// A single-player game: the client runs the simulation by the clock, and saves it when given a saves folder, at the
/// settings' autosave frequency (CorsixTH's default is monthly, at the start of each month's last day).
/// </summary>
internal sealed class LocalSession : IGameSession
{
    /// <summary>Real time per tick at CorsixTH's "Normal" speed (3 frames of 18 ms).</summary>
    public static readonly TimeSpan TickInterval = GameSpeeds.Interval(GameSpeed.Normal);

    private readonly GameClock _clock = new();

    private readonly SaveGameStore? _autosaves;
    private readonly int _autosaveFrequency;

    public LocalSession(string name, Simulation simulation, SaveGameStore? autosaves = null, int autosaveFrequency = 1)
    {
        Name = name;
        Simulation = simulation;
        _autosaves = autosaves;
        _autosaveFrequency = autosaveFrequency;
    }

    /// <summary>Why the last autosave failed, or null.</summary>
    public string? AutosaveError { get; private set; }

    public string Name { get; }

    public Simulation Simulation { get; }

    Simulation? IGameSession.Simulation => Simulation;

    public int Player => 0;

    public bool CanChangeSpeed => true;

    public GameSpeed Speed => _clock.Speed;

    public string Status => string.Create(CultureInfo.InvariantCulture, $"tick {Simulation.Tick}");

    public void SetSpeed(GameSpeed speed) => _clock.SetSpeed(speed);

    public void ResumeSpeed() => _clock.Resume();

    public void Submit(Command command) => Simulation.Schedule(Player, command);

    public void Update(TimeSpan elapsed)
    {
        var ticks = _clock.Advance(elapsed);
        for (var i = 0; i < ticks; i++)
        {
            var before = Simulation.Date;
            Simulation.Step();
            if (_autosaves is not null && SaveGameStore.IsAutosaveDue(before, Simulation.Date, _autosaveFrequency))
            {
                Autosave(_autosaves);
            }
        }
    }

    private void Autosave(SaveGameStore store)
    {
        try
        {
            SaveGameStore.Save(store.AutosavePath(Simulation.Date), Name, Simulation);
            AutosaveError = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // As CorsixTH, a failed autosave does not stop the game.
            AutosaveError = e.Message;
        }
    }

    public void Dispose()
    {
    }
}
