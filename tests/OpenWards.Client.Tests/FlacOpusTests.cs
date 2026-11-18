using System.Buffers.Binary;
using OpenWards.Client.Audio;

namespace OpenWards.Client.Tests;

/// <summary>
/// The player's own music in FLAC and Opus. The files in Audio were made with ffmpeg from sine tones, the .raw ones by
/// ffmpeg decoding the FLAC files, so FLAC (lossless) must come out sample for sample.
/// </summary>
public sealed class FlacOpusTests
{
    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "Audio", name);

    private static float[] ReadAll(IPcmSource source)
    {
        var samples = new List<float>();
        var buffer = new float[1000];
        int read;
        while ((read = source.Read(buffer)) > 0)
        {
            samples.AddRange(buffer.Take(read));
        }

        return [.. samples];
    }

    [Fact]
    public void AMono16BitFlacDecodesExactly()
    {
        using var source = PcmSources.Open("song.flac", File.OpenRead(Asset("mono16.flac")))!;
        Assert.Equal((22050, 1), (source.SampleRate, source.Channels));
        var raw = File.ReadAllBytes(Asset("mono16.raw"));
        var expected = Enumerable.Range(0, raw.Length / 2).Select(i => BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(i * 2)) / 32768f).ToArray();

        Assert.Equal(expected, ReadAll(source));
    }

    [Fact]
    public void AStereo24BitFlacWithDecorrelatedChannelsDecodesExactly()
    {
        using var source = PcmSources.Open("song.FLAC", File.OpenRead(Asset("stereo24.flac")))!;
        Assert.Equal((44100, 2), (source.SampleRate, source.Channels));

        // ffmpeg writes 24-bit samples in the top bits of 32-bit ones.
        var raw = File.ReadAllBytes(Asset("stereo24.raw"));
        var expected = Enumerable.Range(0, raw.Length / 4).Select(i => (BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(i * 4)) >> 8) / 8388608f).ToArray();
        var decoded = ReadAll(source);

        Assert.Equal(expected.Length, decoded.Length);
        Assert.Equal(expected, decoded);
        Assert.Contains(decoded, sample => sample > 0.5f);
    }

    [Fact]
    public void AnOpusFileDecodesToStereoAt48Khz()
    {
        using var source = PcmSources.Open("song.opus", File.OpenRead(Asset("stereo.opus")))!;
        Assert.Equal((48000, 2), (source.SampleRate, source.Channels));
        var decoded = ReadAll(source);

        // Half a second, give or take the codec's padding; both channels carry their tones.
        Assert.InRange(decoded.Length / 2, 22000, 26000);
        Assert.Contains(decoded.Where((_, i) => i % 2 == 0), sample => sample > 0.3f);
        Assert.Contains(decoded.Where((_, i) => i % 2 == 1), sample => sample > 0.3f);
    }

    [Fact]
    public void FlacAndOpusCountAsOwnMusic()
    {
        Assert.Contains("FLAC", PcmSources.Extensions);
        Assert.Contains("OPUS", PcmSources.Extensions);
        Assert.Throws<InvalidDataException>(() => new FlacDecoder(new MemoryStream("not flac"u8.ToArray())));
    }
}
