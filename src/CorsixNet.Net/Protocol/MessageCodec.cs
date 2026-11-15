using System.Text;
using CorsixNet.Content.Interface;
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
    public const int ProtocolVersion = 4;

    /// <summary>Largest encoded message accepted; a full state for a 128x128 map is far smaller.</summary>
    public const int MaxMessageSize = 16 * 1024 * 1024;

    private const int MaxStringBytes = 1024;

    /// <summary>More players than any map has.</summary>
    private const int MaxPlayers = 64;

    /// <summary>The byte a <see cref="SpeedRequestMessage"/> without a speed (go back to the previous one) carries.</summary>
    private const byte PreviousSpeed = 0xFF;

    /// <summary>More levels than a lobby offers.</summary>
    private const int MaxLevels = 1024;

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
        SpeedRequest = 9,
        Speed = 10,
        Resync = 11,
        ChatRequest = 12,
        Chat = 13,
        Players = 14,
        Levels = 15,
        ChooseLevelRequest = 16,
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
                writer.WriteUInt64(hello.ContentHash);
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
            case ResyncMessage resync:
                writer.WriteByte((byte)MessageType.Resync);
                writer.WriteInt64(resync.AllowedTick);
                writer.WriteInt32(resync.State.Length);
                writer.WriteBytes(resync.State);
                break;
            case ChatRequestMessage request:
                writer.WriteByte((byte)MessageType.ChatRequest);
                WriteString(writer, request.Text);
                break;
            case ChatMessage chat:
                writer.WriteByte((byte)MessageType.Chat);
                writer.WriteInt32(chat.Player);
                WriteString(writer, chat.Name);
                WriteString(writer, chat.Text);
                break;
            case PlayersMessage players:
                writer.WriteByte((byte)MessageType.Players);
                writer.WriteInt32(players.Players.Count);
                foreach (var player in players.Players)
                {
                    writer.WriteInt32(player.Player);
                    WriteString(writer, player.Name);
                }

                break;
            case LevelsMessage levels:
                writer.WriteByte((byte)MessageType.Levels);
                writer.WriteInt32(levels.Levels.Count);
                foreach (var level in levels.Levels)
                {
                    WriteString(writer, level);
                }

                WriteString(writer, levels.Current);
                break;
            case ChooseLevelRequestMessage request:
                writer.WriteByte((byte)MessageType.ChooseLevelRequest);
                WriteString(writer, request.Level);
                break;
            case SpeedRequestMessage request:
                writer.WriteByte((byte)MessageType.SpeedRequest);
                writer.WriteByte(request.Speed is { } wanted ? (byte)wanted : PreviousSpeed);
                break;
            case SpeedMessage speed:
                writer.WriteByte((byte)MessageType.Speed);
                writer.WriteByte((byte)speed.Speed);
                writer.WriteInt32(speed.Player);
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
            MessageType.Hello => ReadHello(reader, stream),
            MessageType.Welcome => ReadWelcome(reader),
            MessageType.Reject => new RejectMessage(ReadString(reader)),
            MessageType.CommandRequest => new CommandRequestMessage(Command.Read(reader)),
            MessageType.ScheduledCommand => new ScheduledCommandMessage(ScheduledCommand.Read(reader)),
            MessageType.Advance => new AdvanceMessage(reader.ReadInt64()),
            MessageType.StateHash => new StateHashMessage(reader.ReadInt64(), reader.ReadUInt64()),
            MessageType.Desync => new DesyncMessage(reader.ReadInt64(), reader.ReadInt32(), reader.ReadUInt64(), reader.ReadUInt64()),
            MessageType.Resync => ReadResync(reader),
            MessageType.ChatRequest => new ChatRequestMessage(ReadString(reader)),
            MessageType.Chat => new ChatMessage(reader.ReadInt32(), ReadString(reader), ReadString(reader)),
            MessageType.Players => ReadPlayers(reader),
            MessageType.Levels => ReadLevels(reader),
            MessageType.ChooseLevelRequest => new ChooseLevelRequestMessage(ReadString(reader)),
            MessageType.SpeedRequest => ReadSpeedRequest(reader),
            MessageType.Speed => new SpeedMessage(ToSpeed(reader.ReadByte()), reader.ReadInt32()),
            _ => throw new InvalidDataException($"Unknown message type {(byte)type}."),
        };

        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException($"{type} message has {stream.Length - stream.Position} unexpected trailing bytes.");
        }

        return message;
    }

    private static SpeedRequestMessage ReadSpeedRequest(SimReader reader)
    {
        var wanted = reader.ReadByte();
        return new SpeedRequestMessage(wanted == PreviousSpeed ? null : ToSpeed(wanted));
    }

    private static GameSpeed ToSpeed(byte value)
    {
        var speed = (GameSpeed)value;
        return Enum.IsDefined(speed) ? speed : throw new InvalidDataException($"Unknown game speed {value}.");
    }

    /// <summary>
    /// A hello from a client older than version 3 has no content hash; it reads as 0, so that the server can tell the
    /// client why it may not join rather than drop it.
    /// </summary>
    private static HelloMessage ReadHello(SimReader reader, MemoryStream stream)
    {
        var version = reader.ReadInt32();
        var name = ReadString(reader);
        return new HelloMessage(version, name, stream.Position < stream.Length ? reader.ReadUInt64() : 0);
    }

    private static PlayersMessage ReadPlayers(SimReader reader)
    {
        var count = reader.ReadCount(MaxPlayers);
        var players = new List<PlayerInfo>(count);
        for (var i = 0; i < count; i++)
        {
            players.Add(new PlayerInfo(reader.ReadInt32(), ReadString(reader)));
        }

        return new PlayersMessage(players);
    }

    private static LevelsMessage ReadLevels(SimReader reader)
    {
        var count = reader.ReadCount(MaxLevels);
        var levels = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            levels.Add(ReadString(reader));
        }

        return new LevelsMessage(levels, ReadString(reader));
    }

    private static ResyncMessage ReadResync(SimReader reader)
    {
        var allowedTick = reader.ReadInt64();
        var state = reader.ReadBytes(reader.ReadCount(MaxMessageSize));
        return new ResyncMessage(state, allowedTick);
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
