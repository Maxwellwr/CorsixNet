using System.Text;

namespace CorsixNet.Formats.Tests;

public class StringTableTests
{
    [Fact]
    public void ReadsSectionsOfZeroTerminatedStrings()
    {
        var table = StringTable.Parse(Build([["Nurse", "Doctor"], [], ["Café"]]));

        Assert.Equal(StringEncoding.Cp437, table.Encoding);
        Assert.Equal(3, table.SectionCount);
        Assert.Equal(["Nurse", "Doctor"], table[0]);
        Assert.Empty(table[1]);
        Assert.Equal("Café", table[2, 0]);
        Assert.Equal(0, table.SectionSize(3));
        Assert.Null(table.Get(0, 2));
        Assert.Null(table.Get(5, 0));
        Assert.Equal("Doctor", table.Get(0, 1));
    }

    [Fact]
    public void StringsPastTheEndOfTheDataAreEmpty()
    {
        byte[] data = [2, 0, 3, 0, 1, 0, (byte)'A', 0, (byte)'B'];

        var table = StringTable.Parse(data);

        Assert.Equal(["A", "B", ""], table[0]);
        Assert.Equal([""], table[1]);
    }

    [Fact]
    public void RejectsDataShorterThanItsHeader()
    {
        Assert.Throws<InvalidDataException>(() => StringTable.Parse([1]));
        Assert.Throws<InvalidDataException>(() => StringTable.Parse([3, 0, 1, 0]));
    }

    [Fact]
    public void ReadsTheRussianVersionInMik()
    {
        // "Медсестра" (nurse) in MIK; the Russian version's file starts with "Медсе".
        byte[] word = [0x8C, 0xA5, 0xA4, 0xB1, 0xA5, 0xB1, 0xB2, 0xB0, 0xA0];
        var data = Header([1]).Concat(word).Append((byte)0).ToArray();

        var table = StringTable.Parse(data);

        Assert.Equal(StringEncoding.Mik, table.Encoding);
        Assert.Equal("Медсестра", table[0, 0]);
    }

    [Fact]
    public void ReadsTheChineseVersionInCodePage936AndDropsTheSpaceAfterEachCharacter()
    {
        // 护士 (nurse) in GBK is BB A4 CA BF; the string tables put a space after each double-byte character.
        byte[] text = [0xBB, 0xA4, (byte)' ', 0xCA, 0xBF, (byte)' ', (byte)'!', 0];
        var data = Header([1]).Concat(text).ToArray();

        var table = StringTable.Parse(data);

        Assert.Equal(StringEncoding.Cp936, table.Encoding);
        Assert.Equal("护士!", table[0, 0]);
    }

    [Fact]
    public void DetectsCodePage936ByTheShareOfBoxDrawingBytes()
    {
        Assert.Equal(StringEncoding.Cp936, StringTable.DetectEncoding([0xB0, 1, 2, 3, 4, 5, 6, 7, 8, 9]));
        Assert.Equal(StringEncoding.Cp437, StringTable.DetectEncoding([0xB0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10]));
    }

    [Fact]
    public void FixesTheGermanVersion()
    {
        var sections = new List<string>[48];
        for (var i = 0; i < sections.Length; i++)
        {
            sections[i] = ["Er muß gehen"];
        }

        // Section 28: strings 32 and 33 are equal and the rest is one off.
        sections[27] = [.. Enumerable.Range(1, 64).Select(i => i <= 32 ? $"s{i}" : $"s{i - 1}")];

        // Section 44: string 168 holds 168 and 169; 108 bytes in UTF-8 with the umlaut.
        var joined = "Ä" + new string('a', 43) + new string('b', 63);
        Assert.Equal(108, Encoding.UTF8.GetByteCount(joined));
        sections[43] = [.. Enumerable.Range(1, 218).Select(i => i == 168 ? joined : $"t{i}")];

        var table = StringTable.Parse(Build(sections));
        table.FixGerman();

        Assert.Equal("s32", table[27, 31]);
        Assert.Equal("s33", table[27, 32]);
        Assert.Equal("s63", table[27, 62]);
        Assert.Equal("s63", table[27, 63]);
        Assert.Equal(219, table.SectionSize(43));
        Assert.Equal("Ä" + new string('a', 43), table[43, 167]);
        Assert.Equal(new string('b', 63), table[43, 168]);
        Assert.Equal("t169", table[43, 169]);
        Assert.Equal("t218", table[43, 218]);
        Assert.Equal("Er muss gehen", table[0, 0]);
        Assert.Equal("Er muss gehen ", table[46, 0]);
        Assert.Equal("Er muss gehen ", table[47, 0]);
    }

    [Fact]
    public void ReadsTheInstalledLanguage()
    {
        var install = RealGameData.Require();
        var languages = install.InstalledLanguages();
        Assert.NotEmpty(languages);

        var table = install.LoadStrings(languages[0]);

        Assert.True(table.SectionCount >= 48, $"{table.SectionCount} sections");
        for (var i = 0; i < 4; i++)
        {
            Assert.False(string.IsNullOrWhiteSpace(table[0, i]), $"Staff class {i + 1} is empty.");
        }
    }

    private static byte[] Header(int[] sizes)
    {
        var header = new List<byte> { (byte)sizes.Length, (byte)(sizes.Length >> 8) };
        foreach (var size in sizes)
        {
            header.Add((byte)size);
            header.Add((byte)(size >> 8));
        }

        return [.. header];
    }

    private static byte[] Build(IReadOnlyList<IReadOnlyList<string>> sections)
    {
        var data = new List<byte>(Header([.. sections.Select(section => section.Count)]));
        foreach (var section in sections)
        {
            foreach (var text in section)
            {
                foreach (var c in text)
                {
                    data.Add((byte)Cp437.FromUnicode(c));
                }

                data.Add(0);
            }
        }

        return [.. data];
    }
}
