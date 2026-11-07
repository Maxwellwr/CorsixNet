using CorsixNet.Net.Protocol;
using CorsixNet.Net.Transport;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;

namespace CorsixNet.Net.Lockstep;

/// <summary>
/// A player's side of a lockstep game: receives the state and the scheduled commands from the
/// server, and runs the simulation as far as the server allows.
/// </summary>
/// <remarks>Not thread-safe: call it from the game's update loop.</remarks>
public sealed class LockstepClient : IDisposable
{
    private readonly IConnection _connection;
    private readonly GameContent _content;

    /// <summary>Connects as <paramref name="playerName"/>; the client joins when the server welcomes it.</summary>
    /// <param name="connection">Connection to the server; the client owns it.</param>
    /// <param name="playerName">Name shown to the server.</param>
    /// <param name="content">Content definitions; they must match the server's.</param>
    public LockstepClient(IConnection connection, string playerName, GameContent? content = null)
    {
        _connection = connection;
        _content = content ?? GameContent.Empty;
        _connection.Send(new HelloMessage(MessageCodec.ProtocolVersion, playerName));
    }

    /// <summary>The simulation, once the server has sent it.</summary>
    public Simulation? Simulation { get; private set; }

    /// <summary>This client's player number (0-based), valid once joined.</summary>
    public int Player { get; private set; }

    /// <summary>The simulation may step while its tick is below this.</summary>
    public long AllowedTick { get; private set; }

    public bool IsJoined => Simulation is not null;

    public bool IsConnected => _connection.IsConnected;

    /// <summary>Why the client is not in the game, if it was rejected or lost the connection.</summary>
    public string? Problem { get; private set; }

    /// <summary>The most recent desync reported by the server, if any.</summary>
    public DesyncMessage? LastDesync { get; private set; }

    /// <summary>Handles all messages received so far.</summary>
    public void ProcessMessages()
    {
        while (_connection.TryReceive(out var message))
        {
            switch (message)
            {
                case WelcomeMessage welcome:
                    using (var state = new MemoryStream(welcome.State, writable: false))
                    {
                        Simulation = Simulation.Load(state, _content);
                    }

                    Player = welcome.Player;
                    AllowedTick = welcome.AllowedTick;
                    break;
                case ScheduledCommandMessage scheduled when Simulation is not null:
                    Simulation.Schedule(scheduled.Command);
                    break;
                case AdvanceMessage advance when Simulation is not null:
                    AllowedTick = Math.Max(AllowedTick, advance.AllowedTick);
                    break;
                case RejectMessage reject:
                    Problem = reject.Reason;
                    break;
                case DesyncMessage desync:
                    LastDesync = desync;
                    break;
            }
        }

        if (!_connection.IsConnected && Problem is null)
        {
            Problem = _connection.Error ?? "Disconnected from the server.";
        }
    }

    /// <summary>Asks the server to schedule a command for everyone.</summary>
    public void Submit(Command command)
    {
        if (IsJoined)
        {
            _connection.Send(new CommandRequestMessage(command));
        }
    }

    /// <summary>Runs up to <paramref name="maxTicks"/> allowed ticks; returns how many ran.</summary>
    public int StepAllowed(int maxTicks = int.MaxValue)
    {
        var ticks = 0;
        while (Simulation is not null && Simulation.Tick < AllowedTick && ticks < maxTicks)
        {
            Simulation.Step();
            ticks++;
            if (Simulation.Tick % LockstepServer.HashInterval == 0)
            {
                _connection.Send(new StateHashMessage(Simulation.Tick, Simulation.ComputeStateHash()));
            }
        }

        return ticks;
    }

    public void Dispose() => _connection.Dispose();
}
