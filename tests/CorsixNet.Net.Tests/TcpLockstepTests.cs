using System.Net;
using CorsixNet.Net.Lockstep;
using CorsixNet.Net.Transport;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Net.Tests;

public class TcpLockstepTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task TwoClientsStayInSyncOverLoopback()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var server = new LockstepServer(NewSimulation());
        using var runner = new ServerRunner(server, new TcpConnectionListener(IPAddress.Loopback, 0), TimeSpan.FromMilliseconds(2));

        using var a = new LockstepClient(await TcpConnection.ConnectAsync("127.0.0.1", runner.Port, cancellation), "Alice");
        using var b = new LockstepClient(await TcpConnection.ConnectAsync("127.0.0.1", runner.Port, cancellation), "Bob");

        await PumpUntil(() => a.IsJoined && b.IsJoined, cancellation, a, b);
        a.Submit(new SpawnHumanoidCommand(new(1, 1)));
        b.Submit(new SpawnHumanoidCommand(new(5, 5)));
        a.Submit(new WalkToCommand(1, new(6, 1)));

        // The server runs on its own clock and schedules a command at the tick it receives it,
        // which on a slow machine can be well past any fixed tick. Wait for the commands to have
        // run on both clients, then let both run on for a while from there.
        await PumpUntil(() => a.Simulation!.Entities.Count == 2 && b.Simulation!.Entities.Count == 2, cancellation, a, b);
        var target = Math.Max(a.Simulation!.Tick, b.Simulation!.Tick) + 128;
        await PumpUntil(() => a.Simulation.Tick >= target && b.Simulation!.Tick >= target, cancellation, a, b);

        Assert.Null(a.Problem);
        Assert.Null(b.Problem);
        Assert.Null(a.LastDesync);
        Assert.Null(b.LastDesync);
        Assert.Equal(2, a.Simulation.Entities.Count);
        Assert.Equal(2, b.Simulation!.Entities.Count);

        // Freeze the client that is ahead, bring the other to exactly the same tick, compare.
        var (ahead, behind) = a.Simulation.Tick >= b.Simulation!.Tick ? (a, b) : (b, a);
        var tick = ahead.Simulation!.Tick;
        var expected = ahead.Simulation.ComputeStateHash();
        await PumpUntil(() => behind.Simulation!.Tick == tick, cancellation, behind, maxTick: tick);

        Assert.Equal(expected, behind.Simulation!.ComputeStateHash());
    }

    /// <summary>Processes messages and steps the given clients until the condition holds.</summary>
    private static async Task PumpUntil(Func<bool> condition, CancellationToken cancellation, LockstepClient first, LockstepClient? second = null, long maxTick = long.MaxValue)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the clients.");
            foreach (var client in second is null ? [first] : new[] { first, second })
            {
                client.ProcessMessages();
                Assert.Null(client.Problem);
                if (client.Simulation is { } simulation)
                {
                    client.StepAllowed((int)Math.Min(int.MaxValue, maxTick - simulation.Tick));
                }
            }

            await Task.Delay(1, cancellation);
        }
    }

    private static Simulation NewSimulation()
    {
        var map = new LevelMap(8, 8, playerCount: 2);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                ref var tile = ref map[x, y];
                tile.Set(TileFlags.Passable, true);
                tile.Set(TileFlags.CanTravelWest, x > 0);
                tile.Set(TileFlags.CanTravelEast, x < map.Width - 1);
                tile.Set(TileFlags.CanTravelNorth, y > 0);
                tile.Set(TileFlags.CanTravelSouth, y < map.Height - 1);
            }
        }

        return new Simulation(map, seed: 9);
    }
}
