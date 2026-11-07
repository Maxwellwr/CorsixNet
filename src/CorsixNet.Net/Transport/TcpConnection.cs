using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using CorsixNet.Net.Protocol;

namespace CorsixNet.Net.Transport;

/// <summary>
/// A connection over TCP. Each message is framed by its 32-bit little-endian length.
/// A background task reads incoming frames into a queue.
/// </summary>
public sealed class TcpConnection : IConnection
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly ConcurrentQueue<Message> _inbox = new();
    private readonly Lock _sendLock = new();
    private readonly CancellationTokenSource _cancellation = new();
    private volatile bool _connected = true;
    private volatile string? _error;

    private TcpConnection(TcpClient client)
    {
        _client = client;
        _client.NoDelay = true;
        _stream = client.GetStream();
        _ = Task.Run(ReadLoopAsync);
    }

    public bool IsConnected => _connected;

    public string? Error => _error;

    /// <summary>Wraps an accepted socket.</summary>
    public static TcpConnection FromAccepted(TcpClient client) => new(client);

    /// <summary>Connects to a server.</summary>
    public static async Task<TcpConnection> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        return new TcpConnection(client);
    }

    public void Send(Message message)
    {
        if (!_connected)
        {
            return;
        }

        var payload = MessageCodec.Encode(message);
        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);
        try
        {
            lock (_sendLock)
            {
                _stream.Write(frame);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException)
        {
            Fail(e.Message);
        }
    }

    public bool TryReceive(out Message message) => _inbox.TryDequeue(out message!);

    public void Dispose()
    {
        _connected = false;
        _cancellation.Cancel();
        _client.Dispose();
        _cancellation.Dispose();
    }

    private async Task ReadLoopAsync()
    {
        var header = new byte[4];
        try
        {
            while (!_cancellation.IsCancellationRequested)
            {
                await _stream.ReadExactlyAsync(header, _cancellation.Token).ConfigureAwait(false);
                var length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length <= 0 || length > MessageCodec.MaxMessageSize)
                {
                    Fail($"Invalid message length {length}.");
                    return;
                }

                var payload = new byte[length];
                await _stream.ReadExactlyAsync(payload, _cancellation.Token).ConfigureAwait(false);
                _inbox.Enqueue(MessageCodec.Decode(payload));
            }
        }
        catch (OperationCanceledException)
        {
            _connected = false;
        }
        catch (EndOfStreamException)
        {
            Fail("The other side closed the connection.");
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException or InvalidDataException)
        {
            Fail(e.Message);
        }
    }

    private void Fail(string error)
    {
        _error ??= error;
        _connected = false;
    }
}

/// <summary>Accepts TCP connections for a server.</summary>
public sealed class TcpConnectionListener : IDisposable
{
    private readonly TcpListener _listener;
    private readonly ConcurrentQueue<TcpConnection> _accepted = new();
    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>Starts listening. Use <see cref="IPAddress.Loopback"/> unless other machines should connect.</summary>
    public TcpConnectionListener(IPAddress address, int port)
    {
        _listener = new TcpListener(address, port);
        _listener.Start();
        _ = Task.Run(AcceptLoopAsync);
    }

    /// <summary>The port actually listened on (useful when 0 was requested).</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public bool TryAccept(out TcpConnection connection) => _accepted.TryDequeue(out connection!);

    public void Dispose()
    {
        _cancellation.Cancel();
        _listener.Stop();
        _listener.Dispose();
        _cancellation.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_cancellation.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cancellation.Token).ConfigureAwait(false);
                _accepted.Enqueue(TcpConnection.FromAccepted(client));
            }
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // Listener stopped.
        }
    }
}
