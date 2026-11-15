using System.Buffers.Binary;

namespace CorsixNet.Formats.Tests;

public class SmackerTests
{
    [Fact]
    public void ReadsTheHeaderAndDecodesASolidFrameWithItsPaletteAndSound()
    {
        var movie = SmackerMovie.Parse(SolidMovie());

        Assert.Equal(8, movie.Width);
        Assert.Equal(4, movie.Height);
        Assert.Equal(1, movie.FrameCount);
        Assert.Equal(71.0, movie.FrameMilliseconds);
        Assert.Equal(new SmackerAudioTrack(22050, 1, 8, Compressed: false), movie.Audio[0]);

        var decoder = new SmackerDecoder(movie);
        var frame = decoder.Next();
        Assert.True(decoder.IsFinished);
        Assert.All(frame.Pixels, pixel => Assert.Equal(7, pixel));

        // Colour 7 is (63, 0, 32) in six bits: 255, 0, 130 in eight.
        Assert.Equal([255, 0, 130], frame.Palette[21..24]);
        Assert.Equal([1, 2, 3], frame.Audio[0]);
        Assert.Throws<InvalidOperationException>(decoder.Next);
    }

    [Fact]
    public void RejectsWhatIsNotASmacker2Movie()
    {
        Assert.Throws<InvalidDataException>(() => SmackerMovie.Parse(new byte[10]));
        var movie = SolidMovie();
        movie[3] = (byte)'4';
        Assert.Throws<InvalidDataException>(() => SmackerMovie.Parse(movie));
        Assert.Throws<InvalidDataException>(() => SmackerMovie.Parse(SolidMovie()[..110]));
    }

    [Fact]
    public void DecodesTheRealMovies()
    {
        var install = RealGameData.Require();
        var movies = install.Movies();
        Assert.NotNull(movies.Intro);
        Assert.NotNull(movies.WinLevel);
        Assert.Equal(Enumerable.Range(1, 6), movies.Lose.Keys);
        Assert.Contains(12, movies.Advance.Keys);

        // The intro's big trees have more than 65,536 nodes; a frame well into it shows a picture of many colours.
        var intro = SmackerMovie.Parse(File.ReadAllBytes(movies.Intro));
        Assert.Equal((320, 200), (intro.Width, intro.Height));
        Assert.Equal(new SmackerAudioTrack(22050, 2, 16, Compressed: true), intro.Audio[0]);
        var decoder = new SmackerDecoder(intro);
        SmackerFrame frame = null!;
        var samples = 0L;
        for (var i = 0; i <= 400; i++)
        {
            frame = decoder.Next();
            samples += frame.Audio.GetValueOrDefault(0)?.Length ?? 0;
        }

        Assert.True(frame.Pixels.Distinct().Count() > 64);
        Assert.True(samples > 0);

        foreach (var path in movies.Lose.Values.Concat(movies.Advance.Values).Append(movies.WinLevel!).Append(movies.WinGame!))
        {
            var movie = SmackerMovie.Parse(File.ReadAllBytes(path));
            var each = new SmackerDecoder(movie);
            while (!each.IsFinished)
            {
                each.Next();
            }
        }
    }

    /// <summary>
    /// A movie of one 8 by 4 frame: a type tree of a single leaf, solid blocks of colour 7 in runs of 2048, a palette
    /// chunk setting colour 7, and three bytes of plain 8-bit sound on track 0.
    /// </summary>
    private static byte[] SolidMovie()
    {
        // The trees: no MMap, MClr or Full tree; the type tree a single leaf of 0x07FF.
        var bits = new BitWriter();
        bits.Write(0, 1);
        bits.Write(0, 1);
        bits.Write(0, 1);
        bits.Write(1, 1); // the type tree is there
        foreach (var half in new[] { 0xFF, 0x07 })
        {
            bits.Write(1, 1); // the byte tree is there
            bits.Write(0, 1); // a leaf
            bits.Write(half, 8);
            bits.Write(0, 1); // its end
        }

        bits.Write(0x1111, 16);
        bits.Write(0x2222, 16);
        bits.Write(0x3333, 16);
        bits.Write(0, 1); // the big tree's only leaf, decoded from the byte trees with no bits
        bits.Write(0, 1); // its end
        var trees = bits.ToArray();

        // The frame: the palette chunk (its size in fours), the audio chunk, then the video (no bits needed).
        var palette = new List<byte> { 0, 0x86 }; // skip entries 0 to 6
        palette.AddRange([63, 0, 32]);
        while ((palette.Count % 4) != 0)
        {
            palette.Add(0x80 | 0x7F);
        }

        palette[0] = (byte)(palette.Count / 4);
        var frame = new List<byte>(palette);
        frame.AddRange([7, 0, 0, 0, 1, 2, 3]);
        while ((frame.Count % 4) != 0)
        {
            frame.Add(0);
        }

        var data = new byte[104];
        "SMK2"u8.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 8);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 4);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 1);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), -7100);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(52), trees.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(72), 0x40000000u | 22050);
        var table = new byte[5];
        BinaryPrimitives.WriteInt32LittleEndian(table, frame.Count);
        table[4] = 0x03; // a palette and track 0
        return [.. data, .. table, .. trees, .. frame];
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private int _bit;

        public void Write(int value, int count)
        {
            for (var i = 0; i < count; i++)
            {
                if ((_bit & 7) == 0)
                {
                    _bytes.Add(0);
                }

                if ((value & (1 << i)) != 0)
                {
                    _bytes[^1] |= (byte)(1 << (_bit & 7));
                }

                _bit++;
            }
        }

        public byte[] ToArray() => [.. _bytes];
    }
}
