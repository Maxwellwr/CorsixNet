using CorsixNet.Net.Protocol;

namespace CorsixNet.Net.Transport;

/// <summary>
/// A reliable, ordered, message-based link between a client and the server.
/// </summary>
/// <remarks>
/// <see cref="Send"/> may be called from any thread. <see cref="TryReceive"/> is polled by the
/// owner's update loop. After the link breaks, <see cref="IsConnected"/> becomes false and
/// <see cref="Error"/> may say why; messages received before that can still be read.
/// </remarks>
public interface IConnection : IDisposable
{
    bool IsConnected { get; }

    string? Error { get; }

    void Send(Message message);

    bool TryReceive(out Message message);
}
