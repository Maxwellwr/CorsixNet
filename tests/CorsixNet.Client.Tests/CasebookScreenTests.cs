using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim.Diseases;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class CasebookScreenTests
{
    [Fact]
    public void TheCureKindFollowsTheTreatmentRooms()
    {
        Assert.Equal(7, CasebookScreen.CureSprite(new DiseaseDefinition("a", 1, 1) { TreatmentRooms = ["pharmacy"] }));
        Assert.Equal(8, CasebookScreen.CureSprite(new DiseaseDefinition("b", 1, 1) { TreatmentRooms = ["ward", "operating_theatre"] }));
        Assert.Equal(10, CasebookScreen.CureSprite(new DiseaseDefinition("c", 1, 1) { TreatmentRooms = ["psych"] }));
        Assert.Equal(6, CasebookScreen.CureSprite(new DiseaseDefinition("d", 1, 1) { TreatmentRooms = ["inflation"] }));
    }

    [Fact]
    public void TheListHasDiagnosesFirstThenDiseasesByName()
    {
        var host = new WindowTestHost();
        var known = CasebookScreen.Known(host);

        Assert.NotEmpty(known);
        var expected = known.OrderByDescending(disease => disease.IsPseudo).ThenBy(disease => disease.Id.ToUpperInvariant(), StringComparer.Ordinal);
        Assert.Equal(expected.Select(disease => disease.Id), known.Select(disease => disease.Id));
        Assert.False(new CasebookWindow().IsFullscreen(host));
    }
}
