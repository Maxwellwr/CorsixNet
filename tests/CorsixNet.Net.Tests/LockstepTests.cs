using CorsixNet.Net.Lockstep;
using CorsixNet.Net.Protocol;
using CorsixNet.Net.Transport;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Net.Tests;

public sealed class LockstepTests : IDisposable
{
    private readonly LockstepServer _server = new(NewSimulation());
    private readonly List<LockstepClient> _clients = [];

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        _server.Dispose();
    }

    [Fact]
    public void ClientsJoinWithDistinctPlayers()
    {
        var a = Connect("Alice");
        var b = Connect("Bob");

        Exchange();

        Assert.True(a.IsJoined);
        Assert.True(b.IsJoined);
        Assert.Equal(0, a.Player);
        Assert.Equal(1, b.Player);
        Assert.Equal(2, _server.JoinedCount);
    }

    [Fact]
    public void CommandsFromBothClientsReachEveryoneInTheSameOrder()
    {
        var a = Connect("Alice");
        var b = Connect("Bob");
        Exchange();

        a.Submit(new SpawnHumanoidCommand(new(1, 1)));
        b.Submit(new SpawnHumanoidCommand(new(8, 8)));
        Run(ticks: 5);
        a.Submit(new WalkToCommand(1, new(8, 1)));
        b.Submit(new WalkToCommand(2, new(1, 8)));
        Run(ticks: 200);

        AssertInSync();
        Assert.Empty(_server.Desyncs);
        Assert.Equal(2, a.Simulation!.Entities.Count);
        Assert.Equal(new TilePosition(8, 1), ((Humanoid)a.Simulation.Entities[0]).Tile);
        Assert.Equal(new TilePosition(1, 8), ((Humanoid)b.Simulation!.Entities[1]).Tile);
    }

    [Fact]
    public void SlowClientCatchesUpWithoutDesync()
    {
        var a = Connect("Alice");
        var slow = Connect("Slow");
        Exchange();

        for (var i = 0; i < 100; i++)
        {
            if (i % 10 == 0)
            {
                a.Submit(new SpawnHumanoidCommand(new(1 + (i / 10), 2)));
            }

            _server.ProcessMessages();
            _server.AdvanceTick();
            a.ProcessMessages();
            a.StepAllowed();

            // The slow client processes messages but runs at most one tick per round.
            slow.ProcessMessages();
            slow.StepAllowed(maxTicks: i % 2);
        }

        Assert.True(slow.Simulation!.Tick < _server.Simulation.Tick);
        Exchange();
        AssertInSync();
        Assert.Empty(_server.Desyncs);
    }

    [Fact]
    public void LateJoinerReceivesTheCurrentState()
    {
        var a = Connect("Alice");
        Exchange();
        a.Submit(new SpawnHumanoidCommand(new(2, 2)));
        a.Submit(new WalkToCommand(1, new(7, 7)));
        Run(ticks: 30);

        var late = Connect("Late");
        Exchange();
        Run(ticks: 100);

        Assert.True(late.IsJoined);
        AssertInSync();
        Assert.Single(late.Simulation!.Entities);
        Assert.Empty(_server.Desyncs);
    }

    [Fact]
    public void RejectsPlayersBeyondTheMapsPlayerCount()
    {
        Connect("Alice");
        Connect("Bob");
        var third = Connect("Carol");

        Exchange();

        Assert.False(third.IsJoined);
        Assert.Contains("full", third.Problem);
    }

    [Fact]
    public void RejectsOtherProtocolVersions()
    {
        var (serverEnd, clientEnd) = InMemoryConnection.CreatePair();
        _server.AddConnection(serverEnd);
        clientEnd.Send(new HelloMessage(MessageCodec.ProtocolVersion + 1, "Future"));

        _server.ProcessMessages();

        Assert.True(clientEnd.TryReceive(out var reply));
        Assert.Contains("Protocol version", Assert.IsType<RejectMessage>(reply).Reason);
    }

    [Fact]
    public void DetectsDesync()
    {
        var a = Connect("Alice");
        var cheater = Connect("Cheater");
        Exchange();
        a.Submit(new SpawnHumanoidCommand(new(1, 1)));
        Run(ticks: 3);

        // Change the cheater's state behind the server's back.
        cheater.Simulation!.Random.NextUInt32();
        Run(ticks: 2 * LockstepServer.HashInterval);

        Assert.NotEmpty(_server.Desyncs);
        Assert.All(_server.Desyncs, desync => Assert.Equal(cheater.Player, desync.Player));
        Assert.Equal(_server.Desyncs[^1], a.LastDesync);
        Assert.Equal(_server.Desyncs[^1], cheater.LastDesync);
    }

    [Fact]
    public void ServerDropsClientsThatSendCommandsBeforeJoining()
    {
        var (serverEnd, clientEnd) = InMemoryConnection.CreatePair();
        _server.AddConnection(serverEnd);
        clientEnd.Send(new CommandRequestMessage(new SpawnHumanoidCommand(new(1, 1))));

        _server.ProcessMessages();

        Assert.False(clientEnd.IsConnected);
        Assert.Empty(_server.Simulation.PendingCommands);
    }

    private static Simulation NewSimulation()
    {
        var map = new LevelMap(10, 10, playerCount: 2);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                ref var tile = ref map[x, y];
                tile.Floor = 1;
                tile.Set(TileFlags.Passable | TileFlags.CanTravelAll, true);
                tile.Set(TileFlags.CanTravelWest, x > 0);
                tile.Set(TileFlags.CanTravelEast, x < map.Width - 1);
                tile.Set(TileFlags.CanTravelNorth, y > 0);
                tile.Set(TileFlags.CanTravelSouth, y < map.Height - 1);
            }
        }

        return new Simulation(map, seed: 42);
    }

    private LockstepClient Connect(string name)
    {
        var (serverEnd, clientEnd) = InMemoryConnection.CreatePair();
        _server.AddConnection(serverEnd);
        var client = new LockstepClient(clientEnd, name);
        _clients.Add(client);
        return client;
    }

    /// <summary>Delivers pending messages both ways and lets clients run everything allowed.</summary>
    private void Exchange()
    {
        _server.ProcessMessages();
        foreach (var client in _clients)
        {
            client.ProcessMessages();
            client.StepAllowed();
        }

        _server.ProcessMessages();
    }

    private void Run(int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            _server.ProcessMessages();
            _server.AdvanceTick();
            Exchange();
        }
    }

    private void AssertInSync()
    {
        var expected = _server.Simulation.ComputeStateHash();
        foreach (var client in _clients.Where(c => c.IsJoined))
        {
            Assert.Equal(_server.Simulation.Tick, client.Simulation!.Tick);
            Assert.Equal(expected, client.Simulation.ComputeStateHash());
        }
    }
}
