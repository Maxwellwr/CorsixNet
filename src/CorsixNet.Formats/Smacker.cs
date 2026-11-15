// A reader of Smacker movies (.SMK, version 2), as the original's intro, level and end movies, written from the
// published description of the format (the Smacker file format pages of the MultimediaWiki). CorsixTH plays them with
// FFmpeg (CorsixTH/Src/th_movie.cpp); this is our own decoder.

using System.Buffers.Binary;

namespace CorsixNet.Formats;

/// <summary>An audio track of a Smacker movie: its sample rate, channels and sample size.</summary>
public sealed record SmackerAudioTrack(int SampleRate, int Channels, int BitsPerSample, bool Compressed);

/// <summary>
/// A Smacker movie: its frames' pictures (8-bit indexed with their palette) and sound, decoded one frame after the
/// other with <see cref="SmackerDecoder"/>.
/// </summary>
public sealed class SmackerMovie
{
    private const int HeaderSize = 104;

    private readonly byte[] _data;
    private readonly int[] _frameOffsets;
    private readonly int[] _frameSizes;
    private readonly byte[] _frameTypes;

    private SmackerMovie(byte[] data, int width, int height, int frames, double frameMilliseconds, int flags, int treesSize, int[] treeSizes, SmackerAudioTrack?[] audio, int[] frameOffsets, int[] frameSizes, byte[] frameTypes)
    {
        _data = data;
        Width = width;
        Height = height;
        FrameCount = frames;
        FrameMilliseconds = frameMilliseconds;
        Flags = flags;
        TreesOffset = HeaderSize + (frameSizes.Length * 5);
        TreesSize = treesSize;
        TreeSizes = treeSizes;
        Audio = audio;
        _frameOffsets = frameOffsets;
        _frameSizes = frameSizes;
        _frameTypes = frameTypes;
    }

    /// <summary>The pictures' width in pixels.</summary>
    public int Width { get; }

    /// <summary>The pictures' height in pixels as stored; doubled or interlaced movies show twice as high (<see cref="DisplayHeight"/>).</summary>
    public int Height { get; }

    /// <summary>The height to show the pictures at: twice <see cref="Height"/> for a Y-doubled or Y-interlaced movie.</summary>
    public int DisplayHeight => (Flags & 6) != 0 ? Height * 2 : Height;

    /// <summary>How many frames the movie has, not counting the ring frame.</summary>
    public int FrameCount { get; }

    /// <summary>How long each frame shows, in milliseconds.</summary>
    public double FrameMilliseconds { get; }

    /// <summary>The header's flags: 1 a ring frame, 2 Y interlaced, 4 Y doubled.</summary>
    public int Flags { get; }

    /// <summary>The audio tracks, by number (0 to 6); null where a track is not there.</summary>
    public IReadOnlyList<SmackerAudioTrack?> Audio { get; }

    internal int TreesOffset { get; }

    internal int TreesSize { get; }

    internal IReadOnlyList<int> TreeSizes { get; }

    internal ReadOnlySpan<byte> Data => _data;

    /// <summary>A frame's data and type byte (bit 0: a palette; bits 1 to 7: audio tracks 0 to 6).</summary>
    internal (int Offset, int Size, byte Type) Frame(int index) => (_frameOffsets[index], _frameSizes[index], _frameTypes[index]);

    /// <summary>Reads a movie's header and frame table.</summary>
    /// <exception cref="InvalidDataException">The data is not a Smacker 2 movie or is cut short.</exception>
    public static SmackerMovie Parse(byte[] data)
    {
        if (data.Length < HeaderSize || !data.AsSpan(0, 4).SequenceEqual("SMK2"u8))
        {
            throw new InvalidDataException("Not a Smacker 2 movie.");
        }

        int U32(int at) => (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at));
        var width = U32(4);
        var height = U32(8);
        var frames = U32(12);
        var rate = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16));
        var flags = U32(20);
        var treesSize = U32(52);
        int[] treeSizes = [U32(56), U32(60), U32(64), U32(68)];
        if (width is <= 0 or > 4096 || height is <= 0 or > 4096 || frames is <= 0 or > 100_000)
        {
            throw new InvalidDataException("Smacker movie of an impossible size.");
        }

        // A positive rate is milliseconds a frame, a negative one tens of microseconds, none ten frames a second.
        var frameMilliseconds = rate > 0 ? rate : rate < 0 ? -rate / 100.0 : 100.0;
        var audio = new SmackerAudioTrack?[7];
        for (var i = 0; i < 7; i++)
        {
            var info = (uint)U32(72 + (i * 4));
            if ((info & 0x40000000) != 0)
            {
                audio[i] = new SmackerAudioTrack((int)(info & 0xFFFFFF), (info & 0x10000000) != 0 ? 2 : 1, (info & 0x20000000) != 0 ? 16 : 8, (info & 0x80000000) != 0);
            }
        }

        // The ring frame, kept for looping, is not played.
        var stored = frames + ((flags & 1) != 0 ? 1 : 0);
        var tableEnd = HeaderSize + (stored * 5);
        if (tableEnd + treesSize > data.Length)
        {
            throw new InvalidDataException("Smacker movie cut short.");
        }

        var sizes = new int[stored];
        var types = new byte[stored];
        var offsets = new int[stored];
        var offset = tableEnd + treesSize;
        for (var i = 0; i < stored; i++)
        {
            sizes[i] = U32(HeaderSize + (i * 4)) & ~3;
            types[i] = data[HeaderSize + (stored * 4) + i];
            offsets[i] = offset;
            offset += sizes[i];
        }

        if (offset > data.Length)
        {
            throw new InvalidDataException("Smacker movie cut short.");
        }

        return new SmackerMovie(data, width, height, frames, frameMilliseconds, flags, treesSize, treeSizes, audio, offsets, sizes, types);
    }
}

/// <summary>A decoded frame: its picture's palette indices and palette, and its sound by track.</summary>
/// <param name="Pixels">Width × height palette indices, row after row.</param>
/// <param name="Palette">256 colours, red, green and blue bytes each.</param>
/// <param name="Audio">For each track with sound in this frame, its samples (16-bit little-endian or 8-bit unsigned, channels interleaved).</param>
public sealed record SmackerFrame(byte[] Pixels, byte[] Palette, IReadOnlyDictionary<int, byte[]> Audio);

/// <summary>Decodes a Smacker movie's frames in order, each from the picture before it.</summary>
public sealed class SmackerDecoder
{
    /// <summary>How many blocks of 4 by 4 pixels a block type's run covers, by its six bits.</summary>
    private static readonly int[] RunLengths = [.. Enumerable.Range(1, 59), 128, 256, 512, 1024, 2048];

    private readonly SmackerMovie _movie;
    private readonly BigTree _mmap;
    private readonly BigTree _mclr;
    private readonly BigTree _full;
    private readonly BigTree _type;
    private readonly byte[] _pixels;
    private readonly byte[] _palette = new byte[768];
    private int _next;

    public SmackerDecoder(SmackerMovie movie)
    {
        _movie = movie;
        _pixels = new byte[movie.Width * movie.Height];
        var reader = new BitReader(movie.Data.Slice(movie.TreesOffset, movie.TreesSize).ToArray());
        _mmap = BigTree.Read(ref reader);
        _mclr = BigTree.Read(ref reader);
        _full = BigTree.Read(ref reader);
        _type = BigTree.Read(ref reader);
    }

    /// <summary>The number of the next frame <see cref="Next"/> decodes.</summary>
    public int Position => _next;

    /// <summary>Whether every frame has been decoded.</summary>
    public bool IsFinished => _next >= _movie.FrameCount;

    /// <summary>Decodes the next frame.</summary>
    /// <exception cref="InvalidOperationException">Every frame has been decoded.</exception>
    /// <exception cref="InvalidDataException">The frame's data is broken.</exception>
    public SmackerFrame Next()
    {
        if (IsFinished)
        {
            throw new InvalidOperationException("The movie has no more frames.");
        }

        var (offset, size, type) = _movie.Frame(_next++);
        var data = _movie.Data.Slice(offset, size);
        var at = 0;
        if ((type & 1) != 0)
        {
            // The palette chunk: its length in fours of bytes, then the changes from the palette before.
            var length = data[0] * 4;
            ReadPalette(data.Slice(1, Math.Max(0, length - 1)));
            at = length;
        }

        var audio = new Dictionary<int, byte[]>();
        for (var track = 0; track < 7; track++)
        {
            if ((type & (2 << track)) == 0)
            {
                continue;
            }

            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
            if (length < 4 || at + length > data.Length)
            {
                throw new InvalidDataException("Smacker audio chunk out of bounds.");
            }

            if (_movie.Audio[track] is { } info)
            {
                audio[track] = info.Compressed ? DecodeAudio(data.Slice(at + 4, length - 4)) : data.Slice(at + 4, length - 4).ToArray();
            }

            at += length;
        }

        DecodeVideo(data[at..].ToArray());
        return new SmackerFrame((byte[])_pixels.Clone(), (byte[])_palette.Clone(), audio);
    }

    /// <summary>
    /// The palette chunk: a byte with the high bit skips that many entries plus one; with bit 6, copies that many plus one
    /// from the old palette, from the entry the next byte names; otherwise it and the next two bytes are a colour of six
    /// bits a component.
    /// </summary>
    private void ReadPalette(ReadOnlySpan<byte> chunk)
    {
        var old = (byte[])_palette.Clone();
        var entry = 0;
        var at = 0;
        while (entry < 256 && at < chunk.Length)
        {
            var b = chunk[at++];
            if ((b & 0x80) != 0)
            {
                entry += (b & 0x7F) + 1;
            }
            else if ((b & 0x40) != 0)
            {
                var count = (b & 0x3F) + 1;
                if (at >= chunk.Length)
                {
                    break;
                }

                var source = chunk[at++];
                for (var i = 0; i < count && entry < 256 && source + i < 256; i++, entry++)
                {
                    Array.Copy(old, (source + i) * 3, _palette, entry * 3, 3);
                }
            }
            else
            {
                if (at + 2 > chunk.Length)
                {
                    break;
                }

                _palette[entry * 3] = Expand(b);
                _palette[(entry * 3) + 1] = Expand(chunk[at++]);
                _palette[(entry * 3) + 2] = Expand(chunk[at++]);
                entry++;
            }
        }
    }

    /// <summary>A six-bit colour component made eight bits wide.</summary>
    private static byte Expand(byte value)
    {
        var six = value & 0x3F;
        return (byte)((six << 2) | (six >> 4));
    }

    /// <summary>
    /// The picture in blocks of 4 by 4 pixels, row after row: each block type from the type tree, with a run of blocks:
    /// two-colour blocks (colours and pattern from their trees), full blocks of their own pixels, blocks left as they
    /// were, and blocks of one colour.
    /// </summary>
    private void DecodeVideo(byte[] data)
    {
        _mmap.ResetCache();
        _mclr.ResetCache();
        _full.ResetCache();
        _type.ResetCache();
        var reader = new BitReader(data);
        var width = _movie.Width;
        var blocksAcross = width / 4;
        var blocks = blocksAcross * (_movie.Height / 4);
        var block = 0;
        while (block < blocks)
        {
            var value = _type.Decode(ref reader);
            var kind = value & 3;
            var run = RunLengths[(value >> 2) & 0x3F];
            var colour = (byte)(value >> 8);
            for (var i = 0; i < run && block < blocks; i++, block++)
            {
                var start = ((block / blocksAcross) * 4 * width) + ((block % blocksAcross) * 4);
                switch (kind)
                {
                    case 0:
                        var colours = _mclr.Decode(ref reader);
                        var pattern = _mmap.Decode(ref reader);
                        var high = (byte)(colours >> 8);
                        var low = (byte)colours;
                        for (var y = 0; y < 4; y++)
                        {
                            for (var x = 0; x < 4; x++)
                            {
                                _pixels[start + (y * width) + x] = (pattern & (1 << ((y * 4) + x))) != 0 ? high : low;
                            }
                        }

                        break;
                    case 1:
                        for (var y = 0; y < 4; y++)
                        {
                            // Each row as two pairs of pixels, the right pair first.
                            var row = start + (y * width);
                            var right = _full.Decode(ref reader);
                            _pixels[row + 2] = (byte)right;
                            _pixels[row + 3] = (byte)(right >> 8);
                            var left = _full.Decode(ref reader);
                            _pixels[row] = (byte)left;
                            _pixels[row + 1] = (byte)(left >> 8);
                        }

                        break;
                    case 2:
                        break;
                    default:
                        for (var y = 0; y < 4; y++)
                        {
                            _pixels.AsSpan(start + (y * width), 4).Fill(colour);
                        }

                        break;
                }
            }
        }
    }

    /// <summary>
    /// A compressed audio chunk: the samples' byte count, flags, a Huffman tree for each channel (two for 16-bit samples,
    /// low and high byte), the first sample of each channel, then the differences from the sample before.
    /// </summary>
    private static byte[] DecodeAudio(ReadOnlySpan<byte> chunk)
    {
        if (chunk.Length < 4)
        {
            return [];
        }

        var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(chunk);
        var reader = new BitReader(chunk[4..].ToArray());
        if (!reader.ReadBit())
        {
            return new byte[size];
        }

        var stereo = reader.ReadBit();
        var sixteen = reader.ReadBit();
        var channels = stereo ? 2 : 1;
        var trees = new SmallTree[channels * (sixteen ? 2 : 1)];
        for (var i = 0; i < trees.Length; i++)
        {
            trees[i] = SmallTree.Read(ref reader);
        }

        var output = new byte[size];
        var predictions = new int[channels];
        for (var channel = channels - 1; channel >= 0; channel--)
        {
            if (sixteen)
            {
                // Sixteen bits, the high byte read first.
                var high = reader.ReadBits(8);
                var low = reader.ReadBits(8);
                predictions[channel] = (short)((high << 8) | low);
            }
            else
            {
                predictions[channel] = reader.ReadBits(8);
            }
        }

        var bytesPerSample = sixteen ? 2 : 1;
        var at = 0;
        void Write(int sample)
        {
            if (sixteen)
            {
                if (at + 2 <= output.Length)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(at), (short)sample);
                }
            }
            else if (at < output.Length)
            {
                output[at] = (byte)sample;
            }

            at += bytesPerSample;
        }

        for (var channel = 0; channel < channels; channel++)
        {
            Write(predictions[channel]);
        }

        while (at + (bytesPerSample * channels) <= output.Length)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                if (sixteen)
                {
                    var low = trees[channel * 2].Decode(ref reader);
                    var high = trees[(channel * 2) + 1].Decode(ref reader);
                    predictions[channel] = (short)(predictions[channel] + (short)(low | (high << 8)));
                }
                else
                {
                    predictions[channel] = (byte)(predictions[channel] + (sbyte)trees[channel].Decode(ref reader));
                }

                Write(predictions[channel]);
            }
        }

        return output;
    }

    /// <summary>The bits of a chunk, from the low bit of each byte up.</summary>
    internal ref struct BitReader(byte[] data)
    {
        private readonly byte[] _data = data;
        private int _bit;

        public bool ReadBit()
        {
            var index = _bit >> 3;
            // Past the end reads zeros, as a broken chunk should not stop the movie.
            var value = index < _data.Length && (_data[index] & (1 << (_bit & 7))) != 0;
            _bit++;
            return value;
        }

        public int ReadBits(int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++)
            {
                if (ReadBit())
                {
                    value |= 1 << i;
                }
            }

            return value;
        }
    }

    /// <summary>A Huffman tree of bytes: nodes of two branches (bit 0, then bit 1) and leaves of eight bits.</summary>
    internal sealed class SmallTree
    {
        private const int Leaf = 1 << 30;

        private readonly List<int> _nodes = [];

        /// <summary>A tree that is there with a flag bit, then its nodes, then a closing bit; none gives 0 always.</summary>
        public static SmallTree Read(ref BitReader reader)
        {
            var tree = new SmallTree();
            if (reader.ReadBit())
            {
                tree.ReadNode(ref reader, 0);
                reader.ReadBit();
            }

            return tree;
        }

        // Nodes are stored as: a leaf, value | Leaf; a branch, the index of its bit-1 child (its bit-0 child follows it).
        private void ReadNode(ref BitReader reader, int depth)
        {
            if (depth > 32)
            {
                throw new InvalidDataException("Smacker tree too deep.");
            }

            if (reader.ReadBit())
            {
                var index = _nodes.Count;
                _nodes.Add(0);
                ReadNode(ref reader, depth + 1);
                _nodes[index] = _nodes.Count;
                ReadNode(ref reader, depth + 1);
            }
            else
            {
                _nodes.Add(reader.ReadBits(8) | Leaf);
            }
        }

        public int Decode(ref BitReader reader)
        {
            if (_nodes.Count == 0)
            {
                return 0;
            }

            var index = 0;
            while ((_nodes[index] & Leaf) == 0)
            {
                index = reader.ReadBit() ? _nodes[index] : index + 1;
            }

            return _nodes[index] & 0xFF;
        }
    }

    /// <summary>
    /// A Huffman tree of 16-bit values, its leaves made of a low and a high byte from two byte trees; three escape values
    /// mark the leaves that give back the last three values decoded instead.
    /// </summary>
    internal sealed class BigTree
    {
        private const int Leaf = 1 << 30;

        private readonly List<int> _nodes = [];
        private readonly int[] _last = new int[3];
        private readonly int[] _escapeLeaves = [-1, -1, -1];

        public static BigTree Read(ref BitReader reader)
        {
            var tree = new BigTree();
            if (!reader.ReadBit())
            {
                return tree;
            }

            var low = SmallTree.Read(ref reader);
            var high = SmallTree.Read(ref reader);
            int[] escapes = [reader.ReadBits(16), reader.ReadBits(16), reader.ReadBits(16)];
            tree.ReadNode(ref reader, low, high, escapes, 0);
            reader.ReadBit();
            return tree;
        }

        // A leaf: value | Leaf and, for an escape leaf, which (1 to 3) << 16; a branch: the index of its bit-1 child, which
        // big trees take well past 65,536.
        private void ReadNode(ref BitReader reader, SmallTree low, SmallTree high, int[] escapes, int depth)
        {
            if (depth > 32)
            {
                throw new InvalidDataException("Smacker tree too deep.");
            }

            if (reader.ReadBit())
            {
                var index = _nodes.Count;
                _nodes.Add(0);
                ReadNode(ref reader, low, high, escapes, depth + 1);
                _nodes[index] = _nodes.Count;
                ReadNode(ref reader, low, high, escapes, depth + 1);
                return;
            }

            var value = low.Decode(ref reader) | (high.Decode(ref reader) << 8);
            var leaf = value | Leaf;
            for (var i = 0; i < 3; i++)
            {
                if (value == escapes[i] && _escapeLeaves[i] < 0)
                {
                    _escapeLeaves[i] = _nodes.Count;
                    leaf = Leaf | ((i + 1) << 16);
                    break;
                }
            }

            _nodes.Add(leaf);
        }

        public void ResetCache() => Array.Clear(_last);

        public int Decode(ref BitReader reader)
        {
            if (_nodes.Count == 0)
            {
                return 0;
            }

            var index = 0;
            while ((_nodes[index] & Leaf) == 0)
            {
                index = reader.ReadBit() ? _nodes[index] : index + 1;
            }

            var node = _nodes[index];
            var escape = (node >> 16) & 3;
            var value = escape != 0 ? _last[escape - 1] : node & 0xFFFF;
            if (_last[0] != value)
            {
                _last[2] = _last[1];
                _last[1] = _last[0];
                _last[0] = value;
            }

            return value;
        }
    }
}
