using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.People;

namespace CorsixNet.Sim.Tests;

/// <summary>The disease catalog, a level's disease settings, treatment prices and insurance.</summary>
public class DiseaseTests
{
    [Fact]
    public void TheCatalogHasEveryDiseaseAndDiagnosis()
    {
        Assert.Equal(34, DiseaseCatalog.All.Count(d => !d.IsPseudo));
        Assert.Equal(9, DiseaseCatalog.All.Count(d => d.IsPseudo));
        Assert.Equal(DiseaseCatalog.All.Count, DiseaseCatalog.All.Select(d => d.Id).Distinct().Count());

        var cold = DiseaseCatalog.Find("uncommon_cold")!;
        Assert.Equal(16, cold.ExpertiseId);
        Assert.Equal(0, cold.NonVisualsId);
        Assert.Equal(["pharmacy"], cold.TreatmentRooms);
        Assert.Equal(100, DiseaseCatalog.Find(DiseaseCatalog.GpDiagnosis)!.CurePrice);
        Assert.True(DiseaseCatalog.Find("alien_dna")!.OnlyEmergency);

        // Every diagnosis room a disease names has its pseudo disease, so that it can be paid for.
        foreach (var room in DiseaseCatalog.All.SelectMany(d => d.DiagnosisRooms).Distinct())
        {
            Assert.NotNull(DiseaseCatalog.Find(DiseaseCatalog.DiagnosisPrefix + room));
        }
    }

    [Fact]
    public void VisualDiseasesAreHeldBackAndComeFromTheirMonth()
    {
        var visuals = new int[DiseaseRules.VisualsCount];
        var available = new int[DiseaseRules.VisualsCount];
        visuals[0] = 5;
        available[0] = 3;
        var rules = new DiseaseRules(
            Enumerable.Range(0, DiseaseRules.ExpertiseCount + 1).Select(i => DiseaseRules.Default.StartPrice(i)).ToArray(),
            new bool[DiseaseRules.ExpertiseCount + 1],
            new int[DiseaseRules.ExpertiseCount + 1],
            visuals,
            new int[DiseaseRules.NonVisualsCount],
            available,
            new int[DiseaseRules.NonVisualsCount],
            holdVisualMonths: 2,
            holdVisualPeepCount: 6);
        var bloaty = DiseaseCatalog.Find("bloaty_head")!;

        Assert.Equal([bloaty], rules.AvailableDiseases());
        Assert.False(rules.IsUsableForNewPatient(bloaty, monthOfGame: 4, visitors: 5));
        Assert.False(rules.IsUsableForNewPatient(bloaty, monthOfGame: 3, visitors: 6));
        Assert.True(rules.IsUsableForNewPatient(bloaty, monthOfGame: 4, visitors: 6));
        Assert.False(rules.IsUsableForNewPatient(DiseaseCatalog.Find("alien_dna")!, 10, 10));
    }

    [Fact]
    public void PricesFollowTheLevelThePlayerAndInflation()
    {
        var hospital = new Hospital(0, 0);
        var rules = DiseaseRules.Default;
        var cold = DiseaseCatalog.Find("uncommon_cold")!;
        var gp = DiseaseCatalog.Find(DiseaseCatalog.GpDiagnosis)!;

        Assert.Equal(300, hospital.TreatmentPrice(cold, rules));
        Assert.Equal(100, hospital.TreatmentPrice(gp, rules));

        hospital.CaseOf(cold, rules).PricePercent = 150;
        Assert.Equal(450, hospital.TreatmentPrice(cold, rules));

        hospital.OnEndYear(0, new SimRandom(1), AwardRules.Default);
        Assert.Equal(105, hospital.TreatmentPrice(gp, rules)); // ceil(100 * 1.045) = 105
        hospital.OnEndYear(0, new SimRandom(1), AwardRules.Default);
        Assert.Equal(110, hospital.TreatmentPrice(gp, rules)); // ceil(100 * 1.045²) = ceil(109.2025)
    }

    [Fact]
    public void InsuranceCompaniesPayTwoMonthsLater()
    {
        var hospital = new Hospital(0, 1000);
        var gp = hospital.CaseOf(DiseaseCatalog.Find(DiseaseCatalog.GpDiagnosis)!, DiseaseRules.Default);

        hospital.ReceiveForTreatment(1, gp, 100, insuranceCompany: 0);
        hospital.ReceiveForTreatment(2, gp, 100, insuranceCompany: 2);
        Assert.Equal(1100, hospital.Balance);
        Assert.Equal(100, hospital.InsuranceOwed(2));
        Assert.Equal(200, gp.MoneyEarned);
        Assert.Equal(TransactionKind.Treatment, hospital.Transactions[0].Kind);

        hospital.PayInsurance(3);
        hospital.PayInsurance(4);
        Assert.Equal(1100, hospital.Balance);
        hospital.PayInsurance(5);
        Assert.Equal(1200, hospital.Balance);
        Assert.Equal(0, hospital.InsuranceOwed(2));
        Assert.Equal(new Transaction(5, TransactionKind.Insurance, "2", 100, 1200), hospital.Transactions[0]);
    }

    [Fact]
    public void AQuarterOfPatientsPayThroughInsurance()
    {
        var random = new SimRandom(11);
        var insured = Enumerable.Range(0, 4000).Select(_ => PatientCare.ChooseInsuranceCompany(random)).ToList();

        Assert.InRange(insured.Count(c => c > 0), 850, 1150);
        Assert.All(insured, c => Assert.InRange(c, 0, Hospital.InsuranceCompanies));
    }
}
