using System.Text;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Net.Protocol;

/// <summary>
/// Encodes messages as bytes: a type byte followed by the fields, using the simulation's
/// little-endian serialization. Framing (lengths) is the transport's job.
/// </summary>
public static class MessageCodec
{
    /// <summary>Bumped whenever messages or the simulation state format change incompatibly.</summary>
    public const int ProtocolVersion = 1;

    /// <summary>Largest encoded message accepted; a full state for a 128x128 map is far smaller.</summary>
    public const int MaxMessageSize = 16 * 1024 * 1024;

    private const int MaxStringBytes = 1024;

    private enum MessageType : byte
    {
        Hello = 1,
        Welcome = 2,
        Reject = 3,
        CommandRequest = 4,
        ScheduledCommand = 5,
        Advance = 6,
        StateHash = 7,
        Desync = 8,
    }

    public static byte[] Encode(Message message)
    {
        using var stream = new MemoryStream();
        var writer = new SimWriter(stream);
        switch (message)
        {
            case HelloMessage hello:
                writer.WriteByte((byte)MessageType.Hello);
                writer.WriteInt32(hello.ProtocolVersion);
                WriteString(writer, hello.PlayerName);
                break;
            case WelcomeMessage welcome:
                writer.WriteByte((byte)MessageType.Welcome);
                writer.WriteInt32(welcome.Player);
                writer.WriteInt64(welcome.AllowedTick);
                writer.WriteInt32(welcome.State.Length);
                writer.WriteBytes(welcome.State);
                break;
            case RejectMessage reject:
                writer.WriteByte((byte)MessageType.Reject);
                WriteString(writer, reject.Reason);
                break;
            case CommandRequestMessage request:
                writer.WriteByte((byte)MessageType.CommandRequest);
                request.Command.Write(writer);
                break;
            case ScheduledCommandMessage scheduled:
                writer.WriteByte((byte)MessageType.ScheduledCommand);
                scheduled.Command.Write(writer);
                break;
            case AdvanceMessage advance:
                writer.WriteByte((byte)MessageType.Advance);
                writer.WriteInt64(advance.AllowedTick);
                break;
            case StateHashMessage hash:
                writer.WriteByte((byte)MessageType.StateHash);
                writer.WriteInt64(hash.Tick);
                writer.WriteUInt64(hash.Hash);
                break;
            case DesyncMessage desync:
                writer.WriteByte((byte)MessageType.Desync);
                writer.WriteInt64(desync.Tick);
                writer.WriteInt32(desync.Player);
                writer.WriteUInt64(desync.Expected);
                writer.WriteUInt64(desync.Actual);
                break;
            default:
                throw new ArgumentException($"Unknown message {message.GetType().Name}.", nameof(message));
        }

        return stream.ToArray();
    }

    /// <exception cref="InvalidDataException">The bytes are not a valid message.</exception>
    public static Message Decode(ReadOnlySpan<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        var reader = new SimReader(stream);
        var type = (MessageType)reader.ReadByte();
        Message message = type switch
        {
            MessageType.Hello => new HelloMessage(reader.ReadInt32(), ReadString(reader)),
            MessageType.Welcome => ReadWelcome(reader),
            MessageType.Reject => new RejectMessage(ReadString(reader)),
            MessageType.CommandRequest => new CommandRequestMessage(Command.Read(reader)),
            MessageType.ScheduledCommand => new ScheduledCommandMessage(ScheduledCommand.Read(reader)),
            MessageType.Advance => new AdvanceMessage(reader.ReadInt64()),
            MessageType.StateHash => new StateHashMessage(reader.ReadInt64(), reader.ReadUInt64()),
            MessageType.Desync => new DesyncMessage(reader.ReadInt64(), reader.ReadInt32(), reader.ReadUInt64(), reader.ReadUInt64()),
            _ => throw new InvalidDataException($"Unknown message type {(byte)type}."),
        };

        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException($"{type} message has {stream.Length - stream.Position} unexpected trailing bytes.");
        }

        return message;
    }

    private static WelcomeMessage ReadWelcome(SimReader reader)
    {
        var player = reader.ReadInt32();
        var allowedTick = reader.ReadInt64();
        var state = reader.ReadBytes(reader.ReadCount(MaxMessageSize));
        return new WelcomeMessage(player, state, allowedTick);
    }

    private static void WriteString(SimWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > MaxStringBytes)
        {
            throw new ArgumentException($"String of {bytes.Length} bytes is longer than {MaxStringBytes}.", nameof(value));
        }

        writer.WriteInt32(bytes.Length);
        writer.WriteBytes(bytes);
    }

    private static string ReadString(SimReader reader)
    {
        return Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadCount(MaxStringBytes)));
    }
}
