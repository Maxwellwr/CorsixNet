using System.Buffers.Binary;

namespace CorsixNet.Formats.Tests;

public class AnimationSetTests
{
    [Fact]
    public void ParsesFramesAndElements()
    {
        // Animation 0 starts at frame 1; frames 1 -> 2 -> 1 form a loop.
        byte[] starts = [.. U16(1), 0, 0];
        byte[] frames = [.. Frame(listIndex: 0, next: 0), .. Frame(listIndex: 0, next: 2, sound: 7), .. Frame(listIndex: 3, next: 1)];
        byte[] lists = [.. U16(0), .. U16(1), .. U16(0xFFFF), .. U16(1), .. U16(0xFFFF)];
        byte[] elements = [.. Element(sprite: 3, x: 141, y: 186), .. Element(sprite: 5, x: 151, y: 180, flags: 1, layer: 2, layerId: 4)];

        var set = AnimationSet.Parse(starts, frames, lists, elements);

        Assert.Equal(1, set.AnimationCount);
        Assert.Equal(1, set.FirstFrame(0));
        Assert.Equal(2, set.Length(1));
        Assert.Equal(2, set.Advance(1, 1));
        Assert.Equal(1, set.Advance(1, 2));
        Assert.Equal(7, set.Frame(1).Sound);
        Assert.Equal(
            [new AnimationElement(3, 0, 0, 0, 0, 0), new AnimationElement(5, 10, -6, 1, 2, 4)],
            set.Elements(1));
        Assert.Equal([new AnimationElement(5, 10, -6, 1, 2, 4)], set.Elements(2));
    }

    [Fact]
    public void ClampsOutOfRangeReferences()
    {
        byte[] starts = [.. U16(99), 0, 0];
        byte[] frames = Frame(listIndex: 50, next: 70);
        byte[] lists = [.. U16(9)];
        byte[] elements = Element(sprite: 0, x: 0, y: 0, layer: 14);

        var set = AnimationSet.Parse(starts, frames, lists, elements);

        Assert.Equal(0, set.FirstFrame(0));
        Assert.Equal(new AnimationFrame(0, 0, 0, 0), set.Frame(0));
        Assert.Empty(set.Elements(0));
    }

    [Fact]
    public void RejectsEmptyData()
    {
        Assert.Throws<InvalidDataException>(() => AnimationSet.Parse([], [], [], []));
    }

    [Fact]
    public void LoadsGameAnimations()
    {
        var install = RealGameData.Require();
        var sprites = install.LoadSpriteSheet("DATA", "VSPR-0", complex: false);

        var set = AnimationSet.Load(install);

        Assert.True(set.AnimationCount > 1000);
        for (var frame = 0; frame < set.FrameCount; frame++)
        {
            Assert.All(set.Elements(frame), element => Assert.InRange(element.Sprite, 0, sprites.Count));
        }
    }

    [Theory]
    [InlineData(16)] // standard male patient, walk north
    [InlineData(18)] // standard male patient, walk east
    [InlineData(0)]  // standard female patient, walk north
    [InlineData(2)]  // standard female patient, walk east
    public void WalkCyclesAreSixFrames(int animation)
    {
        // A step takes 8 ticks (Humanoid.TicksPerStep) but the walk cycle is 6 frames, so the
        // client must keep the cycle running across steps, as CorsixTH does (HumanoidDrawing).
        var set = AnimationSet.Load(RealGameData.Require());

        Assert.Equal(6, set.Length(set.FirstFrame(animation)));
    }

    private static byte[] U16(int value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
        return bytes;
    }

    private static byte[] Frame(int listIndex, int next, byte sound = 0)
    {
        var bytes = new byte[10];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)listIndex);
        bytes[6] = sound;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), (ushort)next);
        return bytes;
    }

    private static byte[] Element(int sprite, int x, int y, byte flags = 0, byte layer = 0, byte layerId = 0)
    {
        var bytes = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)(sprite * 6));
        bytes[2] = (byte)x;
        bytes[3] = (byte)y;
        bytes[4] = (byte)((layer << 4) | flags);
        bytes[5] = layerId;
        return bytes;
    }
}
