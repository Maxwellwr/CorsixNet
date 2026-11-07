using System.Collections.Concurrent;
using CorsixNet.Net.Protocol;

namespace CorsixNet.Net.Transport;

/// <summary>
/// One end of an in-process connection pair. Messages are encoded and decoded on the way,
/// so tests exercise the codec as well as the lockstep logic.
/// </summary>
public sealed class InMemoryConnection : IConnection
{
    private readonly ConcurrentQueue<byte[]> _inbox = new();
    private InMemoryConnection? _peer;
    private bool _closed;

    private InMemoryConnection()
    {
    }

    public bool IsConnected => !_closed && _peer is { _closed: false };

    public string? Error => null;

    public static (InMemoryConnection A, InMemoryConnection B) CreatePair()
    {
        var a = new InMemoryConnection();
        var b = new InMemoryConnection();
        a._peer = b;
        b._peer = a;
        return (a, b);
    }

    public void Send(Message message)
    {
        if (IsConnected)
        {
            _peer!._inbox.Enqueue(MessageCodec.Encode(message));
        }
    }

    public bool TryReceive(out Message message)
    {
        if (_inbox.TryDequeue(out var bytes))
        {
            message = MessageCodec.Decode(bytes);
            return true;
        }

        message = null!;
        return false;
    }

    public void Dispose() => _closed = true;
}
