using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.People;
using static CorsixNet.Content.Tests.PatientFlowTests;

namespace CorsixNet.Content.Tests;

/// <summary>
/// Transparent and invisible patients (CorsixTH's diseases/transparency.lua, invisibility.lua): their looks, and the
/// pharmacy turning them into standard patients (pharmacy_cabinet.lua's morphs, pharmacy.lua's after_use_pharmacy).
/// </summary>
public class SeeThroughPatientTests
{
    [Fact]
    public void SeeThroughPatientsComeAndThePharmacyMakesThemStandard()
    {
        var simulation = NewStaffedHospital(Rules());
        BuildPharmacy(simulation);
        var seen = new HashSet<HumanoidType>();
        var cured = new HashSet<HumanoidType>();
        var morphs = new HashSet<int>();
        var before = new Dictionary<int, HumanoidType>();

        for (var tick = 0; tick < 4 * 31 * GameDate.HoursPerDay && cured.Count < 3; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is not PatientBehaviour)
                {
                    continue;
                }

                if (humanoid.Type is HumanoidType.TransparentMalePatient or HumanoidType.TransparentFemalePatient or HumanoidType.InvisiblePatient)
                {
                    seen.Add(humanoid.Type);
                    before[humanoid.Id] = humanoid.Type;
                    if (humanoid.MorphAnimation is { } into)
                    {
                        morphs.Add(into.Id);
                    }
                }
                else if (before.TryGetValue(humanoid.Id, out var was))
                {
                    Assert.Equal(was == HumanoidType.TransparentFemalePatient ? HumanoidType.StandardFemalePatient : HumanoidType.StandardMalePatient, humanoid.Type);
                    cured.Add(was);
                    before.Remove(humanoid.Id);
                }
            }
        }

        Assert.Equal([HumanoidType.TransparentMalePatient, HumanoidType.TransparentFemalePatient, HumanoidType.InvisiblePatient], seen.Order());
        Assert.NotEmpty(cured);

        // Drinking morphs into the standard man's or woman's drinking (1662, 3108).
        Assert.NotEmpty(morphs);
        Assert.Subset(new HashSet<int> { 1662, 3108 }, morphs);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        Assert.Equal(simulation.ComputeStateHash(), Simulation.Load(stream, BaseGameContent.Create()).ComputeStateHash());
    }

    /// <summary>Invisibility and transparency, known and easy to diagnose at the GP's, cured at the pharmacy.</summary>
    private static LevelRules Rules()
    {
        var visuals = new int[DiseaseRules.VisualsCount];
        visuals[DiseaseCatalog.Find("invisibility")!.VisualsId!.Value] = 1;
        visuals[DiseaseCatalog.Find("transparency")!.VisualsId!.Value] = 1;
        var diseases = new DiseaseRules(
            Enumerable.Repeat(100, DiseaseRules.ExpertiseCount + 1).ToArray(),
            Enumerable.Repeat(true, DiseaseRules.ExpertiseCount + 1).ToArray(),
            new int[DiseaseRules.ExpertiseCount + 1],
            visuals,
            new int[DiseaseRules.NonVisualsCount],
            new int[DiseaseRules.VisualsCount],
            new int[DiseaseRules.NonVisualsCount],
            0,
            0);
        return new LevelRules(
            1_000_000,
            [KeyValuePair.Create("gp", 2500), KeyValuePair.Create("pharmacy", 2500)],
            [KeyValuePair.Create(ReceptionDesks.ObjectId, 600)],
            diseases: diseases);
    }
}
