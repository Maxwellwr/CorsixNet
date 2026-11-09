using System.Buffers.Binary;
using System.Text;

namespace CorsixNet.Formats.Tests;

public class SoundArchiveTests
{
    [Fact]
    public void ReadsTheTableLeavingOutItsFirstEntry()
    {
        var first = Wave(sampleRate: 22050, bits: 8, channels: 1, samples: 22050);
        var second = Wave(sampleRate: 11025, bits: 16, channels: 2, samples: 5512);
        var archive = SoundArchive.Parse(Build(("SELECTX.WAV", first), ("Wrong2.wav", second), ("selectx.wav", second)));

        Assert.Equal(["SELECTX.WAV", "Wrong2.wav", "selectx.wav"], archive.Sounds.Select(sound => sound.Name));
        Assert.Equal(first, archive.Read(archive.Sounds[0]).ToArray());
        Assert.Equal(second, archive.Read(archive.Sounds[1]).ToArray());

        // Names are looked up in any case of letters, the first of a name winning.
        Assert.Same(archive.Sounds[0], archive.Find("selectx.wav"));
        Assert.Same(archive.Sounds[1], archive.Find("WRONG2.WAV"));
        Assert.Null(archive.Find("missing.wav"));
    }

    [Fact]
    public void TheDurationComesFromTheWaveHeader()
    {
        var archive = SoundArchive.Parse(Build(
            ("ONE.WAV", Wave(sampleRate: 22050, bits: 8, channels: 1, samples: 22050)),
            ("HALF.WAV", Wave(sampleRate: 11025, bits: 16, channels: 2, samples: 5512)),
            ("NOTWAVE.WAV", [1, 2, 3, 4, 5, 6, 7, 8, 9])));

        Assert.Equal(1000, archive.Duration(archive.Sounds[0]));
        Assert.Equal(499, archive.Duration(archive.Sounds[1])); // 22,048 bytes at 44,100 bytes a second
        Assert.Equal(0, archive.Duration(archive.Sounds[2]));
    }

    [Fact]
    public void WildcardsMatchWholeNames()
    {
        var archive = SoundArchive.Parse(Build(("cough1.wav", []), ("COUGH2.WAV", []), ("coughing.wav", []), ("xcough1.wav", [])));

        Assert.Equal(["COUGH1.WAV", "COUGH2.WAV", "COUGHING.WAV"], archive.Matching("cough*.wav"));
        Assert.Equal(["COUGH1.WAV", "XCOUGH1.WAV"], archive.Matching("*1.WAV"));
        Assert.Empty(archive.Matching("cough.wav"));
    }

    [Fact]
    public void ASoundOutsideTheArchiveCannotBeRead()
    {
        var data = Build(("BAD.WAV", [1, 2, 3]));
        var archive = SoundArchive.Parse(data);
        var bad = archive.Sounds[0] with { Length = data.Length + 1 };

        Assert.Throws<InvalidDataException>(() => archive.Read(bad));
        Assert.Equal(0, archive.Duration(bad));
    }

    [Fact]
    public void RejectsDataThatIsNotASoundArchive()
    {
        Assert.Throws<InvalidDataException>(() => SoundArchive.Parse(new byte[100]));

        var badHeader = new byte[300];
        BinaryPrimitives.WriteUInt32LittleEndian(badHeader.AsSpan(296), 200);
        Assert.Throws<InvalidDataException>(() => SoundArchive.Parse(badHeader));

        var badTable = Build(("A.WAV", []));
        var header = BinaryPrimitives.ReadInt32LittleEndian(badTable.AsSpan(badTable.Length - 4));
        BinaryPrimitives.WriteUInt32LittleEndian(badTable.AsSpan(header + 58), 1000);
        Assert.Throws<InvalidDataException>(() => SoundArchive.Parse(badTable));
    }

    [Fact]
    public void WaveChunksAreWalkedAsCorsixThDoes()
    {
        // A LIST chunk of odd length before fmt is skipped with its pad byte.
        var wave = new List<byte>();
        wave.AddRange("RIFF"u8.ToArray());
        wave.AddRange(BitConverter.GetBytes(4048));
        wave.AddRange("WAVE"u8.ToArray());
        wave.AddRange("junk"u8.ToArray());
        wave.AddRange(BitConverter.GetBytes(3));
        wave.AddRange([9, 9, 9, 0]);
        wave.AddRange(Wave(sampleRate: 8000, bits: 8, channels: 1, samples: 4000)[12..]);

        var format = WaveFormat.Find(wave.ToArray());

        Assert.NotNull(format);
        Assert.Equal((1, 1, 8000u, 8, 4000u), (format.AudioFormat, format.Channels, format.SampleRate, format.BitsPerSample, format.DataLength));
        Assert.Null(WaveFormat.Find("RIFF"u8));
    }

    [Fact]
    public void LoadsTheRealSoundArchive()
    {
        var archive = RealGameData.Require().LoadSounds();

        Assert.NotNull(archive);
        Assert.True(archive.Sounds.Count > 100, $"{archive.Sounds.Count} sounds");
        var select = archive.Find("selectx.wav");
        Assert.NotNull(select);
        Assert.InRange(archive.Duration(select), 1, 10_000);
        Assert.All(archive.Sounds, sound => archive.Read(sound));
    }

    /// <summary>A PCM WAV file of silence.</summary>
    private static byte[] Wave(int sampleRate, int bits, int channels, int samples)
    {
        var dataLength = samples * channels * bits / 8;
        var wave = new byte[44 + dataLength];
        "RIFF"u8.CopyTo(wave);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4), 36 + dataLength);
        "WAVEfmt "u8.CopyTo(wave.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(22), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(28), sampleRate * channels * bits / 8);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(32), (short)(channels * bits / 8));
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(34), (short)bits);
        "data"u8.CopyTo(wave.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40), dataLength);
        return wave;
    }

    /// <summary>
    /// An archive as the original's: the files, then the table (a first entry spanning everything, then one per
    /// sound), then the 234-byte header, then the header's position.
    /// </summary>
    private static byte[] Build(params (string Name, byte[] Data)[] sounds)
    {
        var data = new List<byte>();
        var positions = new List<int>();
        foreach (var (_, bytes) in sounds)
        {
            positions.Add(data.Count);
            data.AddRange(bytes);
        }

        var table = data.Count;
        var entries = new byte[(sounds.Length + 1) * 32];
        for (var i = 0; i < sounds.Length; i++)
        {
            var entry = entries.AsSpan((i + 1) * 32, 32);
            Encoding.Latin1.GetBytes(sounds[i].Name).CopyTo(entry);
            BinaryPrimitives.WriteInt32LittleEndian(entry[18..], positions[i]);
            BinaryPrimitives.WriteInt32LittleEndian(entry[26..], sounds[i].Data.Length);
        }

        data.AddRange(entries);
        var header = data.Count;
        var headerBytes = new byte[234];
        BinaryPrimitives.WriteInt32LittleEndian(headerBytes.AsSpan(50), table);
        BinaryPrimitives.WriteInt32LittleEndian(headerBytes.AsSpan(58), entries.Length);
        data.AddRange(headerBytes);
        data.AddRange(BitConverter.GetBytes(header));

        // The first entry spans the whole file.
        var result = data.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(table + 26), result.Length);
        return result;
    }
}
