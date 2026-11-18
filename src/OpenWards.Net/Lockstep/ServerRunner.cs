using System.Diagnostics;
using OpenWards.Net.Transport;

namespace OpenWards.Net.Lockstep;

/// <summary>
/// Runs a <see cref="LockstepServer"/> on its own thread: accepts TCP connections, handles
/// messages, and lets real time pass at the server's game speed (<see cref="LockstepServer.Run"/>) while anyone is
/// connected.
/// </summary>
public sealed class ServerRunner : IDisposable
{
    private readonly LockstepServer _server;
    private readonly TcpConnectionListener _listener;
    private readonly Thread _thread;
    private volatile bool _running = true;

    public ServerRunner(LockstepServer server, TcpConnectionListener listener)
    {
        _server = server;
        _listener = listener;
        _thread = new Thread(Run) { IsBackground = true, Name = "OpenWards server" };
        _thread.Start();
    }

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
        var last = TimeSpan.Zero;
        while (_running)
        {
            while (_listener.TryAccept(out var connection))
            {
                _server.AddConnection(connection);
            }

            _server.ProcessMessages();

            var now = clock.Elapsed;
            if (_server.JoinedCount > 0)
            {
                // Game time stands still while nobody is playing. A stall is caught up by a bounded amount.
                _server.Run(now - last);
            }

            last = now;
            Thread.Sleep(1);
        }
    }
}
