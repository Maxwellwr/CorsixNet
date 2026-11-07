using System.Globalization;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;

namespace CorsixNet.Client.Sessions;

/// <summary>A single-player game: the client runs the simulation by the clock.</summary>
internal sealed class LocalSession : IGameSession
{
    /// <summary>Real time per tick at CorsixTH's "Normal" speed (3 frames of 18 ms).</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(54);

    /// <summary>Upper bound on ticks per update, so a long stall does not freeze the client.</summary>
    private const int MaxTicksPerUpdate = 10;

    private TimeSpan _pending;

    public LocalSession(string name, Simulation simulation)
    {
        Name = name;
        Simulation = simulation;
    }

    public string Name { get; }

    public Simulation Simulation { get; }

    Simulation? IGameSession.Simulation => Simulation;

    public int Player => 0;

    public bool CanPause => true;

    public bool IsPaused { get; set; }

    public string Status => string.Create(CultureInfo.InvariantCulture, $"tick {Simulation.Tick} {(IsPaused ? "paused" : "running")}");

    public void Submit(Command command) => Simulation.Schedule(Player, command);

    public void Update(TimeSpan elapsed)
    {
        if (IsPaused)
        {
            return;
        }

        _pending += elapsed;
        var ticks = 0;
        while (_pending >= TickInterval && ticks < MaxTicksPerUpdate)
        {
            Simulation.Step();
            _pending -= TickInterval;
            ticks++;
        }

        if (ticks == MaxTicksPerUpdate)
        {
            _pending = TimeSpan.Zero;
        }
    }

    public void Dispose()
    {
    }
}
