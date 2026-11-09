using System.Text;
using CorsixNet.Content.Levels;
using CorsixNet.Formats;
using CorsixNet.Formats.Tests;
using CorsixNet.Sim.Disasters;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Content.Tests;

public class LevelRulesLoaderTests
{
    [Theory]
    [InlineData("LEVEL.L1", 1)]
    [InlineData("level.l12", 12)]
    [InlineData(@"C:\Games\LEVELS\LEVEL.L20", 20)]
    [InlineData("custom.map", null)]
    [InlineData("LEVEL.L", null)]
    public void RecognisesOriginalLevelMaps(string fileName, int? expected)
    {
        Assert.Equal(expected, LevelRulesLoader.LevelNumber(fileName));
    }

    [Fact]
    public void TakesCostsByConfigNumberAndTheLevelsOwnTownFirst()
    {
        var config = LevelConfig.Parse(Encoding.ASCII.GetBytes("""
            #town.StartCash 10000
            #towns[3].StartCash 50000
            #town.StartRep 450
            #towns[3].StartRep 600
            #towns[3].InterestRate 300
            #town.OverdraftDiff 250
            #gbv.LandCostPerTile 40
            #awards_trophies.CuresAward 30
            #rooms[7].Cost 2280
            #objects[2].StartCost 100
            #objects[1].StartCost 300
            """));
        var content = BaseGameContent.Create();

        var rules = LevelRulesLoader.FromConfig(config, 3, content);

        Assert.Equal(50000, rules.StartCash);
        Assert.Equal(2280, rules.RoomCost("gp"));
        Assert.Equal(100, rules.ObjectCost("cabinet"));
        Assert.Equal(10000, LevelRulesLoader.FromConfig(config, 4, content).StartCash);
        Assert.Equal(10000, LevelRulesLoader.FromConfig(config, null, content).StartCash);
        Assert.Equal(LevelRulesLoader.DefaultStartCash, LevelRulesLoader.FromConfig(LevelConfig.Parse([]), 1, content).StartCash);
        Assert.Equal(600, rules.StartReputation);
        Assert.Equal((300, 250), (rules.InterestRate, rules.OverdraftDifferential));
        Assert.Equal(Hospital.DefaultInterestRate, LevelRulesLoader.FromConfig(config, 4, content).InterestRate);
        Assert.Equal(40, rules.LandCostPerTile);
        Assert.Equal(30, rules.Awards[AwardRules.CuresAward]);
        Assert.Equal(AwardRules.Default[AwardRules.CuresBonus], rules.Awards[AwardRules.CuresBonus]);
        Assert.Equal(LevelRules.DefaultLandCostPerTile, LevelRulesLoader.FromConfig(LevelConfig.Parse([]), 1, content).LandCostPerTile);
        Assert.Equal(450, LevelRulesLoader.FromConfig(config, 4, content).StartReputation);
        Assert.Equal(Hospital.AverageReputation, LevelRulesLoader.FromConfig(LevelConfig.Parse([]), 1, content).StartReputation);
    }

    [Fact]
    public void ReadsTheOriginalLevelOneMoney()
    {
        var install = RealGameData.Require();
        var content = BaseGameContent.Create();

        var rules = LevelRulesLoader.LoadOriginal(install, "LEVEL.L1", content);

        Assert.Equal(40000, rules.StartCash);
        Assert.Equal(2280, rules.RoomCost("gp"));
        Assert.Equal(100, rules.ObjectCost("cabinet"));
        Assert.Equal(20, rules.ObjectCost("chair"));
        Assert.True(rules.ObjectCost("desk") > 0);
        Assert.Equal(2280 + rules.ObjectCost("desk") + 100 + 20, rules.BuildCost(BaseGameContent.GpOffice));

        // Every original level can be won.
        Assert.NotEmpty(rules.EndConditions.WinGoals);
    }

    [Fact]
    public void ReadsStaffSettingsOnTopOfTheDefaults()
    {
        var config = LevelConfig.Parse(Encoding.ASCII.GetBytes("""
            #staff[1].MinSalary 80 Doctor
            #gbv.SalaryAdd[7] 10 Consultant
            #gbv.SalaryAbilityDivisor 0 must be > 0
            #gbv.DoctorThreshold 300
            #staff_levels[0].Month.Nurses.Doctors 0 4 9
            #staff_levels[1].Month.Doctors.JrRate 4 5 1
            #staff_levels[3].Month.Doctors 8 1
            """));

        var staff = LevelRulesLoader.StaffFromConfig(config);

        Assert.Equal(80, staff.MinSalary(StaffClass.Doctor));
        Assert.Equal(60, staff.MinSalary(StaffClass.Nurse));
        Assert.Equal(10, staff.SalaryAdd(StaffRules.ConsultantAdd));
        Assert.Equal(-30, staff.SalaryAdd(StaffRules.JuniorAdd));
        Assert.Equal(10, staff.SalaryAbilityDivisor);
        Assert.Equal(300, staff.DoctorThreshold);
        Assert.Equal(750, staff.ConsultantThreshold);

        // Entry 2 is missing, so entry 3 is never reached, as with Lua's length operator.
        Assert.Equal(2, staff.Levels.Count);
        Assert.Equal(9, staff.AvailableCount(StaffClass.Doctor, 0));
        Assert.Equal(4, staff.AvailableCount(StaffClass.Nurse, 0));
        Assert.Equal(3, staff.AvailableCount(StaffClass.Handyman, 0));
        Assert.Equal(5, staff.AvailableCount(StaffClass.Doctor, 9));
        Assert.Equal(1, staff.LevelValue(4, level => level.JrRate));
        Assert.Equal(10, staff.LevelValue(4, level => level.ShrkRate));
    }

    [Fact]
    public void ReadsWhichObjectsTheLevelHas()
    {
        var config = LevelConfig.Parse(Encoding.ASCII.GetBytes("""
            #objects[2].StartCost.StartAvail.AvailableForLevel 100 0 1
            #objects[1].StartCost.StartAvail.AvailableForLevel 100 1 0
            """));
        var content = BaseGameContent.Create();

        var rules = LevelRulesLoader.FromConfig(config, null, content);

        Assert.Equal(new ObjectAvailability(true, false), rules.Availability("cabinet"));
        Assert.Equal(new ObjectAvailability(false, true), rules.Availability("desk"));
        Assert.Equal(new ObjectAvailability(true, true), rules.Availability("chair"));
        Assert.False(rules.IsRoomAvailable(BaseGameContent.GpOffice));
    }

    [Fact]
    public void ReadsThePlannedEarthquakesUpToTheFirstMissingOne()
    {
        var config = LevelConfig.Parse(Encoding.ASCII.GetBytes("""
            #quake_control[0].StartMonth.EndMonth.Severity 5 10 3
            #quake_control[1].StartMonth.EndMonth.Severity 15 20 9
            #quake_control[3].StartMonth.EndMonth.Severity 35 40 2
            """));

        var rules = LevelRulesLoader.FromConfig(config, null, BaseGameContent.Create());

        Assert.Equal([new PlannedEarthquake(5, 10, 3), new PlannedEarthquake(15, 20, 9)], rules.Earthquakes);
        Assert.Empty(LevelRulesLoader.FromConfig(LevelConfig.Parse([]), null, BaseGameContent.Create()).Earthquakes);
    }

    [Fact]
    public void ReadsWhenStaffCountAsTired()
    {
        var config = LevelConfig.Parse(Encoding.ASCII.GetBytes("""
            #gbv.Tired 500
            #gbv.CrackUpTired 900
            """));

        var rules = LevelRulesLoader.FromConfig(config, null, BaseGameContent.Create());

        Assert.Equal(new TirednessRules(500, 700, 900), rules.Tiredness);
        Assert.Equal(TirednessRules.Default, LevelRulesLoader.FromConfig(LevelConfig.Parse([]), null, BaseGameContent.Create()).Tiredness);
    }

    [Fact]
    public void ReadsHowOftenVipsCome()
    {
        var config = LevelConfig.Parse(Encoding.ASCII.GetBytes("#gbv.MayorLaunch 90"));

        Assert.Equal(90, LevelRulesLoader.FromConfig(config, null, BaseGameContent.Create()).MayorLaunch);
        Assert.Equal(LevelRules.DefaultMayorLaunch, LevelRulesLoader.FromConfig(LevelConfig.Parse([]), null, BaseGameContent.Create()).MayorLaunch);
    }

    [Fact]
    public void ReadsHowContagiousDiseasesAreAndTheEpidemicSettings()
    {
        var config = LevelConfig.Parse(Encoding.ASCII.GetBytes("""
            #expertise[17].ContRate.Known.RschReqd 30 1 0
            #gbv.ContagiousSpreadFactor 40
            #gbv.EpidemicFine 3000
            #gbv.EpidemicCompHi 9000
            #gbv.EpidemicConcurrentLimit 2
            """));

        var epidemics = LevelRulesLoader.FromConfig(config, null, BaseGameContent.Create()).Epidemics;

        Assert.Equal(30, epidemics.ContagiousRate(17));
        Assert.Equal(0, epidemics.ContagiousRate(16));
        Assert.Equal(40, epidemics.SpreadFactor);
        Assert.Equal(3000, epidemics.Fine);
        Assert.Equal(1000, epidemics.CompensationLow);
        Assert.Equal(9000, epidemics.CompensationHigh);
        Assert.Equal(2, epidemics.ConcurrentLimit);
        Assert.Equal(14, epidemics.ReduceContagiousMonths);
        Assert.Equal(50, epidemics.VaccinationCost);
    }

    [Fact]
    public void ReadsThePlannedEmergenciesSteppingOverOneMissingNumber()
    {
        var config = LevelConfig.Parse(Encoding.ASCII.GetBytes("""
            #emergency_control[0].StartMonth.EndMonth.Min.Max.Illness.PercWin.Bonus 4 5 4 6 16 75 400
            #emergency_control[2].StartMonth.EndMonth.Min.Max.Illness.PercWin.Bonus 8 10 3 5 32 75 500
            """));

        var emergencies = LevelRulesLoader.FromConfig(config, null, BaseGameContent.Create()).Emergencies;

        Assert.Equal([new PlannedEmergency(0, 4, 5, 4, 6, 16, 75, 400), new PlannedEmergency(2, 8, 10, 3, 5, 32, 75, 500)], emergencies);
        Assert.Empty(LevelRulesLoader.FromConfig(LevelConfig.Parse([]), null, BaseGameContent.Create()).Emergencies);
    }

    [Fact]
    public void EitherRandomOrMeanMakesTheEmergenciesRandom()
    {
        RandomEmergencies? Read(string text) =>
            LevelRulesLoader.FromConfig(LevelConfig.Parse(Encoding.ASCII.GetBytes(text)), null, BaseGameContent.Create()).RandomEmergencies;

        // As in Lua, 0 counts: CorsixTH's own campaign levels say "Random 0".
        Assert.Equal(new RandomEmergencies(180, 30), Read("#emergency_control[0].Random 0"));
        Assert.Equal(new RandomEmergencies(90, 10), Read("#emergency_control[0].Mean.Variance 90 10"));
        Assert.Null(Read("#emergency_control[0].StartMonth.EndMonth.Min.Max.Illness.PercWin.Bonus 4 5 4 6 16 75 400"));
    }

    [Theory]
    [InlineData("easy", Difficulty.Easy)]
    [InlineData("Medium", Difficulty.Medium)]
    [InlineData("FULL", Difficulty.Medium)]
    [InlineData("hard", Difficulty.Hard)]
    public void DifficultiesAreReadByNameAndNameTheirFiles(string name, Difficulty difficulty)
    {
        Assert.True(LevelRulesLoader.TryParseDifficulty(name, out var read));
        Assert.Equal(difficulty, read);
        Assert.Equal(name.ToUpperInvariant() == "MEDIUM" ? "FULL" : name.ToUpperInvariant(), LevelRulesLoader.FilePrefix(read));
        Assert.Equal(difficulty, LevelRulesLoader.FromConfig(LevelConfig.Parse([]), 1, BaseGameContent.Create(), difficulty).Difficulty);
    }

    [Fact]
    public void AnUnknownDifficultyIsNotRead() => Assert.False(LevelRulesLoader.TryParseDifficulty("nightmare", out _));

    [Fact]
    public void OnlyTheCampaignHasADifficulty()
    {
        var install = RealGameData.Require();
        var content = BaseGameContent.Create();

        var easy = LevelRulesLoader.LoadOriginal(install, "LEVEL.L3", content, Difficulty.Easy);
        var hard = LevelRulesLoader.LoadOriginal(install, "LEVEL.L3", content, Difficulty.Hard);
        Assert.Equal(Difficulty.Easy, easy.Difficulty);
        Assert.Equal(Difficulty.Hard, hard.Difficulty);
        Assert.Equal(Difficulty.Medium, LevelRulesLoader.LoadOriginal(install, "LEVEL.L20", content, Difficulty.Hard).Difficulty);
    }

    [Fact]
    public void TheOriginalLevelsPlanEarthquakesWithTheirMonthsInOrder()
    {
        var install = RealGameData.Require();
        var content = BaseGameContent.Create();

        for (var level = 1; level <= 12; level++)
        {
            var rules = LevelRulesLoader.LoadOriginal(install, $"LEVEL.L{level}", content);
            Assert.All(rules.Earthquakes, earthquake => Assert.True(earthquake.Severity == 0 || earthquake.StartMonth <= earthquake.EndMonth, $"Level {level}: {earthquake}"));
        }
    }

    [Fact]
    public void TheOriginalLevelOneHasTheGpsOfficeFromTheStart()
    {
        var install = RealGameData.Require();
        var content = BaseGameContent.Create();

        var rules = LevelRulesLoader.LoadOriginal(install, "LEVEL.L1", content);

        Assert.True(rules.IsRoomDiscoveredAtStart(BaseGameContent.GpOffice));
        Assert.Contains("gp", LevelMapLoader.LoadOriginal(install, "LEVEL.L1").StartSimulation(seed: 1).Hospitals[0].DiscoveredRooms);
    }

    [Fact]
    public void ReadsTheOriginalLevelOneStaffSettings()
    {
        var install = RealGameData.Require();

        var staff = LevelRulesLoader.LoadOriginal(install, "LEVEL.L1", BaseGameContent.Create()).Staff;

        // FULL00.SAM gives the salaries; FULL01.SAM its own staff levels.
        Assert.Equal(75, staff.MinSalary(StaffClass.Doctor));
        Assert.Equal(100, staff.SalaryAdd(StaffRules.ConsultantAdd));
        Assert.Equal(10, staff.SalaryAbilityDivisor);
        Assert.Equal(6, staff.Levels.Count);
        Assert.Equal(8, staff.AvailableCount(StaffClass.Doctor, 0));
        Assert.Equal(8, staff.AvailableCount(StaffClass.Receptionist, 0));
        Assert.Equal(6, staff.AvailableCount(StaffClass.Doctor, 3));
        Assert.Equal(0, staff.LevelValue(0, level => level.SurgRate));
        Assert.Equal(10, staff.LevelValue(0, level => level.JrRate));
    }

    [Fact]
    public void StartsOriginalLevelsWithTheirMoney()
    {
        var install = RealGameData.Require();

        var simulation = LevelMapLoader.LoadOriginal(install, "LEVEL.L1").StartSimulation(seed: 1);

        Assert.Equal(40000, simulation.Hospitals[0].Balance);

        // The hospital is worth its parcel's tiles and 20,000.
        Assert.True(simulation.Hospitals[0].Value > Hospital.StartValueBesidesLand);
        Assert.Equal(2280, simulation.Rules.RoomCost("gp"));
    }

    [Fact]
    public void ReadsTheOriginalLevelOnePopulation()
    {
        var install = RealGameData.Require();

        var rules = LevelRulesLoader.LoadOriginal(install, "LEVEL.L1", BaseGameContent.Create());

        // FULL01.SAM: 3 patients the first month, one more each month until month 7, and so on.
        Assert.Equal([new(0, 3), new(1, 1), new(7, 0), new(14, 1), new(21, 0)], rules.Population);
    }

    [Fact]
    public void ReadsWinAndLoseCriteria()
    {
        var config = LevelConfig.Parse(Encoding.ASCII.GetBytes("""
            #town.StartCash.StartRep 30000 450
            #win_criteria[0].Criteria.MaxMin.Value.Group.Bound 1 1 600 1 0
            #win_criteria[1].Criteria.MaxMin.Value.Group.Bound 2 1 50000 1 0
            #win_criteria[2].Criteria.MaxMin.Value.Group.Bound 0 0 0 0 0
            #lose_criteria[0].Criteria.MaxMin.Value.Group.Bound 2 0 -20000 1 -10000
            #lose_criteria[4].Criteria.MaxMin.Value.Group.Bound 5 1 40 2 30
            #lose_criteria[5].Criteria.MaxMin.Value.Group.Bound 12 1 40 2 30
            """));

        var conditions = LevelRulesLoader.FromConfig(config, null, BaseGameContent.Create()).EndConditions;

        Assert.Equal(
            [new EndGoal(EndCriterion.Reputation, 1, true, 600, 0, 450), new EndGoal(EndCriterion.Balance, 1, true, 50000, 0, 30000)],
            conditions.WinGoals);
        Assert.Equal(
            [new EndGoal(EndCriterion.Balance, 1, false, -20000, -10000, 30000), new EndGoal(EndCriterion.PercentageKilled, 2, true, 40, 30, 0)],
            conditions.LoseGoals);
        Assert.Empty(LevelRulesLoader.FromConfig(LevelConfig.Parse([]), 1, BaseGameContent.Create()).EndConditions.WinGoals);
    }

    [Fact]
    public void ThePopulationFallsBackOnCorsixThsDefaults()
    {
        var config = LevelConfig.Parse(Encoding.ASCII.GetBytes("#popn[0].Month.Change 0 2\n"));

        Assert.Equal([new(0, 2), new(1, 1), new(27, 0)], LevelRulesLoader.PopulationFromConfig(config));
    }

    [Fact]
    public void ReadsDiseaseSettingsOnTopOfTheDefaults()
    {
        var config = LevelConfig.Parse(Encoding.ASCII.GetBytes("""
            #expertise[2].StartPrice.Known.MaxDiagDiff 900 1 200 BLOATY_HEAD
            #non_visuals[0] 5 I_UNCOMMON_COLD
            #non_visuals_available[0] 4 I_UNCOMMON_COLD
            """));

        var diseases = LevelRulesLoader.DiseasesFromConfig(config);
        var cold = DiseaseCatalog.Find("uncommon_cold")!;

        Assert.Equal(900, diseases.StartPrice(2));
        Assert.True(diseases.IsKnown(2));
        Assert.Equal(200, diseases.MaxDiagDiff(2));
        Assert.Equal(300, diseases.StartPrice(16));
        Assert.Equal(350, diseases.MaxDiagDiff(16));
        Assert.True(diseases.IsKnown(1));
        Assert.Equal([cold], diseases.AvailableDiseases());
        Assert.Equal(2, diseases.HoldVisualMonths);
        Assert.False(diseases.IsUsableForNewPatient(cold, monthOfGame: 4, visitors: 0));
        Assert.True(diseases.IsUsableForNewPatient(cold, monthOfGame: 5, visitors: 0));
    }

    [Fact]
    public void ReadsThePlayingComputerOpponentsInCorsixThsOrder()
    {
        var config = LevelConfig.Parse(Encoding.ASCII.GetBytes("""
            #computer[0].Playing.Name 1 DEEPBLUE
            #computer[3].Playing 1
            #computer[12].Playing.Name 1 CORSIX
            #computer[13].Playing.Name 0 ROUJIN
            """));

        var rules = LevelRulesLoader.FromConfig(config, null, BaseGameContent.Create());

        // pairs visits base_config.lua's 1 to 14 before 0; a missing name is base_config.lua's.
        Assert.Equal(
            [new PlannedCompetitor(4, "MULTIVAC"), new PlannedCompetitor(13, "CORSIX"), new PlannedCompetitor(1, "DEEPBLUE")],
            rules.Competitors);
        Assert.Empty(LevelRulesLoader.CompetitorsFromConfig(LevelConfig.Parse([])));
    }

    [Fact]
    public void ReadsTheSalaryScoreAndKeepsTheLevelNumber()
    {
        var config = LevelConfig.Parse(Encoding.ASCII.GetBytes("#gbv.ScoreMaxInc 250\n"));

        var rules = LevelRulesLoader.FromConfig(config, 3, BaseGameContent.Create());

        Assert.Equal(250, rules.ScoreMaxIncrease);
        Assert.Equal(3, rules.LevelNumber);
        Assert.Equal(LevelRules.DefaultScoreMaxIncrease, LevelRulesLoader.FromConfig(LevelConfig.Parse([]), null, BaseGameContent.Create()).ScoreMaxIncrease);
    }

    [Fact]
    public void ReadsTheOriginalLevelOneDiseases()
    {
        var install = RealGameData.Require();

        var diseases = LevelRulesLoader.LoadOriginal(install, "LEVEL.L1", BaseGameContent.Create()).Diseases;

        // FULL01.SAM: two visual and four non-visual diseases; prices from FULL00.SAM, difficulties from FULL01.SAM.
        Assert.Equal(
            ["bloaty_head", "heaped_piles", "invisibility", "the_squits", "tv_personalities", "uncommon_cold"],
            diseases.AvailableDiseases().Select(d => d.Id));
        Assert.Equal(850, diseases.CurePrice(DiseaseCatalog.Find("bloaty_head")!));
        Assert.Equal(200, diseases.MaxDiagDiff(2));
        Assert.True(diseases.IsKnown(5));
        Assert.False(diseases.IsKnown(2));
        Assert.Equal(2, diseases.HoldVisualPeepCount);
    }
}
