using CorsixNet.Sim.Economy;

namespace CorsixNet.Sim.Tests;

/// <summary>The hospital's value, the year's counts and the trophies and awards of the year end.</summary>
public class YearEndTests
{
    [Fact]
    public void TheValueIsTheLandAndWhatWasPaidForRoomsAndObjects()
    {
        var map = TestMaps.FromRows("....", "....", "....");
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        for (var x = 0; x < map.Width; x++)
        {
            map[x, 1].Parcel = 1;
            map[x, 2].Parcel = 1;
        }

        var simulation = new Simulation(map, seed: 1, rules: new LevelRules(10_000, [], [], landCostPerTile: 30));
        var hospital = simulation.Hospitals[0];
        Assert.Equal((8 * 30) + 20_000, hospital.Value);

        hospital.Spend(1, 2500, TransactionKind.BuildRoom, "gp");
        hospital.Spend(1, 300, TransactionKind.BuyObject, "desk");
        hospital.Spend(1, 1000, TransactionKind.Wages, string.Empty);
        hospital.Receive(1, 300, TransactionKind.SellObject, "desk");
        hospital.Receive(1, 100, TransactionKind.Treatment, "diag_gp");
        Assert.Equal(20_240 + 2500, hospital.Value);
    }

    [Fact]
    public void AGoodYearWinsTrophiesAndAwards()
    {
        var hospital = new Hospital(0, 0, reputation: 600, value: 200_000);
        hospital.CuredThisYear = 60;
        hospital.VisitorsThisYear = 70;

        hospital.OnEndYear(100, new SimRandom(4), AwardRules.Default);

        var kinds = hospital.LastYearAwards.Select(a => a.Kind).ToList();
        Assert.Equal(
            [
                YearEndAwardKind.ConsistentReputation, YearEndAwardKind.AllCuredTrophy, YearEndAwardKind.HighReputation,
                YearEndAwardKind.HighValue, YearEndAwardKind.ManyCures, YearEndAwardKind.AllCures, YearEndAwardKind.CuresOverDeaths,
            ],
            kinds);
        var valueAward = hospital.LastYearAwards.Single(a => a.Kind == YearEndAwardKind.HighValue);
        Assert.Equal(0, valueAward.Reputation % 3);
        Assert.InRange(valueAward.Reputation, 3, 45);

        // Trophy money and award money are paid separately; the reputation rises by the value award.
        Assert.Equal(new Transaction(100, TransactionKind.TrophyBonus, string.Empty, 2000 + 20_000, 22_000), hospital.Transactions[1]);
        Assert.Equal(new Transaction(100, TransactionKind.AwardBonus, string.Empty, 2000 + 2000 + 5000 + 4000, 35_000), hospital.Transactions[0]);
        Assert.InRange(hospital.Reputation, 600, 645);

        // The year's counts start afresh and prices rise.
        Assert.Equal((0, 0, 0, 0), (hospital.CuredThisYear, hospital.DeathsThisYear, hospital.NotCuredThisYear, hospital.VisitorsThisYear));
        Assert.Equal(1, hospital.InflationYears);
    }

    [Fact]
    public void ABadYearIsFined()
    {
        var hospital = new Hospital(0, 0, reputation: 150, value: 10_000);
        hospital.CuredThisYear = 1;
        hospital.DeathsThisYear = 30;

        hospital.OnEndYear(100, new SimRandom(4), AwardRules.Default);

        Assert.Equal(
            [
                YearEndAwardKind.PoorReputation, YearEndAwardKind.LowValue, YearEndAwardKind.FewCures,
                YearEndAwardKind.ManyDeaths, YearEndAwardKind.DeathsOverCures,
            ],
            hospital.LastYearAwards.Select(a => a.Kind));
        Assert.Equal(new Transaction(100, TransactionKind.AwardBonus, string.Empty, -3000 - 3000 - 5000 - 4000, -15_000), Assert.Single(hospital.Transactions));
        // The value penalty lowers the reputation, though so far below 500 a loss counts only now and then.
        Assert.InRange(hospital.LastYearAwards.Single(a => a.Kind == YearEndAwardKind.LowValue).Reputation, -90, -6);
        Assert.InRange(hospital.Reputation, 60, 150);
    }

    [Fact]
    public void OneCureIsNotEnoughForATrophy()
    {
        var hospital = new Hospital(0, 0, reputation: 600, value: 200_000);
        hospital.CuredThisYear = 1;

        hospital.OnEndYear(100, new SimRandom(4), AwardRules.Default);

        // CorsixTH needs more than one cure for anything but the fines; with one cure there are fewer than ten.
        Assert.Equal([YearEndAwardKind.FewCures], hospital.LastYearAwards.Select(a => a.Kind));
    }

    [Fact]
    public void ADipInReputationLosesTheConsistentReputationTrophy()
    {
        var random = new SimRandom(4);
        var prices = AwardRules.With([new(AwardRules.Reputation, 500)]);
        var hospital = new Hospital(0, 0, reputation: 504, value: 100_000);
        hospital.ChangeReputation(random, ReputationChange.Death);
        hospital.ChangeReputation(random, ReputationChange.Cured);
        Assert.Equal((501, 500), (hospital.Reputation, hospital.LowestReputationThisYear));
        hospital.CuredThisYear = 20;
        hospital.NotCuredThisYear = 1;

        hospital.OnEndYear(100, random, prices);

        Assert.Equal(
            [YearEndAwardKind.NoDeathsTrophy, YearEndAwardKind.HighReputation, YearEndAwardKind.FewDeaths, YearEndAwardKind.CuresOverDeaths],
            hospital.LastYearAwards.Select(a => a.Kind));
        Assert.Equal(hospital.Reputation, hospital.LowestReputationThisYear);
    }

    [Fact]
    public void TheLevelSetsThePrizes()
    {
        var prices = AwardRules.With([new(AwardRules.CuresPoor, 0), new(AwardRules.HospValuePoor, 0), new("NotASetting", 1)]);
        var hospital = new Hospital(0, 0, value: 20_000);
        hospital.DeathsThisYear = 1;

        hospital.OnEndYear(100, new SimRandom(4), prices);

        // Without the fines for few cures and a low value only the one for no cures per death is left.
        Assert.Equal([YearEndAwardKind.DeathsOverCures], hospital.LastYearAwards.Select(a => a.Kind));
        Assert.Equal(150_000, prices[AwardRules.HospValueAward]);
    }

    [Fact]
    public void TheSimulationJudgesTheYearAtItsEndAndSavesKeepIt()
    {
        var map = TestMaps.FromRows("....", "....");
        var original = new Simulation(map, seed: 7, rules: new LevelRules(1000, [], []));
        var hospital = original.Hospitals[0];
        while (original.Date.Year == 1)
        {
            original.Step();
        }

        // An empty hospital worth 20,000 cures nobody and is worth too little.
        Assert.Equal([YearEndAwardKind.LowValue, YearEndAwardKind.FewCures], hospital.LastYearAwards.Select(a => a.Kind));
        Assert.Equal(1, hospital.InflationYears);
        Assert.Contains(hospital.Transactions, t => t is { Kind: TransactionKind.AwardBonus, Amount: -3000 });

        using var stream = new MemoryStream();
        original.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(original.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(hospital.LastYearAwards, restored.Hospitals[0].LastYearAwards);
        Assert.Equal(20_000, restored.Hospitals[0].Value);
        Assert.Equal(AwardRules.Default[AwardRules.CuresPenalty], restored.Rules.Awards[AwardRules.CuresPenalty]);
    }
}
