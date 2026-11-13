using System.Buffers.Binary;
using CorsixNet.Client.Audio;
using CorsixNet.Formats;

namespace CorsixNet.Client.Tests;

/// <summary>Sounds on animation frames (CorsixTH's animation::tick and frame_sound_replacements).</summary>
public class FrameSoundsTests
{
    [Fact]
    public void AFrameWithASoundSoundsWhenSomethingDrawnMovesOntoIt()
    {
        // Frames 0 -> 1 -> 0: frame 1 has sound 7.
        byte[] starts = [.. U16(0), 0, 0];
        byte[] frames = [.. Frame(next: 1), .. Frame(next: 0, sound: 7)];
        var set = AnimationSet.Parse(starts, frames, [.. U16(0xFFFF)], new byte[6]);
        var sounds = new FrameSounds();

        // Seen for the first time on the sounding frame: nothing.
        sounds.BeginFrame();
        sounds.Drawn(set, key: 5, frame: 1, x: 10, y: 20);
        Assert.Empty(sounds.TakeDue());

        sounds.BeginFrame();
        sounds.Drawn(set, 5, 0, 10, 20);
        Assert.Empty(sounds.TakeDue());

        // Onto frame 1: its sound, where it was drawn; staying on it, no more.
        sounds.BeginFrame();
        sounds.Drawn(set, 5, 1, 12, 22);
        Assert.Equal([(7, 12, 22)], sounds.TakeDue());
        sounds.BeginFrame();
        sounds.Drawn(set, 5, 1, 12, 22);
        Assert.Empty(sounds.TakeDue());

        // Not drawn for a while, it is forgotten: back on frame 0 then 1 it is new again.
        sounds.BeginFrame();
        sounds.TakeDue();
        sounds.BeginFrame();
        sounds.Drawn(set, 5, 1, 12, 22);
        Assert.Empty(sounds.TakeDue());
    }

    [Fact]
    public void CorsixThReplacesTheSoundsOfSomeFrames()
    {
        var set = AnimationSet.Parse([.. U16(0), 0, 0], Frame(next: 0), [.. U16(0xFFFF)], new byte[6]);
        var sounds = new FrameSounds();

        // Vomiting (frame 1902 of animation 1034): PUKE.WAV or PUKE2.WAV (58 or 114).
        Assert.Contains(sounds.SoundOf(set, 1902), new[] { 58, 114 });
        Assert.Equal(123, sounds.SoundOf(set, 6987));
    }

    private static byte[] U16(int value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
        return bytes;
    }

    private static byte[] Frame(int next, byte sound = 0)
    {
        var bytes = new byte[10];
        bytes[6] = sound;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), (ushort)next);
        return bytes;
    }
}
