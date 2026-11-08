using System.Text;
using CorsixNet.Content.Levels;
using CorsixNet.Formats;
using CorsixNet.Formats.Tests;
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
    public void ThePopulationFallsBackOnCorsixThsDefaults()
    {
        var config = LevelConfig.Parse(Encoding.ASCII.GetBytes("#popn[0].Month.Change 0 2\n"));

        Assert.Equal([new(0, 2), new(1, 1), new(27, 0)], LevelRulesLoader.PopulationFromConfig(config));
    }
}
