using CorsixNet.Content.Interface;
using CorsixNet.Net.Protocol;
using CorsixNet.Net.Transport;
using CorsixNet.Sim;

namespace CorsixNet.Net.Lockstep;

/// <summary>
/// The authority of a lockstep game. It runs its own copy of the simulation, stamps every
/// client command with the tick and order it executes in, and tells clients how far they may run.
/// </summary>
/// <remarks>
/// <para>
/// The server is always ahead of its clients: it executes tick <c>t</c> itself before allowing
/// clients to execute it, and it sends commands for a tick before the permission to execute it.
/// Over an ordered connection, a client therefore always knows every command of a tick before
/// it runs that tick, and never has to wait or roll back.
/// </para>
/// <para>
/// Every <see cref="HashInterval"/> ticks the server records the hash of its state. Clients
/// report theirs for the same ticks; a mismatch is a desync, reported to everyone. The client that diverged then gets
/// the server's state again (<see cref="ResyncMessage"/>), as a joining client does, and goes on from there.
/// </para>
/// <para>
/// The server keeps the game's one clock (<see cref="GameClock"/>): any player may pause the game or change its speed,
/// and everyone is told (<see cref="SpeedMessage"/>), so the game runs at the same speed for all.
/// </para>
/// <para>
/// The server may offer levels (<see cref="LobbyLevel"/>): the host, player 1, can switch everyone to another one
/// (<see cref="ChooseLevelRequestMessage"/>). The game then starts that level paused, and every client gets its state as
/// on joining; players the new level has no room for are told so and dropped.
/// </para>
/// <para>Not thread-safe: drive it from one thread (see <see cref="ServerRunner"/>).</para>
/// </remarks>
public sealed class LockstepServer : IDisposable
{
    /// <summary>The longest chat line passed on, in characters; longer ones are cut.</summary>
    public const int MaxChatLength = 200;

    /// <summary>Clients report their state hash after every tick that is a multiple of this.</summary>
    public const int HashInterval = 16;

    /// <summary>How many recorded hashes the server keeps for comparison.</summary>
    private const int HashHistory = 64;

    private readonly List<Peer> _peers = [];
    private readonly SortedDictionary<long, ulong> _hashes = [];
    private readonly List<DesyncMessage> _desyncs = [];
    private readonly GameClock _clock = new();

    private readonly IReadOnlyList<LobbyLevel> _levels;

    /// <summary>The tick the level chosen last started at, whose hash switched clients report first.</summary>
    private long _restartTick;

    /// <param name="simulation">The game, at its start or loaded.</param>
    /// <param name="level">The name of the level being played.</param>
    /// <param name="levels">The levels the host may switch the game to; none by default.</param>
    public LockstepServer(Simulation simulation, string level = "", IReadOnlyList<LobbyLevel>? levels = null)
    {
        Simulation = simulation;
        Level = level;
        _levels = levels ?? [];
        ContentHash = ContentFingerprint.Compute(simulation.Content);
    }

    /// <summary>The name of the level being played.</summary>
    public string Level { get; private set; }

    /// <summary>The levels offered to the host.</summary>
    public IReadOnlyList<LobbyLevel> Levels => _levels;

    /// <summary>The fingerprint of the simulation's content, which a joining client's must match.</summary>
    public ulong ContentHash { get; }

    /// <summary>The authoritative simulation. Only the server's thread may touch it.</summary>
    public Simulation Simulation { get; private set; }

    /// <summary>
    /// Ticks between receiving a command and executing it. 0 is safe, because clients may only run
    /// ticks the server has completed; a delay gives slower clients time to receive commands
    /// before they are allowed to reach them.
    /// </summary>
    public int CommandDelay { get; init; }

    /// <summary>Receives a line of text for each notable event (joins, leaves, desyncs).</summary>
    public Action<string>? Log { get; init; }

    /// <summary>Number of clients that have joined and are still connected.</summary>
    public int JoinedCount => _peers.Count(peer => peer.Player is not null && peer.Connection.IsConnected);

    /// <summary>The speed the game runs at; CorsixTH's Normal at first.</summary>
    public GameSpeed Speed => _clock.Speed;

    /// <summary>Every desync detected so far.</summary>
    public IReadOnlyList<DesyncMessage> Desyncs => _desyncs;

    /// <summary>Takes ownership of a new connection; the client joins once it says hello.</summary>
    public void AddConnection(IConnection connection) => _peers.Add(new Peer(connection));

    /// <summary>Handles all messages received so far and drops broken connections.</summary>
    public void ProcessMessages()
    {
        foreach (var peer in _peers.ToList())
        {
            try
            {
                while (peer.Connection.TryReceive(out var message))
                {
                    Handle(peer, message);
                }
            }
            catch (InvalidDataException e)
            {
                Log?.Invoke($"Dropping {Describe(peer)}: invalid message ({e.Message}).");
                Disconnect(peer);
                continue;
            }

            if (!peer.Connection.IsConnected)
            {
                Log?.Invoke($"{Describe(peer)} left{(peer.Connection.Error is { } error ? $": {error}" : ".")}");
                Disconnect(peer);
            }
        }
    }

    /// <summary>Executes the next tick and allows clients to execute it too.</summary>
    public void AdvanceTick()
    {
        Simulation.Step();

        if (Simulation.Tick % HashInterval == 0)
        {
            _hashes[Simulation.Tick] = Simulation.ComputeStateHash();
            while (_hashes.Count > HashHistory)
            {
                _hashes.Remove(_hashes.Keys.First());
            }
        }

        Broadcast(new AdvanceMessage(Simulation.Tick));
    }

    /// <summary>
    /// Lets <paramref name="elapsed"/> real time pass at the game's <see cref="Speed"/>: executes the ticks it is worth
    /// and allows clients to execute them too. Returns how many ticks ran.
    /// </summary>
    public int Run(TimeSpan elapsed)
    {
        var ticks = _clock.Advance(elapsed);
        for (var i = 0; i < ticks; i++)
        {
            AdvanceTick();
        }

        return ticks;
    }

    /// <summary>
    /// Changes the game's speed, or with null goes back to the speed before a pause or speed-up, and tells everyone.
    /// <paramref name="player"/> is who asked, or -1 for the server itself.
    /// </summary>
    public void SetSpeed(GameSpeed? speed, int player = -1)
    {
        var before = _clock.Speed;
        if (speed is { } wanted)
        {
            _clock.SetSpeed(wanted);
        }
        else
        {
            _clock.Resume();
        }

        if (_clock.Speed != before)
        {
            Log?.Invoke($"{(player < 0 ? "The server" : $"Player {player}")} set the speed to {GameSpeeds.Name(_clock.Speed)}.");
            Broadcast(new SpeedMessage(_clock.Speed, player));
        }
    }

    public void Dispose()
    {
        foreach (var peer in _peers)
        {
            peer.Connection.Dispose();
        }

        _peers.Clear();
    }

    private void Handle(Peer peer, Message message)
    {
        switch (message)
        {
            case HelloMessage hello when peer.Player is null:
                Join(peer, hello);
                break;
            case CommandRequestMessage request when peer.Player is { } player:
                var scheduled = Simulation.Schedule(player, request.Command, Simulation.Tick + CommandDelay);
                Broadcast(new ScheduledCommandMessage(scheduled));
                break;
            case StateHashMessage hash when peer.Player is not null:
                CheckHash(peer, hash);
                break;
            case ChatRequestMessage request when peer.Player is { } player:
                Chat(peer, player, request.Text);
                break;
            case SpeedRequestMessage request when peer.Player is { } player:
                SetSpeed(request.Speed, player);
                break;
            case ChooseLevelRequestMessage request when peer.Player is { } player:
                ChooseLevel(peer, player, request.Level);
                break;
            default:
                throw new InvalidDataException($"Unexpected {message.GetType().Name} from {Describe(peer)}.");
        }
    }

    private void Join(Peer peer, HelloMessage hello)
    {
        if (hello.ProtocolVersion != MessageCodec.ProtocolVersion)
        {
            Reject(peer, $"Protocol version {hello.ProtocolVersion} does not match the server's {MessageCodec.ProtocolVersion}.");
            return;
        }

        if (hello.ContentHash != ContentHash)
        {
            Reject(peer, $"The game content ({hello.ContentHash:X16}) does not match the server's ({ContentHash:X16}): both need the same version of CorsixNet.");
            return;
        }

        var taken = _peers.Where(p => p.Player is not null).Select(p => p.Player!.Value).ToHashSet();
        var free = Enumerable.Range(0, Simulation.Map.PlayerCount).Where(player => !taken.Contains(player)).ToList();
        if (free.Count == 0)
        {
            Reject(peer, $"The game is full ({Simulation.Map.PlayerCount} players).");
            return;
        }

        peer.Player = free[0];
        peer.Name = hello.PlayerName;

        if (_levels.Count > 0)
        {
            peer.Connection.Send(LevelsMessage());
        }

        peer.Connection.Send(new WelcomeMessage(peer.Player.Value, SaveState(), Simulation.Tick));
        peer.Connection.Send(new SpeedMessage(_clock.Speed, -1));
        Log?.Invoke($"{Describe(peer)} joined at tick {Simulation.Tick}.");
        BroadcastPlayers();
    }

    /// <summary>Passes a player's line on to everyone, the player too: trimmed, cut to length, without control characters.</summary>
    private void Chat(Peer peer, int player, string text)
    {
        var line = new string([.. text.Where(c => !char.IsControl(c))]).Trim();
        if (line.Length == 0)
        {
            return;
        }

        if (line.Length > MaxChatLength)
        {
            line = line[..MaxChatLength];
        }

        Log?.Invoke($"{Describe(peer)}: {line}");
        Broadcast(new ChatMessage(player, peer.Name ?? string.Empty, line));
    }

    private LevelsMessage LevelsMessage() => new([.. _levels.Select(level => level.Name)], Level);

    /// <summary>
    /// The host switches the game to an offered level: it starts paused, everyone it has room for gets its state, and
    /// the others are told why they must go.
    /// </summary>
    private void ChooseLevel(Peer peer, int player, string name)
    {
        if (player != 0)
        {
            Log?.Invoke($"{Describe(peer)} may not choose the level; only player 1 may.");
            return;
        }

        if (_levels.FirstOrDefault(level => string.Equals(level.Name, name, StringComparison.OrdinalIgnoreCase)) is not { } chosen)
        {
            Log?.Invoke($"{Describe(peer)} chose {name}, which is not offered.");
            return;
        }

        Simulation simulation;
        try
        {
            simulation = chosen.Start();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Log?.Invoke($"Cannot load {chosen.Name}: {e.Message}");
            Broadcast(new ChatMessage(-1, string.Empty, $"Cannot load {chosen.Name}: {e.Message}"));
            return;
        }

        Simulation = simulation;
        Level = chosen.Name;
        _hashes.Clear();
        _restartTick = Simulation.Tick;
        _hashes[_restartTick] = Simulation.ComputeStateHash();
        Log?.Invoke($"{Describe(peer)} chose {chosen.Name}.");
        SetSpeed(GameSpeed.Pause, player);

        var state = SaveState();
        foreach (var other in _peers.Where(p => p.Player is not null).ToList())
        {
            if (other.Player!.Value >= Simulation.Map.PlayerCount)
            {
                Reject(other, $"{chosen.Name} has room for {Simulation.Map.PlayerCount} players.");
                continue;
            }

            other.Connection.Send(LevelsMessage());
            other.Connection.Send(new WelcomeMessage(other.Player.Value, state, Simulation.Tick));
            other.AwaitingRestart = true;
            other.ResyncedAt = -1;
        }

        BroadcastPlayers();
    }

    private void CheckHash(Peer peer, StateHashMessage hash)
    {
        // A client switched to another level reports the hash of the state it was sent first; until then its hashes are
        // of the level it left.
        if (peer.AwaitingRestart)
        {
            if (hash.Tick != _restartTick)
            {
                return;
            }

            peer.AwaitingRestart = false;
        }

        // Hashes the client sent before it took the state of a resync are of the state it left behind.
        if (hash.Tick <= peer.ResyncedAt || !_hashes.TryGetValue(hash.Tick, out var expected) || expected == hash.Hash)
        {
            return;
        }

        var player = peer.Player!.Value;
        var desync = new DesyncMessage(hash.Tick, player, expected, hash.Hash);
        _desyncs.Add(desync);
        Log?.Invoke($"Desync: player {player} at tick {hash.Tick} (expected {expected:X16}, got {hash.Hash:X16}).");
        Broadcast(desync);
        peer.Connection.Send(new ResyncMessage(SaveState(), Simulation.Tick));
        peer.ResyncedAt = Simulation.Tick;
        Log?.Invoke($"Sent player {player} the state at tick {Simulation.Tick}.");
    }

    private byte[] SaveState()
    {
        using var state = new MemoryStream();
        Simulation.Save(state);
        return state.ToArray();
    }

    private void Reject(Peer peer, string reason)
    {
        peer.Connection.Send(new RejectMessage(reason));
        Log?.Invoke($"Rejected {peer.Name ?? "a client"}: {reason}");
        Disconnect(peer);
    }

    private void Broadcast(Message message)
    {
        foreach (var peer in _peers)
        {
            if (peer.Player is not null)
            {
                peer.Connection.Send(message);
            }
        }
    }

    private void Disconnect(Peer peer)
    {
        peer.Connection.Dispose();
        _peers.Remove(peer);
        if (peer.Player is not null)
        {
            BroadcastPlayers();
        }
    }

    /// <summary>Tells everyone who is in the game now.</summary>
    private void BroadcastPlayers()
    {
        Broadcast(new PlayersMessage([.. _peers
            .Where(p => p.Player is not null)
            .OrderBy(p => p.Player)
            .Select(p => new PlayerInfo(p.Player!.Value, p.Name ?? string.Empty))]));
    }

    private static string Describe(Peer peer)
    {
        return peer.Player is { } player ? $"player {player} ({peer.Name})" : "a connecting client";
    }

    private sealed class Peer(IConnection connection)
    {
        public IConnection Connection { get; } = connection;

        public int? Player { get; set; }

        public string? Name { get; set; }

        /// <summary>Whether the client was switched to another level and has not yet reported the hash of its start.</summary>
        public bool AwaitingRestart { get; set; }

        /// <summary>The server's tick when this client was last sent a resync, or -1.</summary>
        public long ResyncedAt { get; set; } = -1;
    }
}

/// <summary>A level the server offers the host: its name, and how to start a game of it.</summary>
/// <param name="Name">The level's name, as players see it.</param>
/// <param name="Start">Starts the level's simulation; may throw <see cref="IOException"/> or <see cref="InvalidDataException"/>.</param>
public sealed record LobbyLevel(string Name, Func<Simulation> Start);
