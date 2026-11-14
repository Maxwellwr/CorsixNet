using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Research;
using static CorsixNet.Sim.Definitions.FootprintFlags;

namespace CorsixNet.Sim.Tests;

/// <summary>Research (CorsixTH's research_department.lua).</summary>
public class ResearchTests
{
    /// <summary>The ultrascanner's object type and fallback expertise.</summary>
    private const int Ultrascanner = 22;

    private const int UltrascanExpertise = 40;

    private const int Inflator = 9;

    private static readonly ObjectDefinition Machine = new(
        "ultrascanner", "Ultrascanner", Ultrascanner, new Dictionary<Direction, ObjectOrientation> { [Direction.North] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition)]) });

    private static readonly RoomDefinition Ultrascan = new(
        "ultrascan", "Ultrascan", 4, 19, new WallSet(84, 85, 92, 93), new Dictionary<string, int> { ["ultrascanner"] = 1 }, LevelConfigId: 14);

    [Fact]
    public void EachAreaWithSomethingToDoGetsAFifthAndTheRestGoesToTheFirst()
    {
        var research = NewSimulation().Hospitals[0].Research;

        // Nothing to discover for cures; the diagnosis area takes the share left over.
        Assert.Equal(0, research.Area(ResearchCategory.Cure).Share);
        Assert.Equal(40, research.Area(ResearchCategory.Diagnosis).Share);
        Assert.Equal(ResearchTarget.Object("ultrascanner"), research.Area(ResearchCategory.Diagnosis).Current);
        Assert.Equal(20, research.Area(ResearchCategory.Drugs).Share);
        Assert.Equal(ResearchTarget.Drain, research.Area(ResearchCategory.Drugs).Current);
        Assert.Equal(ResearchTarget.Object("inflator"), research.Area(ResearchCategory.Improvements).Current);
        Assert.Equal(ResearchTarget.Drain, research.Area(ResearchCategory.Specialisation).Current);
        Assert.Equal(100, research.Total);
        Assert.False(research.Objects["ultrascanner"].Discovered);
        Assert.True(research.Objects["inflator"].Discovered);
    }

    [Fact]
    public void EnoughPointsDiscoverTheUltrascannerAndItsRoomCanBeBuilt()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        Assert.False(hospital.IsRoomDiscovered("ultrascan"));

        // 20,000 points over the divisor of 5 are 4,000; the diagnosis area's 40 % of them, times 0.75 to 1.25, beat 1,000.
        hospital.Research.AddPoints(simulation, hospital, 20_000);

        Assert.True(hospital.Research.Objects["ultrascanner"].Discovered);
        Assert.True(hospital.IsRoomDiscovered("ultrascan"));

        // Nothing more to discover: the area's share goes to the others.
        Assert.True(hospital.Research.Area(ResearchCategory.Diagnosis).Current.IsNone);
        Assert.Equal(0, hospital.Research.Area(ResearchCategory.Diagnosis).Share);
        Assert.Equal(100, hospital.Research.Total);
    }

    [Fact]
    public void TheAllResearchCheatDiscoversEveryObjectToResearch()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];

        Run(simulation, new CheatCommand(CheatKind.AllResearch));

        Assert.True(hospital.Research.Objects["ultrascanner"].Discovered);
        Assert.True(hospital.IsRoomDiscovered("ultrascan"));
        Assert.True(hospital.Cheated);
    }

    [Fact]
    public void TooFewPointsAreKeptForLater()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        hospital.Research.AddPoints(simulation, hospital, 1_000);

        Assert.False(hospital.Research.Objects["ultrascanner"].Discovered);
        Assert.InRange(hospital.Research.Objects["ultrascanner"].Points, 60m, 100m);
    }

    [Fact]
    public void EquipmentComesByItselfInTheMonthTheLevelNames()
    {
        var simulation = NewSimulation(whenAvail: 3);
        var hospital = simulation.Hospitals[0];
        hospital.Research.CheckAutomaticDiscovery(simulation, hospital, 2);
        Assert.False(hospital.IsRoomDiscovered("ultrascan"));

        hospital.Research.CheckAutomaticDiscovery(simulation, hospital, 3);
        Assert.True(hospital.IsRoomDiscovered("ultrascan"));
    }

    [Fact]
    public void ADiscoveredMachineImprovesInStrengthThenInCost()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        var inflator = hospital.Research.Objects["inflator"];
        Assert.Equal(10, inflator.StartStrength);

        // An improvement takes 10 % of the 2,000 points of discovery: 200.
        Assert.Equal(200m, hospital.Research.Required(simulation, ResearchTarget.Object("inflator")));
        while (inflator.StrengthImprovements == 0)
        {
            hospital.Research.AddPoints(simulation, hospital, 1_000);
        }

        Assert.Equal(12, inflator.StartStrength);
        while (inflator.CostImprovements == 0)
        {
            hospital.Research.AddPoints(simulation, hospital, 1_000);
        }

        // An eighth of 5,000 rounded to tens comes off, and the next improvement takes 20 %.
        Assert.Equal(5_000 - 630, inflator.Cost);
        Assert.Equal(5_000 - 630, hospital.Research.ObjectCost(simulation.Rules, "inflator"));
    }

    [Fact]
    public void ADiscoveredDrugGetsCheaperOrMoreEffective()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        var disease = simulation.Rules.Diseases.AvailableDiseases().First(ResearchDepartment.IsDrugDisease);
        var entry = hospital.CaseOf(disease, simulation.Rules.Diseases);
        entry.CureEffectiveness = 80;
        entry.Discovered = true;
        hospital.Research.OnDiseaseDiscovered(simulation, hospital, disease);
        Assert.Equal(ResearchTarget.Drug(disease.Id), hospital.Research.Area(ResearchCategory.Drugs).Current);

        for (var i = 0; i < 50 && entry.CureEffectiveness == 80 && entry.DrugCost == 100; i++)
        {
            hospital.Research.AddPoints(simulation, hospital, 1_000);
        }

        Assert.True(entry.CureEffectiveness == 85 || entry.DrugCost == 90, $"Effectiveness {entry.CureEffectiveness}, cost {entry.DrugCost}.");
    }

    [Fact]
    public void ResearchCostsThreeADayPerResearcherAndIsPaidAtTheMonthsEnd()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];

        // Diagnosis (40 %) and improvements (20 %) work on something real; drugs and specialisation do not.
        hospital.Research.AccumulateCost(researchers: 2);
        Assert.Equal(4, hospital.Research.AccumulatedCost);

        var balance = hospital.Balance;
        hospital.PayResearch(10);
        Assert.Equal(balance - 4, hospital.Balance);
        Assert.Equal(new Transaction(10, TransactionKind.Research, string.Empty, -4, balance - 4), hospital.Transactions[0]);
        Assert.Equal(0, hospital.Research.AccumulatedCost);
    }

    [Fact]
    public void TheResearchWindowMovesSharesWithinOneHundredPercent()
    {
        var simulation = NewSimulation();
        var research = simulation.Hospitals[0].Research;

        Run(simulation, new AdjustResearchCommand(ResearchCategory.Improvements, -15));
        Assert.Equal(5, research.Area(ResearchCategory.Improvements).Share);
        Assert.Equal(85, research.Total);

        // Not above 100 %, and not for an area with nothing to work on.
        Run(simulation, new AdjustResearchCommand(ResearchCategory.Diagnosis, 50));
        Assert.Equal(55, research.Area(ResearchCategory.Diagnosis).Share);
        Run(simulation, new AdjustResearchCommand(ResearchCategory.Cure, 5));
        Assert.Equal(0, research.Area(ResearchCategory.Cure).Share);
        Assert.Equal(100, research.Total);
    }

    [Fact]
    public void ResearchIsSaved()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        hospital.Research.AddPoints(simulation, hospital, 1_000);
        hospital.Research.AccumulateCost(1);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, NewContent());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        var copy = loaded.Hospitals[0].Research;
        Assert.Equal(hospital.Research.Objects["ultrascanner"].Points, copy.Objects["ultrascanner"].Points);
        Assert.Equal(hospital.Research.AccumulatedCost, copy.AccumulatedCost);
        Assert.Equal(hospital.Research.Area(ResearchCategory.Diagnosis).Current, copy.Area(ResearchCategory.Diagnosis).Current);
        Assert.Equal(ResearchRules.DefaultMinDrugCost, loaded.Rules.Research.MinDrugCost);
        Assert.Equal(1_000, loaded.Rules.Research.ExpertiseRequired(UltrascanExpertise));
    }

    private static void Run(Simulation simulation, Command command)
    {
        simulation.Schedule(0, command);
        simulation.Step();
    }

    private static GameContent NewContent() => new([Ultrascan], [114], [Machine]);

    [Fact]
    public void OnlyObjectsTheLevelHasAndResearchHasFoundCanBeBought()
    {
        var simulation = NewSimulation();
        var research = simulation.Hospitals[0].Research;

        // UIEditRoom:purchaseItems: an undiscovered machine is not offered, a discovered one and plain furniture are.
        Assert.False(research.CanBuy(simulation.Rules, "ultrascanner"));
        Assert.True(research.CanBuy(simulation.Rules, "inflator"));
        Assert.True(research.CanBuy(simulation.Rules, "cabinet"));

        var level = new LevelRules(1000, [], [], objectAvailability: [KeyValuePair.Create("analyser", new ObjectAvailability(ForLevel: false, AtStart: false))]);
        Assert.False(research.CanBuy(level, "analyser"));
    }
    private static Simulation NewSimulation(int whenAvail = 0)
    {
        var map = TestMaps.FromRows("........", "........", "........", "........", "........", "........", "........", "........");
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        var expertise = new int[DiseaseRules.ExpertiseCount + 1];
        expertise[UltrascanExpertise] = 1_000;
        expertise[2] = 2_000;
        var research = new ResearchRules(
            [
                KeyValuePair.Create(Ultrascanner, new ResearchObjectRules(6_000, StartAvail: false, whenAvail, 12, ForLevel: true, null)),
                KeyValuePair.Create(Inflator, new ResearchObjectRules(5_000, StartAvail: true, 0, 10, ForLevel: true, null)),
            ],
            expertise);
        var rules = new LevelRules(
            100_000,
            [KeyValuePair.Create("ultrascan", 1_000)],
            [KeyValuePair.Create("ultrascanner", 6_000)],
            objectAvailability: [KeyValuePair.Create("ultrascanner", new ObjectAvailability(ForLevel: true, AtStart: false))],
            research: research);
        return new Simulation(map, seed: 3, NewContent(), rules);
    }
}
