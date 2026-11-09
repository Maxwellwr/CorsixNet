using System.Globalization;
using CorsixNet.Content.Interface;
using CorsixNet.Content.Saves;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;

namespace CorsixNet.Client.Sessions;

/// <summary>
/// Watching a replay: the recorded game's start runs by the clock like a local game, with each recorded command scheduled
/// at the tick it was given; the player's own commands are not taken. At the recording's end the state is compared with
/// the one recorded, to tell whether the game played out the same.
/// </summary>
internal sealed class ReplaySession : IGameSession
{
    private readonly GameClock _clock = new();
    private readonly Replay _replay;
    private int _next;

    public ReplaySession(string name, Replay replay, GameContent content)
    {
        Name = name;
        _replay = replay;
        Simulation = replay.StartingState(content);
        CheckEnd();
    }

    public string Name { get; }

    public Simulation Simulation { get; }

    Simulation? IGameSession.Simulation => Simulation;

    public int Player => 0;

    public bool CanChangeSpeed => true;

    public GameSpeed Speed => _clock.Speed;

    /// <summary>Whether the replay played out as recorded: null until its end is reached (or if it has none).</summary>
    public bool? Matches { get; private set; }

    /// <summary>Whether all the recorded commands have been run.</summary>
    public bool IsFinished => _next >= _replay.Commands.Count && Simulation.Tick >= _replay.End.Tick;

    public string Status => Matches switch
    {
        true => string.Create(CultureInfo.InvariantCulture, $"replay tick {Simulation.Tick}: played out as recorded"),
        false => string.Create(CultureInfo.InvariantCulture, $"replay tick {Simulation.Tick}: OUT OF STEP with the recording"),
        _ => string.Create(CultureInfo.InvariantCulture, $"replay tick {Simulation.Tick} of {_replay.End.Tick}"),
    };

    public void SetSpeed(GameSpeed speed) => _clock.SetSpeed(speed);

    public void ResumeSpeed() => _clock.Resume();

    /// <summary>A replay takes no commands of the player's.</summary>
    public void Submit(Command command)
    {
    }

    public void Update(TimeSpan elapsed)
    {
        var ticks = _clock.Advance(elapsed);
        for (var i = 0; i < ticks; i++)
        {
            Step();
        }
    }

    /// <summary>Schedules the commands recorded for this tick, then runs it.</summary>
    internal void Step()
    {
        var commands = _replay.Commands;
        while (_next < commands.Count && commands[_next].Tick <= Simulation.Tick)
        {
            Simulation.Schedule(commands[_next].Player, commands[_next].Command);
            _next++;
        }

        Simulation.Step();
        CheckEnd();
    }

    private void CheckEnd()
    {
        if (Matches is null && _replay.End.Tick > 0 && Simulation.Tick == _replay.End.Tick)
        {
            Matches = Simulation.ComputeStateHash() == _replay.End.Hash;
        }
    }

    public void Dispose()
    {
    }
}
