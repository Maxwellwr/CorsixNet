using System.Globalization;
using CorsixNet.Content.Interface;
using CorsixNet.Net.Lockstep;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;

namespace CorsixNet.Client.Sessions;

/// <summary>A lockstep game: commands go to the server, and the simulation runs as far as it allows.</summary>
internal sealed class NetworkSession : IGameSession
{
    /// <summary>Upper bound on ticks per update, so joining a long game does not freeze the client.</summary>
    private const int MaxTicksPerUpdate = 20;

    private readonly LockstepClient _client;
    private readonly ServerRunner? _hostedServer;

    /// <param name="name">Name for the status line.</param>
    /// <param name="client">Connected client; the session owns it.</param>
    /// <param name="hostedServer">Server running in this process, if this client hosts the game; the session owns it.</param>
    public NetworkSession(string name, LockstepClient client, ServerRunner? hostedServer = null)
    {
        Name = name;
        _client = client;
        _hostedServer = hostedServer;
    }

    public string Name { get; }

    public Simulation? Simulation => _client.Simulation;

    public int Player => _client.Player;

    public bool CanChangeSpeed => false;

    public GameSpeed Speed => GameSpeed.Normal;

    public void SetSpeed(GameSpeed speed)
    {
    }

    public void ResumeSpeed()
    {
    }

    public string Status
    {
        get
        {
            if (_client.Problem is { } problem)
            {
                return $"offline: {problem}";
            }

            if (_client.Simulation is not { } simulation)
            {
                return "joining...";
            }

            var role = _hostedServer is null ? "online" : $"hosting on port {_hostedServer.Port}";
            var behind = _client.AllowedTick - simulation.Tick;
            var desync = _client.LastDesync is { } d ? $"   DESYNC at tick {d.Tick} (player {d.Player + 1})" : string.Empty;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"tick {simulation.Tick} {role}   player {Player + 1} of {simulation.Map.PlayerCount}{(behind > 1 ? $"   {behind} ticks behind" : string.Empty)}{desync}");
        }
    }

    public void Submit(Command command) => _client.Submit(command);

    public void Update(TimeSpan elapsed)
    {
        _client.ProcessMessages();
        _client.StepAllowed(MaxTicksPerUpdate);
    }

    public void Dispose()
    {
        _client.Dispose();
        _hostedServer?.Dispose();
    }
}
