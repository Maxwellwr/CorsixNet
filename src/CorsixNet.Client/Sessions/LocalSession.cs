using System.Globalization;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;

namespace CorsixNet.Client.Sessions;

/// <summary>A single-player game: the client runs the simulation by the clock.</summary>
internal sealed class LocalSession : IGameSession
{
    /// <summary>Real time per tick at CorsixTH's "Normal" speed (3 frames of 18 ms).</summary>
    public static readonly TimeSpan TickInterval = GameSpeeds.Interval(GameSpeed.Normal);

    private readonly GameClock _clock = new();

    public LocalSession(string name, Simulation simulation)
    {
        Name = name;
        Simulation = simulation;
    }

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
            Simulation.Step();
        }
    }

    public void Dispose()
    {
    }
}
