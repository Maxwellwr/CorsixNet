using System.Globalization;
using OpenWards.Content.Interface;
using OpenWards.Content.Saves;
using OpenWards.Net.Lockstep;
using OpenWards.Net.Protocol;
using OpenWards.Sim;
using OpenWards.Sim.Commands;

namespace OpenWards.Client.Sessions;

/// <summary>A lockstep game: commands go to the server, and the simulation runs as far as it allows.</summary>
internal sealed class NetworkSession : IGameSession
{
    /// <summary>Upper bound on ticks per update, so joining a long game does not freeze the client.</summary>
    private const int MaxTicksPerUpdate = 20;

    private readonly LockstepClient _client;
    private readonly ServerRunner? _hostedServer;

    /// <summary>The last speed request sent, and the server's speed when it was sent.</summary>
    private (GameSpeed? Request, GameSpeed ServerSpeed)? _lastRequest;

    /// <param name="name">Name for the status line until the server says which level is played: the level or the address.</param>
    /// <param name="client">Connected client; the session owns it.</param>
    /// <param name="hostedServer">Server running in this process, if this client hosts the game; the session owns it.</param>
    public NetworkSession(string name, LockstepClient client, ServerRunner? hostedServer = null)
    {
        _name = name;
        _client = client;
        _hostedServer = hostedServer;
    }

    private readonly string _name;

    /// <summary>The level being played, which the host may switch; the address while joining.</summary>
    public string Name => _client.Level ?? _name;

    public Simulation? Simulation => _client.Simulation;

    public int Player => _client.Player;

    public bool CanChangeSpeed => _client.IsJoined;

    public bool IsShared => true;

    public Replay? Recording => _client.Recording;

    public GameSpeed Speed => _client.Speed;

    public void SetSpeed(GameSpeed speed) => Request(speed);

    public void ResumeSpeed() => Request(null);

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

            var role = _hostedServer is not null ? $"hosting on port {_hostedServer.Port}" : _client.Level is null ? "online" : $"online at {_name}";
            var players = _client.Players.Count == 0 ? string.Empty : "   " + string.Join(", ", _client.Players.Select(p => $"{p.Player + 1} {p.Name}"));
            var behind = _client.AllowedTick - simulation.Tick;
            var speed = _client.SpeedSetBy < 0 ? string.Empty : $"   {GameSpeeds.Name(_client.Speed)} (player {_client.SpeedSetBy + 1})";
            var desync = _client.LastDesync is { } d ? $"DESYNC at tick {d.Tick} (player {d.Player + 1})   " : string.Empty;
            var resynced = _client.Resyncs > 0 ? $"   state resent {_client.Resyncs}x" : string.Empty;
            // A desync first: the end of a long status line is under the date.
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{desync}tick {simulation.Tick} {role}   player {Player + 1} of {simulation.Map.PlayerCount}{players}{(behind > 1 ? $"   {behind} ticks behind" : string.Empty)}{speed}{resynced}");
        }
    }

    public void Submit(Command command) => _client.Submit(command);

    /// <summary>What the players said, oldest first.</summary>
    public IReadOnlyList<ChatMessage> Chat => _client.Chat;

    /// <summary>How many chat lines have arrived in all.</summary>
    public int ChatCount => _client.ChatCount;

    /// <summary>The levels the server offers the host.</summary>
    public IReadOnlyList<string> Levels => _client.Levels;

    /// <summary>The level being played, as the server says.</summary>
    public string? Level => _client.Level;

    /// <summary>Whether this player is the host, who chooses the level.</summary>
    public bool IsHost => _client.IsHost;

    /// <summary>As the host, switches everyone to another level.</summary>
    public void ChooseLevel(string level) => _client.ChooseLevel(level);

    /// <summary>Says a line to every player.</summary>
    public void Say(string text) => _client.Say(text);

    public void Update(TimeSpan elapsed)
    {
        _client.ProcessMessages();
        _client.StepAllowed(MaxTicksPerUpdate);
    }

    /// <summary>
    /// Sends a speed request unless the server already runs at that speed, or the same request was sent and the
    /// server's speed has not changed since: a key held down (speed-up) asks once, not every frame.
    /// </summary>
    private void Request(GameSpeed? speed)
    {
        if (speed == _client.Speed || _lastRequest == (speed, _client.Speed))
        {
            return;
        }

        _lastRequest = (speed, _client.Speed);
        _client.RequestSpeed(speed);
    }

    public void Dispose()
    {
        _client.Dispose();
        _hostedServer?.Dispose();
    }
}
