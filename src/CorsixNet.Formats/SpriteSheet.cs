using System.Buffers.Binary;

namespace CorsixNet.Formats;

/// <summary>
/// A set of sprites stored as a table file (<c>.TAB</c>) and a chunk data file (<c>.DAT</c>).
/// </summary>
/// <remarks>
/// Each 6-byte table entry holds the little-endian offset of the sprite's chunk data,
/// then its width and height in pixels. Entries with zero width or height are empty.
/// </remarks>
public sealed class SpriteSheet
{
    /// <summary>Size of one table entry in bytes.</summary>
    public const int TableEntrySize = 6;

    private readonly IndexedImage[] _sprites;

    private SpriteSheet(IndexedImage[] sprites)
    {
        _sprites = sprites;
    }

    public int Count => _sprites.Length;

    public IndexedImage this[int index] => _sprites[index];

    /// <summary>A sheet of the given sprites, for example a recoloured copy of part of another sheet.</summary>
    public static SpriteSheet FromImages(IEnumerable<IndexedImage> sprites)
    {
        return new SpriteSheet([.. sprites]);
    }

    /// <summary>Decodes a sprite sheet from decompressed table and chunk data.</summary>
    /// <param name="table">Contents of the <c>.TAB</c> file.</param>
    /// <param name="data">Contents of the <c>.DAT</c> file.</param>
    /// <param name="complex">Whether the sheet uses the complex chunk encoding (most UI sheets do).</param>
    public static SpriteSheet Parse(ReadOnlySpan<byte> table, ReadOnlySpan<byte> data, bool complex)
    {
        // A trailing partial entry is ignored, as in CorsixTH.
        var sprites = new IndexedImage[table.Length / TableEntrySize];
        for (var i = 0; i < sprites.Length; i++)
        {
            var entry = table.Slice(i * TableEntrySize, TableEntrySize);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            int width = entry[4];
            int height = entry[5];

            if (width == 0 || height == 0)
            {
                sprites[i] = IndexedImage.Empty;
                continue;
            }

            var chunks = offset < (uint)data.Length ? data[(int)offset..] : [];
            sprites[i] = new IndexedImage(width, height, ChunkDecoder.Decode(chunks, width, height, complex));
        }

        return new SpriteSheet(sprites);
    }
}
