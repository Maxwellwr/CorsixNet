using CorsixNet.Content.Interface;
using CorsixNet.Net.Lockstep;
using CorsixNet.Net.Transport;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Net.Tests;

/// <summary>The levels a server offers, and the host switching everyone to another.</summary>
public sealed class LobbyTests : IDisposable
{
    private readonly List<string> _log = [];
    private readonly LockstepServer _server;

    private readonly List<LockstepClient> _clients = [];

    public LobbyTests() => _server = new(
        NewSimulation(10, players: 2),
        "Two",
        [new LobbyLevel("Two", () => NewSimulation(10, players: 2)), new LobbyLevel("Big", () => NewSimulation(14, players: 2)), new LobbyLevel("Solo", () => NewSimulation(8, players: 1)), new LobbyLevel("Broken", () => throw new InvalidDataException("bad file"))])
    {
        Log = _log.Add,
    };

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        _server.Dispose();
    }

    [Fact]
    public void Players_learn_the_levels_on_joining()
    {
        var a = Connect("Alice");
        var b = Connect("Bob");
        Exchange();

        Assert.Equal(["Two", "Big", "Solo", "Broken"], b.Levels);
        Assert.Equal("Two", b.Level);
        Assert.True(a.IsHost);
        Assert.False(b.IsHost);
        Assert.Equal("Two", a.Recording!.LevelName);
    }

    [Fact]
    public void The_host_switches_everyone_to_another_level_which_starts_paused()
    {
        var a = Connect("Alice");
        var b = Connect("Bob");
        Exchange();
        a.Submit(new SpawnHumanoidCommand(new(2, 2)));
        Run(ticks: 40);

        a.ChooseLevel("big");
        Exchange();

        Assert.Equal("Big", _server.Level);
        Assert.Equal(GameSpeed.Pause, _server.Speed);
        Assert.All([a, b], client =>
        {
            Assert.Equal("Big", client.Level);
            Assert.Equal(14, client.Simulation!.Map.Width);
            Assert.Equal(0, client.Simulation.Tick);
            Assert.Empty(client.Simulation.Entities);
            Assert.Equal(GameSpeed.Pause, client.Speed);
            Assert.Equal("Big", client.Recording!.LevelName);
        });
        Assert.Equal((0, 1), (a.Player, b.Player));

        // The old level's hashes still on their way are not taken for a desync.
        b.Submit(new SpawnHumanoidCommand(new(3, 3)));
        Run(ticks: 100);
        Assert.Empty(_server.Desyncs);
        AssertInSync();
        Assert.Single(a.Simulation!.Entities);
    }

    [Fact]
    public void Only_the_host_chooses_and_only_an_offered_level_that_loads()
    {
        var a = Connect("Alice");
        var b = Connect("Bob");
        Exchange();

        b.ChooseLevel("Big");
        a.ChooseLevel("Elsewhere");
        a.ChooseLevel("Broken");
        Exchange();

        Assert.Equal("Two", _server.Level);
        Assert.Equal(10, a.Simulation!.Map.Width);
        Assert.Equal(GameSpeed.Normal, _server.Speed);
        Assert.Contains(a.Chat, line => line.Player == -1 && line.Text.Contains("bad file", StringComparison.Ordinal));
    }

    [Fact]
    public void Players_a_level_has_no_room_for_are_told_and_dropped()
    {
        var a = Connect("Alice");
        var b = Connect("Bob");
        Exchange();

        a.ChooseLevel("Solo");
        Exchange();

        // Dropped once, when rejected: not a second time as a player who left (that disposed a TCP connection twice).
        Assert.DoesNotContain(_log, line => line.Contains("left", StringComparison.Ordinal));

        Assert.Equal(8, a.Simulation!.Map.Width);
        Assert.Equal(1, _server.JoinedCount);
        Assert.Contains("room for 1", b.Problem);
        Assert.Equal(["Alice"], a.Players.Select(player => player.Name));
    }

    private static Simulation NewSimulation(int size, int players)
    {
        var map = new LevelMap(size, size, playerCount: players);
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

    private void Exchange()
    {
        _server.ProcessMessages();
        foreach (var client in _clients)
        {
            client.ProcessMessages();
            client.StepAllowed();
        }

        _server.ProcessMessages();
        foreach (var client in _clients)
        {
            client.ProcessMessages();
        }
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
        foreach (var client in _clients.Where(c => c.IsJoined && c.Problem is null))
        {
            Assert.Equal(_server.Simulation.Tick, client.Simulation!.Tick);
            Assert.Equal(expected, client.Simulation.ComputeStateHash());
        }
    }
}
