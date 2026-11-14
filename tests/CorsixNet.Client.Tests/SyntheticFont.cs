using System.Buffers.Binary;
using System.Text;

namespace CorsixNet.Client.Tests;

/// <summary>
/// Builds a minimal TrueType font in memory: each character a filled rectangle, with the tables stb_truetype reads
/// (cmap format 12, glyf, head, hhea, hmtx, loca, maxp).
/// </summary>
internal static class SyntheticFont
{
    public const int UnitsPerEm = 1000;

    /// <summary>A character's glyph: its advance and the rectangle it fills, in font units (y up from the baseline).</summary>
    public readonly record struct Glyph(int Codepoint, int Advance, int X0, int Y0, int X1, int Y1);

    public static byte[] Build(params Glyph[] glyphs)
    {
        // Glyph 0 is .notdef, empty.
        var glyf = new List<byte>();
        var loca = new List<uint> { 0, 0 };
        foreach (var glyph in glyphs)
        {
            glyf.AddRange(Outline(glyph));
            loca.Add((uint)glyf.Count);
        }

        var count = glyphs.Length + 1;
        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["cmap"] = Cmap(glyphs),
            ["glyf"] = [.. glyf],
            ["head"] = Head(glyphs),
            ["hhea"] = Hhea(count, glyphs),
            ["hmtx"] = Hmtx(glyphs),
            ["loca"] = [.. loca.SelectMany(offset => U32(offset))],
            ["maxp"] = [.. U32(0x00005000), .. U16((ushort)count)],
        };

        var font = new List<byte>();
        font.AddRange(U32(0x00010000));
        font.AddRange(U16((ushort)tables.Count));
        font.AddRange(U16(0));
        font.AddRange(U16(0));
        font.AddRange(U16(0));
        var offset = 12 + (16 * tables.Count);
        var data = new List<byte>();
        foreach (var (tag, table) in tables)
        {
            font.AddRange(Encoding.ASCII.GetBytes(tag));
            font.AddRange(U32(0));
            font.AddRange(U32((uint)(offset + data.Count)));
            font.AddRange(U32((uint)table.Length));
            data.AddRange(table);
            while (data.Count % 4 != 0)
            {
                data.Add(0);
            }
        }

        font.AddRange(data);
        return [.. font];
    }

    /// <summary>A rectangle for each character, 700 high: 'M' 600 wide on an advance of 700, others 500 on 600; 'g' goes 200 below the baseline and 500 above.</summary>
    public static byte[] Latin(string characters) => Build([.. characters.EnumerateRunes().Select(rune => rune.Value switch
    {
        'M' => new Glyph('M', 700, 50, 0, 650, 700),
        'g' => new Glyph('g', 600, 50, -200, 550, 500),
        ' ' => new Glyph(' ', 300, 0, 0, 0, 0),
        var code => new Glyph(code, 600, 50, 0, 550, 700),
    })]);

    private static byte[] Outline(Glyph glyph)
    {
        if (glyph.X1 <= glyph.X0 || glyph.Y1 <= glyph.Y0)
        {
            return [];
        }

        var bytes = new List<byte>();
        bytes.AddRange(I16(1));
        bytes.AddRange(I16(glyph.X0));
        bytes.AddRange(I16(glyph.Y0));
        bytes.AddRange(I16(glyph.X1));
        bytes.AddRange(I16(glyph.Y1));
        bytes.AddRange(U16(3));
        bytes.AddRange(U16(0));
        bytes.AddRange([1, 1, 1, 1]);

        // Clockwise: bottom left, top left, top right, bottom right; coordinates as deltas.
        (int X, int Y)[] points = [(glyph.X0, glyph.Y0), (glyph.X0, glyph.Y1), (glyph.X1, glyph.Y1), (glyph.X1, glyph.Y0)];
        var previous = 0;
        foreach (var (x, _) in points)
        {
            bytes.AddRange(I16(x - previous));
            previous = x;
        }

        previous = 0;
        foreach (var (_, y) in points)
        {
            bytes.AddRange(I16(y - previous));
            previous = y;
        }

        while (bytes.Count % 4 != 0)
        {
            bytes.Add(0);
        }

        return [.. bytes];
    }

    private static byte[] Cmap(Glyph[] glyphs)
    {
        var groups = glyphs.Select((glyph, i) => (glyph.Codepoint, Index: i + 1)).OrderBy(group => group.Codepoint).ToList();
        var subtable = new List<byte>();
        subtable.AddRange(U16(12));
        subtable.AddRange(U16(0));
        subtable.AddRange(U32((uint)(16 + (12 * groups.Count))));
        subtable.AddRange(U32(0));
        subtable.AddRange(U32((uint)groups.Count));
        foreach (var (codepoint, index) in groups)
        {
            subtable.AddRange(U32((uint)codepoint));
            subtable.AddRange(U32((uint)codepoint));
            subtable.AddRange(U32((uint)index));
        }

        return [.. U16(0), .. U16(1), .. U16(3), .. U16(10), .. U32(12), .. subtable];
    }

    private static byte[] Head(Glyph[] glyphs)
    {
        var head = new byte[54];
        BinaryPrimitives.WriteUInt32BigEndian(head, 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(12), 0x5F0F3CF5);
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(18), UnitsPerEm);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(36), (short)glyphs.Select(glyph => glyph.X0).DefaultIfEmpty().Min());
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(38), (short)glyphs.Select(glyph => glyph.Y0).DefaultIfEmpty().Min());
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(40), (short)glyphs.Select(glyph => glyph.X1).DefaultIfEmpty().Max());
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(42), (short)glyphs.Select(glyph => glyph.Y1).DefaultIfEmpty().Max());
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), 1);
        return head;
    }

    private static byte[] Hhea(int count, Glyph[] glyphs)
    {
        var hhea = new byte[36];
        BinaryPrimitives.WriteUInt32BigEndian(hhea, 0x00010000);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(4), 800);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(6), -200);
        BinaryPrimitives.WriteUInt16BigEndian(hhea.AsSpan(10), (ushort)glyphs.Select(glyph => glyph.Advance).DefaultIfEmpty().Max());
        BinaryPrimitives.WriteUInt16BigEndian(hhea.AsSpan(34), (ushort)count);
        return hhea;
    }

    private static byte[] Hmtx(Glyph[] glyphs) =>
        [.. U16(0), .. I16(0), .. glyphs.SelectMany(glyph => (byte[])[.. U16((ushort)glyph.Advance), .. I16(glyph.X0)])];

    private static byte[] U16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] I16(int value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(bytes, (short)value);
        return bytes;
    }

    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }
}
