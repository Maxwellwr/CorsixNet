using CorsixNet.Net.Protocol;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Rooms;

namespace CorsixNet.Net.Tests;

public class MessageCodecTests
{
    public static TheoryData<Message> Messages() =>
    [
        new HelloMessage(MessageCodec.ProtocolVersion, "Dr. Ñandú"),
        new RejectMessage("full"),
        new CommandRequestMessage(new WalkToCommand(3, new(4, 5))),
        new CommandRequestMessage(new BuildRoomCommand(new RoomPlan("gp", new TileRect(1, 2, 4, 5), new DoorPlacement(new(3, 7), EdgeOrientation.North)))),
        new CommandRequestMessage(new PlaceObjectCommand("desk", new(6, 7), Direction.West)),
        new CommandRequestMessage(new BuildRoomCommand(new RoomPlan("gp", new TileRect(1, 2, 4, 5), new DoorPlacement(new(3, 7), EdgeOrientation.North), [new(new(1, 4), EdgeOrientation.West), new(new(5, 3), EdgeOrientation.West)]))),
        new CommandRequestMessage(new PickUpObjectCommand(12)),
        new CommandRequestMessage(new ConfirmRoomCommand(3)),
        new CommandRequestMessage(new CancelRoomCommand(4)),
        new ScheduledCommandMessage(new ScheduledCommand(10, 2, 1, new SpawnHumanoidCommand(new(1, 2)))),
        new AdvanceMessage(123),
        new StateHashMessage(64, 0xDEADBEEFCAFEF00D),
        new DesyncMessage(64, 1, 1, 2),
    ];

    [Theory]
    [MemberData(nameof(Messages))]
    public void RoundTripsMessages(Message message)
    {
        Assert.Equal(message, MessageCodec.Decode(MessageCodec.Encode(message)));
    }

    [Fact]
    public void RoundTripsWelcomeState()
    {
        var welcome = new WelcomeMessage(1, [1, 2, 3, 4], 77);

        var decoded = Assert.IsType<WelcomeMessage>(MessageCodec.Decode(MessageCodec.Encode(welcome)));

        Assert.Equal((1, 77L), (decoded.Player, decoded.AllowedTick));
        Assert.Equal(welcome.State, decoded.State);
    }

    [Fact]
    public void RejectsUnknownTypeAndTrailingBytes()
    {
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode([200]));

        byte[] withExtra = [.. MessageCodec.Encode(new AdvanceMessage(1)), 0];
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(withExtra));
    }

    [Fact]
    public void RejectsTruncatedMessages()
    {
        var bytes = MessageCodec.Encode(new StateHashMessage(1, 2));

        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(bytes.AsSpan(0, bytes.Length - 1)));
    }
}
