using OpenWards.Sim.Economy;
using OpenWards.Sim.Serialization;
using OpenWards.Sim.Staff;

namespace OpenWards.Sim.Tests;

public sealed class ImportedStaffTests
{
    [Fact]
    public void ImportedProfileKeepsItsValuesAndNameThroughSaving()
    {
        var name = new ImportedStaffName('T', 16, 15, 23, 13);
        var profile = StaffProfile.Import(
            StaffClass.Doctor,
            skill: 565,
            attentionToDetail: 700,
            psychiatrist: false,
            surgeon: true,
            researcher: false,
            wage: 161,
            layer5: 4,
            [0m, 0.18m, 0.23m],
            name,
            LevelRules.Free.Staff,
            new SimRandom(1));

        using var stream = new MemoryStream();
        profile.Write(new SimWriter(stream));
        stream.Position = 0;
        var read = StaffProfile.Read(new SimReader(stream));

        Assert.Equal(name, read.ImportedName);
        Assert.Equal((565, 700, 161, (byte)4), (read.Skill, read.AttentionToDetail, read.Wage, read.Layer5));
        Assert.True(read.IsSurgeon);
        Assert.Equal(0.18m, read.SpecialismProgress(Specialism.Psychiatrist));
        Assert.Equal(0.23m, read.SpecialismProgress(Specialism.Researcher));
    }
}
