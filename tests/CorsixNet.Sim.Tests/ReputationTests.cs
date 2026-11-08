using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;

namespace CorsixNet.Sim.Tests;

/// <summary>The hospital's reputation, the reputation for each disease, and how they move prices.</summary>
public class ReputationTests
{
    private static readonly DiseaseDefinition Cold = DiseaseCatalog.Find("uncommon_cold")!;
    private static readonly DiseaseDefinition Gp = DiseaseCatalog.Find(DiseaseCatalog.GpDiagnosis)!;

    [Fact]
    public void ChangesNearTheAverageAlwaysCount()
    {
        var hospital = new Hospital(0, 0);
        var random = new SimRandom(1);
        var cold = hospital.CaseOf(Cold, DiseaseRules.Default);
        Assert.Equal(Hospital.AverageReputation, hospital.Reputation);
        Assert.Equal(Hospital.AverageReputation, cold.Reputation);
        Assert.Null(hospital.CaseOf(Gp, DiseaseRules.Default).Reputation);

        hospital.ChangeReputation(random, ReputationChange.Cured, cold);
        Assert.Equal((501, 501), (hospital.Reputation, cold.Reputation));

        hospital.ChangeReputation(random, ReputationChange.Death, cold);
        Assert.Equal((497, 497), (hospital.Reputation, cold.Reputation));

        hospital.UpdateNotCuredCounts(random, ReputationChange.Kicked, cold);
        hospital.UpdateNotCuredCounts(random, ReputationChange.OverPriced, cold);
        Assert.Equal((492, 492), (hospital.Reputation, cold.Reputation));
        Assert.Equal(2, hospital.NotCured);
        Assert.Equal(1, cold.TurnedAway);

        // A change without a disease moves only the hospital's reputation.
        hospital.ChangeReputation(random, ReputationChange.UnderPriced);
        Assert.Equal((493, 492), (hospital.Reputation, cold.Reputation));
    }

    [Fact]
    public void ChangesGetRarerTowardsTheEnds()
    {
        Assert.Equal(0m, new Hospital(0, 0, Hospital.MinReputation).ReputationChangeLikelihood);
        Assert.Equal(0m, new Hospital(0, 0, Hospital.MaxReputation).ReputationChangeLikelihood);
        Assert.Equal(1.002m, new Hospital(0, 0).ReputationChangeLikelihood);
        Assert.Equal(0.7515m, new Hospital(0, 0, 750).ReputationChangeLikelihood);

        // At the top a gain never counts for the hospital, but always for the disease; a loss always counts.
        var top = new Hospital(0, 0, 2000);
        var random = new SimRandom(5);
        var cold = top.CaseOf(Cold, DiseaseRules.Default);
        for (var i = 0; i < 100; i++)
        {
            top.ChangeReputation(random, ReputationChange.Cured, cold);
        }

        Assert.Equal((1000, 600), (top.Reputation, cold.Reputation));
        top.ChangeReputation(random, ReputationChange.Death, cold);
        Assert.Equal(996, top.Reputation);

        // At 750 about three gains in four count.
        var counted = 0;
        for (var i = 0; i < 4000; i++)
        {
            var high = new Hospital(0, 0, 750);
            high.ChangeReputation(random, ReputationChange.Cured);
            counted += high.Reputation - 750;
        }

        Assert.InRange(counted, 2850, 3150);
    }

    [Fact]
    public void AGoodReputationRaisesPricesAndABadOneDoesNotLowerThem()
    {
        var hospital = new Hospital(0, 0);
        var rules = DiseaseRules.Default;
        var cold = hospital.CaseOf(Cold, rules);

        cold.Reputation = 600;
        Assert.Equal(360, hospital.TreatmentPrice(Cold, rules)); // 300 * 600 / 500
        cold.Reputation = 400;
        Assert.Equal(300, hospital.TreatmentPrice(Cold, rules));

        // A diagnosis goes by the hospital's reputation.
        Assert.Equal(118, new Hospital(0, 0, 586).TreatmentPrice(Gp, rules)); // ceil(100 * 586 / 500) = ceil(117.2)
        Assert.Equal(100, new Hospital(0, 0, 100).TreatmentPrice(Gp, rules));
    }

    [Fact]
    public void NowAndThenAPriceFarFromWhatPatientsExpectMovesTheReputation()
    {
        var random = new SimRandom(9);

        // At half price for a cure that always works, patients expect more: 0 − (0.1 + 0.3 + 0.3) < −0.4.
        var cheap = new Hospital(0, 0);
        var gp = cheap.CaseOf(Gp, DiseaseRules.Default);
        gp.PricePercent = 50;
        gp.CureEffectiveness = 100;
        for (var i = 0; i < 2000; i++)
        {
            cheap.ComputePriceLevelImpact(random, gp);
        }

        Assert.InRange(cheap.Reputation, 505, 540);

        // At double price for a cure that never works, patients expect less: 1 − (0.1 + 0.3) > 0.3.
        var dear = new Hospital(0, 0);
        gp = dear.CaseOf(Gp, DiseaseRules.Default);
        gp.PricePercent = 200;
        gp.CureEffectiveness = 0;
        for (var i = 0; i < 2000; i++)
        {
            dear.ComputePriceLevelImpact(random, gp);
        }

        Assert.InRange(dear.Reputation, 420, 490);

        // At the standard price nothing changes.
        var fair = new Hospital(0, 0);
        for (var i = 0; i < 2000; i++)
        {
            fair.ComputePriceLevelImpact(random, fair.CaseOf(Cold, DiseaseRules.Default));
        }

        Assert.Equal(Hospital.AverageReputation, fair.Reputation);
    }

    [Fact]
    public void TheLevelSetsTheStartingReputationAndSavesKeepIt()
    {
        var map = TestMaps.FromRows("....", "....");
        var rules = new LevelRules(1000, [], [], startReputation: 640);
        var original = new Simulation(map, seed: 2, rules: rules);
        var hospital = original.Hospitals[0];
        Assert.Equal(640, hospital.Reputation);

        var cold = hospital.CaseOf(Cold, original.Rules.Diseases);
        hospital.UpdateNotCuredCounts(original.Random, ReputationChange.Kicked, cold);

        using var stream = new MemoryStream();
        original.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);

        Assert.Equal(original.ComputeStateHash(), restored.ComputeStateHash());
        var loaded = restored.Hospitals[0];
        Assert.Equal(640, restored.Rules.StartReputation);
        Assert.Equal((637, 1), (loaded.Reputation, loaded.NotCured));
        Assert.Equal((497, 1), (loaded.Casebook[Cold.Id].Reputation, loaded.Casebook[Cold.Id].TurnedAway));
        Assert.Null(loaded.CaseOf(Gp, restored.Rules.Diseases).Reputation);
    }
}
