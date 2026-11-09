using CorsixNet.Formats;
using CorsixNet.Formats.Tests;
using CorsixNet.Sim.Diseases;

namespace CorsixNet.Content.Tests;

public class OriginalTextsTests
{
    [Fact]
    public void NamesDiseasesAndGivesTheirCauseSymptomsAndCure()
    {
        var texts = new OriginalTexts(Strings());

        Assert.Equal(new DiseaseText("Name 3", "Fax 62", "Fax 63", "Fax 64"), texts.Disease("bloaty_head"));
        Assert.Equal(new DiseaseText("Name 15", "Fax 98", "Fax 99", "Fax 100"), texts.Disease("pregnant"));
        Assert.Equal(new DiseaseText("Name 2", null, null, null), texts.Disease("diag_gp"));
        Assert.Equal("Name 42", texts.DiseaseName("diag_general_diag"));
        Assert.Null(texts.Disease("no_such_disease"));
        Assert.Equal("no_such_disease", texts.DiseaseName("no_such_disease"));
    }

    [Fact]
    public void NamesEveryDiseaseOfTheCatalog()
    {
        var texts = new OriginalTexts(Strings());

        foreach (var disease in DiseaseCatalog.All)
        {
            Assert.NotNull(texts.Disease(disease.Id));
            Assert.Equal(disease.IsPseudo, texts.Disease(disease.Id)!.Cause is null);
        }
    }

    [Fact]
    public void NamesTheVipsAfterTheHealthMinister()
    {
        var texts = new OriginalTexts(Strings());

        Assert.Equal("VIP 2", texts.VipName(1));
        Assert.Equal("VIP 11", texts.VipName(10));
        Assert.Null(texts.VipName(0));
        Assert.Null(texts.VipName(11));
    }

    [Fact]
    public void NamesDiseasesInTheInstalledLanguage()
    {
        var install = RealGameData.Require();
        var texts = new OriginalTexts(install.LoadStrings(install.InstalledLanguages()[0]));

        foreach (var disease in DiseaseCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(texts.Disease(disease.Id)?.Name), disease.Id);
        }

        Assert.False(string.IsNullOrWhiteSpace(texts.VipName(1)));
    }

    /// <summary>Section 4 holds "Name n", section 42 "VIP n" and section 44 "Fax n", with CorsixTH's 1-based n.</summary>
    private static StringTable Strings()
    {
        var sections = new string[48][];
        for (var i = 0; i < sections.Length; i++)
        {
            sections[i] = [];
        }

        sections[3] = [.. Enumerable.Range(1, 46).Select(n => $"Name {n}")];
        sections[41] = [.. Enumerable.Range(1, 11).Select(n => $"VIP {n}")];
        sections[43] = [.. Enumerable.Range(1, 218).Select(n => $"Fax {n}")];

        var data = new List<byte> { (byte)sections.Length, 0 };
        foreach (var section in sections)
        {
            data.Add((byte)section.Length);
            data.Add((byte)(section.Length >> 8));
        }

        foreach (var text in sections.SelectMany(section => section))
        {
            data.AddRange(text.Select(c => (byte)c));
            data.Add(0);
        }

        return StringTable.Parse(data.ToArray());
    }
}
