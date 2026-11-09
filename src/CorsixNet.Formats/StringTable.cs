// Reader for the original game's string files (DATA/LANG-n.DAT), ported from CorsixTH CorsixTH/Src/th.cpp
// (th_string_list, detect_encoding, CopyStringCP437, CopyStringCP936, CopyStringMIK), CorsixTH/Src/cpmik_table.h and
// the German fixes of CorsixTH/Lua/languages/original_strings.lua (fixGermanStrings).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Buffers.Binary;
using System.Text;

namespace CorsixNet.Formats;

/// <summary>The character encoding of a string file, guessed from its contents as CorsixTH does.</summary>
public enum StringEncoding
{
    /// <summary>Code page 437: the English, French, German, Italian, Spanish and Swedish versions.</summary>
    Cp437,

    /// <summary>Code page 936 (GBK): the Chinese version.</summary>
    Cp936,

    /// <summary>MIK: the Russian version.</summary>
    Mik,
}

/// <summary>
/// The strings of one of the original game's language files, by section and index. The format: a 16-bit section
/// count, a 16-bit string count for each section, then the strings, each ending in a zero byte. Indices here start at
/// 0, so CorsixTH's <c>S[a][b]</c> is <c>this[a - 1, b - 1]</c>.
/// </summary>
public sealed class StringTable
{
    /// <summary>The language numbers of CorsixTH's languages that use the original strings (<c>Inherit("original_strings", n)</c>).</summary>
    public const int English = 0;

    public const int French = 1;
    public const int German = 2;
    public const int Italian = 3;
    public const int Spanish = 4;
    public const int Swedish = 5;
    public const int Japanese = 6;

    // MIK and CP437 agree on 0xE0-0xFF; 0x80-0xDF are Cyrillic letters and box drawing.
    private static readonly ushort[] MikToUnicode =
    [
        0x0410, 0x0411, 0x0412, 0x0413, 0x0414, 0x0415, 0x0416, 0x0417,
        0x0418, 0x0419, 0x041A, 0x041B, 0x041C, 0x041D, 0x041E, 0x041F,
        0x0420, 0x0421, 0x0422, 0x0423, 0x0424, 0x0425, 0x0426, 0x0427,
        0x0428, 0x0429, 0x042A, 0x042B, 0x042C, 0x042D, 0x042E, 0x042F,
        0x0430, 0x0431, 0x0432, 0x0433, 0x0434, 0x0435, 0x0436, 0x0437,
        0x0438, 0x0439, 0x043A, 0x043B, 0x043C, 0x043D, 0x043E, 0x043F,
        0x0440, 0x0441, 0x0442, 0x0443, 0x0444, 0x0445, 0x0446, 0x0447,
        0x0448, 0x0449, 0x044A, 0x044B, 0x044C, 0x044D, 0x044E, 0x044F,
        0x2514, 0x2534, 0x252C, 0x251C, 0x2500, 0x253C, 0x2563, 0x2551,
        0x255A, 0x2554, 0x2569, 0x2566, 0x2560, 0x2550, 0x256C, 0x2510,
        0x2591, 0x2592, 0x2593, 0x2502, 0x2524, 0x2116, 0x00A7, 0x2557,
        0x255D, 0x2518, 0x250C, 0x2588, 0x2584, 0x258C, 0x2590, 0x2580,
        0x03B1, 0x00DF, 0x0393, 0x03C0, 0x03A3, 0x03C3, 0x00B5, 0x03C4,
        0x03A6, 0x0398, 0x03A9, 0x03B4, 0x221E, 0x03C6, 0x03B5, 0x2229,
        0x2261, 0x00B1, 0x2265, 0x2264, 0x2320, 0x2321, 0x00F7, 0x2248,
        0x00B0, 0x2219, 0x00B7, 0x221A, 0x207F, 0x00B2, 0x25A0, 0x00A0,
    ];

    private static readonly byte[] MikPrefix = [0x8C, 0xA5, 0xA4, 0xB1, 0xA5];

    // CorsixTH's German fixes: the eszett of the old spelling. No word contains another, so the order does not matter.
    private static readonly (string From, string To)[] GermanSpelling =
    [
        ("daß", "dass"),
        ("muß", "muss"),
        ("läßt", "lässt"),
        ("Engpaß", "Engpass"),
        ("bißchen", "bisschen"),
        ("Streß", "Stress"),
        ("Biß", "Biss"),
        ("Freßpaket", "Fresspaket"),
        ("Paßt", "Passt"),
        ("Läßt", "Lässt"),
        ("verantwortungsbewußt", "verantwortungsbewusst"),
        ("verläßlich", "verlässlich"),
        ("Schlußlicht", "Schlusslicht"),
        ("unerläßlich", "unerlässlich"),
    ];

    private readonly List<string>[] _sections;

    private StringTable(List<string>[] sections, StringEncoding encoding)
    {
        _sections = sections;
        Encoding = encoding;
    }

    /// <summary>The encoding the strings were read in.</summary>
    public StringEncoding Encoding { get; }

    /// <summary>The number of sections.</summary>
    public int SectionCount => _sections.Length;

    /// <summary>The strings of a section.</summary>
    public IReadOnlyList<string> this[int section] => _sections[section];

    /// <summary>A string by its section and index.</summary>
    public string this[int section, int index] => _sections[section][index];

    /// <summary>The number of strings in a section, or 0 if there is no such section.</summary>
    public int SectionSize(int section) => section >= 0 && section < _sections.Length ? _sections[section].Count : 0;

    /// <summary>A string, or null if there is no such section or index (CorsixTH's <c>get_string</c>).</summary>
    public string? Get(int section, int index) => index >= 0 && index < SectionSize(section) ? _sections[section][index] : null;

    /// <summary>
    /// Reads a string file. Strings past the end of the data are empty, as in CorsixTH.
    /// </summary>
    /// <exception cref="InvalidDataException">The data is shorter than its header.</exception>
    public static StringTable Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            throw new InvalidDataException("A string file needs at least two bytes.");
        }

        var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var headerLength = (sectionCount + 1) * 2;
        if (data.Length < headerLength)
        {
            throw new InvalidDataException($"A string file with {sectionCount} sections needs a header of {headerLength} bytes.");
        }

        var strings = data[headerLength..];
        var encoding = DetectEncoding(strings);
        var sections = new List<string>[sectionCount];
        var position = 0;
        var builder = new StringBuilder();
        for (var i = 0; i < sectionCount; i++)
        {
            var size = BinaryPrimitives.ReadUInt16LittleEndian(data[((i + 1) * 2)..]);
            var section = new List<string>(size);
            for (var j = 0; j < size; j++)
            {
                builder.Clear();
                if (position < strings.Length)
                {
                    position = encoding switch
                    {
                        StringEncoding.Cp936 => ReadCp936(strings, position, builder),
                        _ => ReadSingleByte(strings, position, builder, encoding == StringEncoding.Mik ? MikToUnicode : null),
                    };
                }

                section.Add(builder.ToString());
            }

            sections[i] = section;
        }

        return new StringTable(sections, encoding);
    }

    /// <summary>
    /// CorsixTH's <c>detect_encoding</c>: the Russian version starts with a known word in MIK; otherwise, if a tenth or
    /// more of the bytes are in 0xB0-0xDF (box drawing in code page 437, common in GB2312), the text is code page 936.
    /// </summary>
    public static StringEncoding DetectEncoding(ReadOnlySpan<byte> strings)
    {
        if (strings.StartsWith(MikPrefix))
        {
            return StringEncoding.Mik;
        }

        var boxDrawing = 0;
        foreach (var b in strings)
        {
            if (b is >= 0xB0 and <= 0xDF)
            {
                boxDrawing++;
            }
        }

        return boxDrawing * 10 >= strings.Length ? StringEncoding.Cp936 : StringEncoding.Cp437;
    }

    /// <summary>
    /// Fixes the German version's strings as CorsixTH's <c>fixGermanStrings</c> does: splits a joined string in section
    /// 44 and shifts the rest, removes a duplicate in section 28, applies the spelling reform, and adds the space
    /// missing at the end of the good and bad staff descriptions (sections 47 and 48).
    /// </summary>
    public void FixGerman()
    {
        // S[44][168] holds S[44][168] and S[44][169] of the other languages in the full version. Lengths and the split
        // are in UTF-8 bytes, as Lua's.
        if (SectionSize(43) >= 218 && System.Text.Encoding.UTF8.GetByteCount(_sections[43][167]) == 108)
        {
            var section = _sections[43];
            if (section.Count == 218)
            {
                section.Add(string.Empty);
            }

            for (var i = 217; i >= 168; i--)
            {
                section[i + 1] = section[i];
            }

            var joined = System.Text.Encoding.UTF8.GetBytes(section[167]);
            section[168] = System.Text.Encoding.UTF8.GetString(joined, 45, joined.Length - 45);
            section[167] = System.Text.Encoding.UTF8.GetString(joined, 0, 45);
        }

        // S[28][32] and S[28][33] are equal in the full version.
        if (SectionSize(27) == 64 && _sections[27][31] == _sections[27][32])
        {
            for (var i = 32; i < 63; i++)
            {
                _sections[27][i] = _sections[27][i + 1];
            }
        }

        foreach (var section in _sections)
        {
            for (var i = 0; i < section.Count; i++)
            {
                var text = section[i];
                foreach (var (from, to) in GermanSpelling)
                {
                    text = text.Replace(from, to, StringComparison.Ordinal);
                }

                section[i] = text;
            }
        }

        foreach (var index in (ReadOnlySpan<int>)[46, 47])
        {
            if (index < _sections.Length)
            {
                var section = _sections[index];
                for (var i = 0; i < section.Count; i++)
                {
                    section[i] += " ";
                }
            }
        }
    }

    private static int ReadSingleByte(ReadOnlySpan<byte> data, int position, StringBuilder builder, ushort[]? high)
    {
        while (position < data.Length)
        {
            var b = data[position++];
            if (b == 0)
            {
                break;
            }

            builder.Append((char)(b < 0x80 ? b : high is null ? Cp437.ToUnicode(b) : high[b - 0x80]));
        }

        return position;
    }

    private static int ReadCp936(ReadOnlySpan<byte> data, int position, StringBuilder builder)
    {
        var gbk = CodePagesEncodingProvider.Instance.GetEncoding(936)
            ?? throw new InvalidOperationException("Code page 936 is not available.");
        Span<char> decoded = stackalloc char[2];
        while (position < data.Length)
        {
            var first = data[position++];
            if (first == 0)
            {
                break;
            }

            if (first < 0x81 || first == 0xFF || position >= data.Length)
            {
                builder.Append((char)first);
                continue;
            }

            var second = data[position++];
            if (second is >= 0x40 and <= 0xFE)
            {
                var count = gbk.GetChars([first, second], decoded);
                builder.Append(count == 1 ? decoded[0] : '?');

                // The string tables seem to follow a multibyte character with a superfluous space.
                if (position < data.Length && data[position] == ' ')
                {
                    position++;
                }
            }
            else if (second == 0)
            {
                builder.Append((char)first);
                break;
            }
            else
            {
                builder.Append((char)first).Append((char)second);
            }
        }

        return position;
    }
}
