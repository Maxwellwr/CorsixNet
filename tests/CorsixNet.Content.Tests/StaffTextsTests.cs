using CorsixNet.Formats;
using CorsixNet.Formats.Tests;
using CorsixNet.Sim;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Content.Tests;

public class StaffTextsTests
{
    [Fact]
    public void NamesStaffFromTheNameStartsAndEnds()
    {
        var texts = new StaffTexts(Strings());
        var names = new SortedSet<string>(StringComparer.Ordinal);

        for (var seed = 1; seed <= 20; seed++)
        {
            var profile = StaffProfile.Create(StaffClass.Doctor, 500, StaffRules.Default, new SimRandom((ulong)seed));
            var name = texts.Name(profile);
            names.Add(name);

            Assert.Contains(name[..2], new[] { "Ab", "Cd" });
            Assert.Contains(name[2..], new[] { "ef", "gh", "ij" });
            Assert.Equal(name, texts.Name(profile));
            Assert.Equal($"{texts.Initial(profile)}. {name}", texts.FullName(profile));
        }

        Assert.True(names.Count > 3, string.Join(", ", names));
    }

    [Fact]
    public void TakesTheInitialFromTheNamesLessTheirFirst32Bytes()
    {
        // The starts and ends joined are 32 filler bytes, then "XYZ".
        var texts = new StaffTexts(Strings(starts: [new string('a', 30), "bb"], ends: ["XYZ"]));

        // CorsixTH: initials[name_seed % 3], where initials[0] is the last one.
        Assert.Equal("Z", texts.Initial(WithNameSeed(3)));
        Assert.Equal("X", texts.Initial(WithNameSeed(4)));
        Assert.Equal("Y", texts.Initial(WithNameSeed(5)));
    }

    [Fact]
    public void DescribesSkilledStaffWellAndOthersBadly()
    {
        var texts = new StaffTexts(Strings());

        for (var seed = 1; seed <= 20; seed++)
        {
            var good = texts.Description(StaffProfile.Create(StaffClass.Nurse, 550, StaffRules.Default, new SimRandom((ulong)seed)));
            var bad = texts.Description(StaffProfile.Create(StaffClass.Nurse, 549, StaffRules.Default, new SimRandom((ulong)seed)));

            Assert.Contains("Good. ", good);
            Assert.DoesNotContain("Bad. ", good);
            Assert.Contains("Bad. ", bad);
            Assert.Contains("Misc. ", good);
            Assert.DoesNotContain("End.", good + bad);
        }
    }

    [Fact]
    public void LeavesOutTheMiscellaneousPartWhenTheDescriptionIsTooLong()
    {
        var texts = new StaffTexts(Strings(good: [new string('g', 60)], misc: [new string('m', 37)]));

        var description = texts.Description(StaffProfile.Create(StaffClass.Doctor, 900, StaffRules.Default, new SimRandom(1)));

        Assert.Equal(new string('g', 60), description);
    }

    [Fact]
    public void NamesTheProfessions()
    {
        var texts = new StaffTexts(Strings());

        Assert.Equal("Nurse", texts.Profession(StaffClass.Nurse));
        Assert.Equal("Doctor", texts.Profession(StaffClass.Doctor));
        Assert.Equal("Handyman", texts.Profession(StaffClass.Handyman));
        Assert.Equal("Receptionist", texts.Profession(StaffClass.Receptionist));
    }

    [Fact]
    public void NamesStaffInTheInstalledLanguage()
    {
        var install = RealGameData.Require();
        var texts = new StaffTexts(install.LoadStrings(install.InstalledLanguages()[0]));

        var profile = StaffProfile.Create(StaffClass.Doctor, 700, StaffRules.Default, new SimRandom(7));

        Assert.False(string.IsNullOrWhiteSpace(texts.Name(profile)));
        Assert.False(string.IsNullOrWhiteSpace(texts.Description(profile)));
        Assert.NotEqual("?", texts.Initial(profile));
    }

    private static StaffProfile WithNameSeed(int nameSeed)
    {
        for (var seed = 1UL; ; seed++)
        {
            var profile = StaffProfile.Create(StaffClass.Doctor, 500, StaffRules.Default, new SimRandom(seed));
            if (profile.NameSeed % 3 == nameSeed % 3)
            {
                return profile;
            }
        }
    }

    private static StringTable Strings(string[]? starts = null, string[]? ends = null, string[]? good = null, string[]? misc = null)
    {
        var sections = new string[48][];
        for (var i = 0; i < sections.Length; i++)
        {
            sections[i] = [];
        }

        sections[0] = ["Nurse", "Doctor", "Handyman", "Receptionist", "Surgeon", ""];
        sections[8] = starts ?? ["Ab", "Cd"];
        sections[9] = ends ?? ["ef", "gh", "ij"];

        // Each list ends in a "." that CorsixTH removes; pad them to their full lengths.
        sections[45] = [.. Pad(misc ?? ["Misc. "], 37), "End."];
        sections[46] = [.. Pad(good ?? ["Good. "], 15), "End."];
        sections[47] = [.. Pad(["Bad. "], 15), "End."];

        var data = new List<byte> { (byte)sections.Length, 0 };
        foreach (var section in sections)
        {
            data.Add((byte)section.Length);
            data.Add(0);
        }

        foreach (var text in sections.SelectMany(section => section))
        {
            data.AddRange(text.Select(c => (byte)c));
            data.Add(0);
        }

        return StringTable.Parse(data.ToArray());
    }

    private static IEnumerable<string> Pad(string[] strings, int count) => Enumerable.Range(0, count).Select(i => strings[i % strings.Length]);
}
