using System.Diagnostics;
using CorsixNet.Net.Transport;

namespace CorsixNet.Net.Lockstep;

/// <summary>
/// Runs a <see cref="LockstepServer"/> on its own thread: accepts TCP connections, handles
/// messages, and advances one tick per <see cref="TickInterval"/> while anyone is connected.
/// </summary>
public sealed class ServerRunner : IDisposable
{
    private readonly LockstepServer _server;
    private readonly TcpConnectionListener _listener;
    private readonly Thread _thread;
    private volatile bool _running = true;

    public ServerRunner(LockstepServer server, TcpConnectionListener listener, TimeSpan tickInterval)
    {
        _server = server;
        _listener = listener;
        TickInterval = tickInterval;
        _thread = new Thread(Run) { IsBackground = true, Name = "CorsixNet server" };
        _thread.Start();
    }

    public TimeSpan TickInterval { get; }

    public int Port => _listener.Port;

    public void Dispose()
    {
        _running = false;
        _thread.Join();
        _listener.Dispose();
        _server.Dispose();
    }

    private void Run()
    {
        var clock = Stopwatch.StartNew();
        var nextTick = TimeSpan.Zero;
        while (_running)
        {
            while (_listener.TryAccept(out var connection))
            {
                _server.AddConnection(connection);
            }

            _server.ProcessMessages();

            if (_server.JoinedCount == 0)
            {
                // Game time stands still while nobody is playing.
                nextTick = clock.Elapsed + TickInterval;
            }
            else
            {
                // Catch up after a stall, but by a bounded amount.
                var ticks = 0;
                while (clock.Elapsed >= nextTick && ticks < 10)
                {
                    _server.AdvanceTick();
                    nextTick += TickInterval;
                    ticks++;
                }

                if (clock.Elapsed >= nextTick)
                {
                    nextTick = clock.Elapsed + TickInterval;
                }
            }

            Thread.Sleep(1);
        }
    }
}
