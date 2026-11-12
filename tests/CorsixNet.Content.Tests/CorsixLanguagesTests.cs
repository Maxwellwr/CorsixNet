using CorsixNet.Content.Languages;
using CorsixNet.Formats;

namespace CorsixNet.Content.Tests;

public class CorsixLanguagesTests
{
    private const string OriginalStringsFile = """
        Language("original_strings")
        SetSpeechFile("Sound-".. ... ..".dat")
        local S = LoadStrings("Lang-".. ... ..".dat")
        Encoding(utf8)
        local function D(name_str) return S[4][name_str] end
        staff_class = { nurse = S[1][1], doctor = S[1][2] }
        adviser = { warnings = { money_low = S[11][2], money_very_low_take_loan = S[11][3] } }
        vip_names = { health_minister = S[42][1], S[42][2], S[42][3] }
        diseases = { bloaty_head = D(3) }
        menu_file_load = { "Load one", "Load two" }
        menu_file_save = menu_file_load
        """;

    private const string EnglishFile = """
        Font("cp437")
        Language("English", "English", "en", "eng")
        Inherit("original_strings", 0)
        adviser.warnings.money_low = "Your money is getting low!"
        vip_names = { [2] = "Sir Lancelot Spratt" }
        tutorial = { build_reception = "Build a " .. "reception" }
        """;

    private const string FrenchFile = """
        Font("cp437")
        Language(utf8 "Français", "French", "fr", "fre", "fra")
        Inherit("english")
        Inherit("original_strings", 1)
        tutorial.build_reception = "Construisez une réception"
        """;

    private const string RussianFile = """
        Font("mik")
        Language("Русский", "Russian", "ru", "rus")
        Inherit("English")
        Encoding(utf8)
        staff_class = {
          nurse = "Медсестра",
        }
        diseases = { bloaty_head = { name = "Раздутая голова" } }
        """;

    [Fact]
    public void ListsTheLanguagesTheFilesDeclare()
    {
        var languages = Languages();

        Assert.Equal(["English", "French", "Russian"], languages.Languages.Select(l => l.EnglishName));
        Assert.Equal("Français", languages.Find("fra")!.NativeName);
        Assert.True(languages.Find("english")!.UsesGameFont);
        Assert.False(languages.Find("ru")!.UsesGameFont);
        Assert.Null(languages.Find("original_strings"));
        Assert.Null(languages.Find("klingon"));
        Assert.Equal(1, languages.OriginalStringsNumber("French"));
        Assert.Equal(0, languages.OriginalStringsNumber("Russian"));
    }

    [Fact]
    public void EnglishTakesTheOriginalStringsAndItsOwnCorrections()
    {
        var strings = Languages().Load("English", n => n == 0 ? Original("en") : null);

        Assert.Equal("en 1.2", strings.Get("staff_class.doctor"));
        Assert.Equal("Your money is getting low!", strings.Get("adviser.warnings.money_low"));
        Assert.Equal("en 11.3", strings.Get("adviser.warnings.money_very_low_take_loan"));
        Assert.Equal(["en 42.2", "Sir Lancelot Spratt"], strings.List("vip_names"));
        Assert.Equal("Build a reception", strings.Get("tutorial.build_reception"));

        // Tables assigned by name are copied.
        Assert.Equal(["Load one", "Load two"], strings.List("menu_file_save"));

        // The original's S[11][2] is named adviser.warnings.money_low, which English corrects.
        Assert.Equal("adviser.warnings.money_low", strings.OriginalName(11, 2));
        Assert.Equal("Your money is getting low!", strings.Original(11, 2));
        Assert.Null(strings.Original(11, 99));
    }

    [Fact]
    public void ATranslationMergesOverTheLanguagesItInherits()
    {
        var languages = Languages();

        // French inherits English, then the French original strings, which win where they are installed.
        var french = languages.Load("fr", n => Original(n == 1 ? "fr" : "en"));
        Assert.Equal("fr 11.2", french.Get("adviser.warnings.money_low"));
        Assert.Equal("Construisez une réception", french.Get("tutorial.build_reception"));

        // Without them, what English assigned stays.
        var frenchWithoutStrings = languages.Load("fr", n => n == 0 ? Original("en") : null);
        Assert.Equal("Your money is getting low!", frenchWithoutStrings.Get("adviser.warnings.money_low"));

        // Russian merges its tables into English's: the doctor stays.
        var russian = languages.Load("Russian", n => Original("en"));
        Assert.Equal("Медсестра", russian.Get("staff_class.nurse"));
        Assert.Equal("en 1.2", russian.Get("staff_class.doctor"));
        Assert.Equal("Медсестра", russian.Original(1, 1));

        // An unknown language falls back to English.
        Assert.Equal("Build a reception", languages.Load("klingon", _ => null).Get("tutorial.build_reception"));
    }

    [Fact]
    public void OriginalTextsTakeTheTranslation()
    {
        var russian = Languages().Load("Russian", _ => Original("en"));
        var texts = new OriginalTexts(Original("en"), russian);

        Assert.Equal("Медсестра", texts.String(1, 1));
        Assert.Equal("en 1.2", texts.String(1, 2));
        Assert.Equal("Раздутая голова", texts.Disease("bloaty_head")!.Name);
        Assert.Equal("en 44.62", texts.Disease("bloaty_head")!.Cause);
        Assert.Equal("Sir Lancelot Spratt", texts.VipName(2));
        Assert.Equal("Медсестра", texts.Named("staff_class.nurse"));
    }

    [Fact]
    public void ReadsEveryBundledLanguageFile()
    {
        var languages = CorsixLanguages.Load(Path.Combine(AppContext.BaseDirectory, "Languages"));

        Assert.Contains(languages.Languages, l => l.EnglishName == "English");
        Assert.True(languages.Languages.Count >= 20);
        foreach (var language in languages.Languages)
        {
            Assert.NotEmpty(languages.Load(language.EnglishName, _ => Original("en")).Strings);
        }

        // original_strings.lua names the adviser's first tutorial message S[11][...] and English keeps it.
        var english = languages.Load("English", _ => Original("en"));
        Assert.NotNull(english.Get("adviser.tutorial.build_reception"));
        Assert.Equal("Your money is getting low!", english.Get("adviser.warnings.money_low"));

        var russian = languages.Load("Russian", _ => Original("en"));
        Assert.Equal("Новая игра", russian.Get("main_menu.new_game"));
        Assert.Equal(english.OriginalName(11, 2), russian.OriginalName(11, 2));
        Assert.Equal(1, languages.OriginalStringsNumber("French"));
        Assert.False(languages.Find("Russian")!.UsesGameFont);
        Assert.True(languages.Find("German")!.UsesGameFont);
    }

    private static CorsixLanguages Languages() => new(
    [
        ("original_strings.lua", OriginalStringsFile),
        ("english.lua", EnglishFile),
        ("french.lua", FrenchFile),
        ("russian.lua", RussianFile),
    ]);

    /// <summary>Every string is "prefix section.index" (CorsixTH's 1-based numbers), 64 sections of 220.</summary>
    private static StringTable Original(string prefix)
    {
        var data = new List<byte> { 64, 0 };
        for (var section = 0; section < 64; section++)
        {
            data.Add(220);
            data.Add(0);
        }

        for (var section = 1; section <= 64; section++)
        {
            for (var index = 1; index <= 220; index++)
            {
                data.AddRange($"{prefix} {section}.{index}".Select(c => (byte)c));
                data.Add(0);
            }
        }

        return StringTable.Parse(data.ToArray());
    }
}

/// <summary>Staff texts from CorsixTH's translations (humanoid_name_starts/_ends, staff_descriptions, staff_class).</summary>
public class TranslatedStaffTextsTests
{
    [Fact]
    public void StaffAreNamedAndDescribedFromTheTranslation()
    {
        var languages = CorsixLanguages.Load(Path.Combine(AppContext.BaseDirectory, "Languages"));
        var russian = languages.Load("Russian", _ => null);
        var texts = new StaffTexts(StringTable.Parse(new byte[] { 0, 0 }), russian);
        var profile = CorsixNet.Sim.Staff.StaffProfile.Create(CorsixNet.Sim.Staff.StaffClass.Nurse, 900, CorsixNet.Sim.Staff.StaffRules.Default, new CorsixNet.Sim.SimRandom(7));

        Assert.Equal(russian.Get("staff_class.nurse"), texts.Profession(CorsixNet.Sim.Staff.StaffClass.Nurse));
        Assert.Contains(russian.List("humanoid_name_starts"), start => texts.Name(profile).StartsWith(start, StringComparison.Ordinal));
        Assert.Contains(russian.List("staff_descriptions.good"), good => texts.Description(profile).Contains(good, StringComparison.Ordinal));
    }

    [Fact]
    public void WholeSectionsAndRemovedItemsAreFollowed()
    {
        var languages = new CorsixLanguages(
        [
            ("original_strings.lua", """
                Language("original_strings")
                local S = LoadStrings("Lang-".. ... ..".dat")
                humanoid_name_starts = S[9]
                staff_descriptions.misc = S[46]
                staff_descriptions.misc[3] = nil
                """),
            ("english.lua", """
                Language("English", "English", "en")
                Inherit("original_strings", 0)
                """),
        ]);
        var strings = languages.Load("English", _ => Table());

        Assert.Equal(["a", "b", "c"], strings.List("humanoid_name_starts"));
        Assert.Equal(["x", "y"], strings.List("staff_descriptions.misc"));
    }

    private static StringTable Table()
    {
        var sections = new string[48][];
        for (var i = 0; i < sections.Length; i++)
        {
            sections[i] = [];
        }

        sections[8] = ["a", "b", "c"];
        sections[45] = ["x", "y", "."];
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
}
