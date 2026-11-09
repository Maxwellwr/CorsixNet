using System.Buffers.Binary;
using System.Text;
using CorsixNet.Content.Audio;
using CorsixNet.Formats;

namespace CorsixNet.Content.Tests;

public class SoundChoiceTests
{
    [Fact]
    public void AWildcardStandsForAnyMatchingSound()
    {
        var choice = new SoundChoice(SoundArchive.Parse(Archive("cough1.wav", "COUGH2.WAV", "sneeze.wav")));
        var counts = new List<int>();

        Assert.Equal("selectx.wav", choice.Resolve("selectx.wav", _ => throw new InvalidOperationException()));
        Assert.Equal("COUGH2.WAV", choice.Resolve("cough*.wav", count => { counts.Add(count); return 1; }));
        Assert.Equal("COUGH1.WAV", choice.Resolve("Cough*.WAV", count => { counts.Add(count); return 0; }));
        Assert.Equal("yawn*.wav", choice.Resolve("yawn*.wav", _ => throw new InvalidOperationException()));
        Assert.Equal([2, 2], counts);
    }

    [Fact]
    public void ASoundIsQuieterFurtherFromTheCentre()
    {
        var radius = SoundChoice.StageRadius(800, 600);

        Assert.Equal(500, radius);
        Assert.Equal(0.5, SoundChoice.VolumeAt(0.5, 0, 0, radius));
        Assert.Equal(0.3, SoundChoice.VolumeAt(0.5, 150, 200, radius)!.Value, 10);
        Assert.Equal(0.1, SoundChoice.VolumeAt(0.5, -400, 300, radius)!.Value, 10);
        Assert.Null(SoundChoice.VolumeAt(0.5, 400, 301, radius));
        Assert.Equal(2, SoundChoice.StageRadius(3, 3));
    }

    /// <summary>A sound archive of empty sounds: the table (its first entry spanning everything), the header, its position.</summary>
    private static byte[] Archive(params string[] names)
    {
        var entries = new byte[(names.Length + 1) * 32];
        for (var i = 0; i < names.Length; i++)
        {
            Encoding.Latin1.GetBytes(names[i]).CopyTo(entries.AsSpan((i + 1) * 32));
        }

        var header = new byte[234];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(50), 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(58), entries.Length);
        return [.. entries, .. header, .. BitConverter.GetBytes(entries.Length)];
    }
}
