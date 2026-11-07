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
/// report theirs for the same ticks; a mismatch is a desync, reported to everyone.
/// </para>
/// <para>Not thread-safe: drive it from one thread (see <see cref="ServerRunner"/>).</para>
/// </remarks>
public sealed class LockstepServer : IDisposable
{
    /// <summary>Clients report their state hash after every tick that is a multiple of this.</summary>
    public const int HashInterval = 16;

    /// <summary>How many recorded hashes the server keeps for comparison.</summary>
    private const int HashHistory = 64;

    private readonly List<Peer> _peers = [];
    private readonly SortedDictionary<long, ulong> _hashes = [];
    private readonly List<DesyncMessage> _desyncs = [];

    public LockstepServer(Simulation simulation)
    {
        Simulation = simulation;
    }

    /// <summary>The authoritative simulation. Only the server's thread may touch it.</summary>
    public Simulation Simulation { get; }

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
            case StateHashMessage hash when peer.Player is { } player:
                CheckHash(player, hash);
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

        var taken = _peers.Where(p => p.Player is not null).Select(p => p.Player!.Value).ToHashSet();
        var free = Enumerable.Range(0, Simulation.Map.PlayerCount).Where(player => !taken.Contains(player)).ToList();
        if (free.Count == 0)
        {
            Reject(peer, $"The game is full ({Simulation.Map.PlayerCount} players).");
            return;
        }

        peer.Player = free[0];
        peer.Name = hello.PlayerName;

        using var state = new MemoryStream();
        Simulation.Save(state);
        peer.Connection.Send(new WelcomeMessage(peer.Player.Value, state.ToArray(), Simulation.Tick));
        Log?.Invoke($"{Describe(peer)} joined at tick {Simulation.Tick}.");
    }

    private void CheckHash(int player, StateHashMessage hash)
    {
        if (!_hashes.TryGetValue(hash.Tick, out var expected) || expected == hash.Hash)
        {
            return;
        }

        var desync = new DesyncMessage(hash.Tick, player, expected, hash.Hash);
        _desyncs.Add(desync);
        Log?.Invoke($"Desync: player {player} at tick {hash.Tick} (expected {expected:X16}, got {hash.Hash:X16}).");
        Broadcast(desync);
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
    }
}
