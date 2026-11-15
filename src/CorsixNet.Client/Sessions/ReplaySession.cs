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
/// the one recorded, to tell whether the game played out the same. It can be wound back and forward (<see cref="Seek"/>):
/// the state is kept every <see cref="KeyframeInterval"/> ticks as the replay plays, and a seek goes on from the nearest
/// kept state before the tick.
/// </summary>
internal sealed class ReplaySession : IGameSession
{
    /// <summary>Ticks between the states kept for seeking: a hundred game days.</summary>
    public const long KeyframeInterval = 5000;

    private readonly GameClock _clock = new();
    private readonly Replay _replay;
    private readonly GameContent _content;

    /// <summary>States kept for seeking, by tick, each with the index of the next command to schedule from there.</summary>
    private readonly SortedDictionary<long, (byte[] State, int Next)> _keyframes = [];

    private int _next;

    public ReplaySession(string name, Replay replay, GameContent content)
    {
        Name = name;
        _replay = replay;
        _content = content;
        Simulation = replay.StartingState(content);
        _keyframes[Simulation.Tick] = (Save(Simulation), 0);
        CheckEnd();
    }

    public string Name { get; }

    public Simulation Simulation { get; private set; }

    /// <summary>The tick the recording starts at.</summary>
    public long StartTick => _keyframes.Keys.First();

    /// <summary>The tick the recording ends at, or 0 if it has no end.</summary>
    public long EndTick => _replay.End.Tick;

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
        _ => string.Create(CultureInfo.InvariantCulture, $"replay tick {Simulation.Tick} of {_replay.End.Tick}   [ ]: a month back or on, with Shift a year"),
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
        if (Simulation.Tick % KeyframeInterval == 0 && !_keyframes.ContainsKey(Simulation.Tick))
        {
            _keyframes[Simulation.Tick] = (Save(Simulation), _next);
        }

        CheckEnd();
    }

    /// <summary>
    /// Goes to <paramref name="tick"/>, kept between the recording's start and its end (if it has one): back from the
    /// nearest kept state before it, or forward by playing on. The ticks in between run at once.
    /// </summary>
    public void Seek(long tick)
    {
        tick = Math.Max(tick, StartTick);
        if (EndTick > 0)
        {
            tick = Math.Min(tick, EndTick);
        }

        if (tick < Simulation.Tick || _keyframes.Keys.LastOrDefault(t => t <= tick) > Simulation.Tick)
        {
            var from = _keyframes.Keys.Last(t => t <= tick);
            var (state, next) = _keyframes[from];
            using (var stream = new MemoryStream(state, writable: false))
            {
                Simulation = Simulation.Load(stream, _content);
            }

            _next = next;
        }

        while (Simulation.Tick < tick)
        {
            Step();
        }
    }

    private static byte[] Save(Simulation simulation)
    {
        using var stream = new MemoryStream();
        simulation.Save(stream);
        return stream.ToArray();
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
