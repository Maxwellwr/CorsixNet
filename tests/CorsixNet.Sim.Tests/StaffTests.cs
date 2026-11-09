using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Sim.Tests;

/// <summary>Staff profiles, candidates for hire and hiring.</summary>
public class StaffTests
{
    private static readonly RoomDefinition Office = new("office", "Office", 4, 18, new WallSet(82, 83, 90, 91));

    private static readonly GameContent Content = new([Office], [114], []);

    private static readonly LevelRules Rules = new(1000, [], []);

    [Theory]
    [InlineData(StaffClass.Nurse, 500, 110)] // 60 + 500 / 10
    [InlineData(StaffClass.Handyman, 999, 124)] // 25 + 99
    [InlineData(StaffClass.Receptionist, 0, 20)]
    [InlineData(StaffClass.Doctor, 500, 155)] // 75 + 50 + 30 (doctor)
    [InlineData(StaffClass.Doctor, 200, 75)] // 75 + 20 - 30 (junior) is below the minimum
    [InlineData(StaffClass.Doctor, 250, 75)] // 250 is still a junior
    [InlineData(StaffClass.Doctor, 750, 250)] // 75 + 75 + 100 (consultant)
    public void TheFairWageFollowsSkillAndAbilities(StaffClass staffClass, int skill, int wage)
    {
        var profile = StaffProfile.Create(staffClass, skill, StaffRules.Default, new SimRandom(1));

        Assert.Equal(wage, profile.Wage);
        Assert.Equal(wage, profile.FairWage(StaffRules.Default));
    }

    [Fact]
    public void EachDoctorAbilityAddsToTheWage()
    {
        // 75 + 50 + 30 (doctor) + 40 (surgeon) + 30 (psychiatrist) + 20 (researcher)
        var profile = StaffProfile.Create(StaffClass.Doctor, 500, StaffRules.Default, new SimRandom(1), psychiatrist: true, surgeon: true, researcher: true);

        Assert.Equal(245, profile.Wage);
        Assert.False(profile.IsJunior);
        Assert.False(profile.IsConsultant);
    }

    [Fact]
    public void AbilitiesOnlyChangeTheWagesOfDoctors()
    {
        var profile = StaffProfile.Create(StaffClass.Nurse, 100, StaffRules.Default, new SimRandom(1), surgeon: true);

        Assert.Equal(70, profile.Wage);
        Assert.True(profile.IsJunior);
    }

    [Fact]
    public void RandomDoctorsFallIntoTheSkillBandOfTheirRank()
    {
        var random = new SimRandom(3);
        var rules = StaffRules.Default;
        var seen = new HashSet<(bool Junior, bool Consultant)>();
        for (var i = 0; i < 500; i++)
        {
            var doctor = StaffProfile.CreateRandom(StaffClass.Doctor, 0, rules, random);
            Assert.InRange(doctor.Skill, 0, StaffProfile.MaxSkill);
            Assert.False(doctor.IsJunior && doctor.IsConsultant);
            Assert.Equal(doctor.Skill <= rules.DoctorThreshold, doctor.IsJunior);
            Assert.Equal(doctor.Skill >= rules.ConsultantThreshold, doctor.IsConsultant);
            Assert.InRange(doctor.Wage, rules.MinSalary(StaffClass.Doctor), 400);
            Assert.InRange(doctor.HairIndex, 0, 9);
            Assert.Equal(doctor.HairIndex >= 5 ? 4 : 2, doctor.Layer5);
            seen.Add((doctor.IsJunior, doctor.IsConsultant));
        }

        // Juniors (one in 5), consultants (one in 10 of the rest) and ordinary doctors all turn up.
        Assert.Equal(3, seen.Count);
    }

    [Theory]
    [InlineData(StaffClass.Nurse, 10, 12)]
    [InlineData(StaffClass.Receptionist, 13, 14)]
    [InlineData(StaffClass.Handyman, 15, 17)]
    public void OtherStaffHaveTheirOwnFaces(StaffClass staffClass, int first, int last)
    {
        var random = new SimRandom(5);
        for (var i = 0; i < 50; i++)
        {
            var profile = StaffProfile.CreateRandom(staffClass, 0, StaffRules.Default, random);
            Assert.InRange(profile.HairIndex, first, last);
            Assert.InRange(profile.FaceIndex, first, last);
            Assert.InRange(profile.ChinIndex, first, last);
            Assert.Equal(2, profile.Layer5);
        }
    }

    [Fact]
    public void ARateOfZeroMeansNoDoctorHasTheAbility()
    {
        var rules = new StaffRules(
            [60, 75, 25, 20],
            [-30, 30, 40, 30, 100, 20],
            10,
            250,
            750,
            [new StaffLevel(0, 8, 8, 3, 2, ShrkRate: 1, SurgRate: 0, RschRate: 1, JrRate: 0, ConsRate: 1)]);
        var random = new SimRandom(9);
        for (var i = 0; i < 20; i++)
        {
            var doctor = StaffProfile.CreateRandom(StaffClass.Doctor, 0, rules, random);
            Assert.True(doctor.IsPsychiatrist);
            Assert.False(doctor.IsSurgeon);
            Assert.True(doctor.IsResearcher);
            Assert.True(doctor.IsConsultant);
        }
    }

    [Fact]
    public void LaterStaffLevelsApplyFromTheirMonthAndFallBackForMissingValues()
    {
        var rules = new StaffRules(
            [60, 75, 25, 20],
            [-30, 30, 40, 30, 100, 20],
            10,
            250,
            750,
            [
                new StaffLevel(0, 8, 8, 3, 2, 10, 10, 10, 5, 10),
                new StaffLevel(3, Doctors: 6, Nurses: 7),
                new StaffLevel(5, Handymen: 1),
            ]);

        Assert.Equal(8, rules.AvailableCount(StaffClass.Doctor, 2));
        Assert.Equal(6, rules.AvailableCount(StaffClass.Doctor, 3));
        Assert.Equal(7, rules.AvailableCount(StaffClass.Nurse, 4));
        Assert.Equal(3, rules.AvailableCount(StaffClass.Handyman, 4));
        Assert.Equal(1, rules.AvailableCount(StaffClass.Handyman, 5));
        Assert.Equal(6, rules.AvailableCount(StaffClass.Doctor, 12));
        Assert.Equal(2, rules.AvailableCount(StaffClass.Receptionist, 12));
        Assert.Throws<ArgumentException>(() => new StaffRules([60, 75, 25, 20], [0, 0, 0, 0, 0, 0], 10, 250, 750, [new StaffLevel(0, Doctors: 1)]));
    }

    [Fact]
    public void TheGameStartsWithCandidatesForHire()
    {
        var simulation = NewSimulation(Rules);

        Assert.Equal(8, simulation.AvailableStaff(StaffClass.Doctor).Count);
        Assert.Equal(8, simulation.AvailableStaff(StaffClass.Nurse).Count);
        Assert.Equal(3, simulation.AvailableStaff(StaffClass.Handyman).Count);
        Assert.Equal(2, simulation.AvailableStaff(StaffClass.Receptionist).Count);
        Assert.All(simulation.AvailableStaff(StaffClass.Nurse), nurse => Assert.Equal(StaffClass.Nurse, nurse.Class));
    }

    [Fact]
    public void HiringPlacesTheCandidateAndPaysAMonthsSalary()
    {
        var simulation = NewSimulation(Rules);
        var candidate = simulation.AvailableStaff(StaffClass.Doctor)[2];

        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 2, new(1, 1)));

        var doctor = Assert.Single(simulation.Entities.OfType<Humanoid>());
        Assert.Same(candidate, doctor.Profile);
        Assert.Equal(HumanoidType.Doctor, doctor.Type);
        Assert.Equal(new TilePosition(1, 1), doctor.Tile);
        Assert.Equal(0, doctor.Employer);
        Assert.Equal(candidate.Layer5, doctor.Layers[Humanoid.StaffLayer]);
        Assert.Equal(7, simulation.AvailableStaff(StaffClass.Doctor).Count);
        Assert.DoesNotContain(candidate, simulation.AvailableStaff(StaffClass.Doctor));

        var hospital = simulation.Hospitals[0];
        Assert.Equal(1000 - candidate.Wage, hospital.Balance);
        Assert.Equal(new Transaction(0, TransactionKind.HireStaff, "Doctor", -candidate.Wage, 1000 - candidate.Wage), hospital.Transactions[0]);
    }

    [Fact]
    public void HiringNeedsAMonthsSalaryInTheBank()
    {
        var simulation = NewSimulation(new LevelRules(10, [], []));

        Assert.Equal(StaffHireProblem.CannotAfford, StaffHiring.Check(simulation, StaffClass.Doctor, 0, new(1, 1), 0));
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, new(1, 1)));

        Assert.Empty(simulation.Entities.OfType<Humanoid>());
        Assert.Equal(8, simulation.AvailableStaff(StaffClass.Doctor).Count);
        Assert.Equal(10, simulation.Hospitals[0].Balance);
    }

    [Fact]
    public void WithoutRulesStaffWorkForNothing()
    {
        var simulation = NewSimulation(rules: null);

        Assert.All(simulation.AvailableStaff(StaffClass.Doctor), doctor => Assert.Equal(0, doctor.Wage));
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, new(1, 1)));

        Assert.Single(simulation.Entities.OfType<Humanoid>());
        Assert.Equal(0, simulation.Hospitals[0].Balance);
    }

    [Fact]
    public void StaffArePlacedOnPassableTilesOfThePlayersHospital()
    {
        var simulation = NewSimulation(Rules);
        simulation.Map[5, 5].Set(TileFlags.Hospital, false);
        simulation.Map[6, 6].Set(TileFlags.Passable, false);

        Assert.Equal(StaffHireProblem.None, StaffHiring.CheckPlacement(simulation, StaffClass.Nurse, new(1, 1), 0));
        Assert.Equal(StaffHireProblem.OutsideMap, StaffHiring.CheckPlacement(simulation, StaffClass.Nurse, new(-1, 1), 0));
        Assert.Equal(StaffHireProblem.NotInHospital, StaffHiring.CheckPlacement(simulation, StaffClass.Nurse, new(5, 5), 0));
        Assert.Equal(StaffHireProblem.NotPassable, StaffHiring.CheckPlacement(simulation, StaffClass.Nurse, new(6, 6), 0));

        simulation.SetParcelOwner(1, 0);
        Assert.Equal(StaffHireProblem.NotInHospital, StaffHiring.CheckPlacement(simulation, StaffClass.Nurse, new(1, 1), 0));
    }

    [Fact]
    public void StaffOtherThanReceptionistsMayBePlacedInRooms()
    {
        var simulation = NewSimulation(Rules);
        Run(simulation, new BuildRoomCommand(new RoomPlan("office", new TileRect(2, 2, 4, 4), new DoorPlacement(new(3, 6), EdgeOrientation.North))));

        Assert.Equal(StaffHireProblem.None, StaffHiring.CheckPlacement(simulation, StaffClass.Doctor, new(3, 3), 0));
        Assert.Equal(StaffHireProblem.NoRoomsForReceptionists, StaffHiring.CheckPlacement(simulation, StaffClass.Receptionist, new(3, 3), 0));

        Run(simulation, new HireStaffCommand(StaffClass.Receptionist, 0, new(3, 3)));
        Assert.Empty(simulation.Entities.OfType<Humanoid>());
    }

    [Fact]
    public void ACommandForAMissingCandidateDoesNothing()
    {
        var simulation = NewSimulation(Rules);

        Run(simulation, new HireStaffCommand(StaffClass.Receptionist, 2, new(1, 1)));
        Run(simulation, new HireStaffCommand(StaffClass.Receptionist, -1, new(1, 1)));
        Run(simulation, new HireStaffCommand(StaffClass.Receptionist, 0, new(1, 1)), player: 1);

        Assert.Empty(simulation.Entities.OfType<Humanoid>());
        Assert.Equal(2, simulation.AvailableStaff(StaffClass.Receptionist).Count);
    }

    [Fact]
    public void SaveAndLoadKeepCandidatesStaffAndStaffRules()
    {
        var rules = new LevelRules(1000, [], [], new StaffRules(
            [61, 76, 26, 21],
            [-31, 31, 41, 31, 101, 21],
            9,
            300,
            700,
            [new StaffLevel(0, 2, 3, 1, 1, 4, 4, 4, 4, 4), new StaffLevel(6, Nurses: 1)]));
        var simulation = NewSimulation(rules);
        Run(simulation, new HireStaffCommand(StaffClass.Nurse, 1, new(1, 1)));

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, Content);

        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        Assert.Equal(simulation.AvailableStaff(StaffClass.Doctor).Select(d => d.Wage), loaded.AvailableStaff(StaffClass.Doctor).Select(d => d.Wage));
        Assert.Equal(2, loaded.AvailableStaff(StaffClass.Nurse).Count);
        var nurse = loaded.Entities.OfType<Humanoid>().Single();
        Assert.Equal(HumanoidType.Nurse, nurse.Type);
        Assert.Equal(simulation.Entities.OfType<Humanoid>().Single().Profile!.Wage, nurse.Profile!.Wage);
        Assert.Equal(1, loaded.Rules.Staff.AvailableCount(StaffClass.Nurse, 6));
        Assert.Equal(76, loaded.Rules.Staff.MinSalary(StaffClass.Doctor));
        Assert.Equal(-31, loaded.Rules.Staff.SalaryAdd(StaffRules.JuniorAdd));
    }

    [Fact]
    public void StaffArePaidAtTheEndOfEachMonth()
    {
        var simulation = NewSimulation(Rules);
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, new(1, 1)));
        Run(simulation, new HireStaffCommand(StaffClass.Nurse, 0, new(2, 1)));
        var wages = simulation.Entities.OfType<Humanoid>().Sum(staff => staff.Profile!.Wage);
        var hospital = simulation.Hospitals[0];
        var afterHiring = hospital.Balance;

        // The last hour of 31 January ends the month.
        var endOfJanuary = (31 * GameDate.HoursPerDay) - 1;
        StepUntil(simulation, endOfJanuary);
        Assert.Equal(afterHiring, hospital.Balance);

        simulation.Step();
        Assert.Equal(afterHiring - wages, hospital.Balance);
        Assert.Equal(new Transaction(endOfJanuary, TransactionKind.Wages, string.Empty, -wages, afterHiring - wages), hospital.Transactions[0]);

        StepUntil(simulation, (31 + 28) * GameDate.HoursPerDay);
        Assert.Equal(afterHiring - (2 * wages), hospital.Balance);
    }

    [Fact]
    public void NoWagesAreLoggedWithoutStaff()
    {
        var simulation = NewSimulation(Rules);

        StepUntil(simulation, 31 * GameDate.HoursPerDay);

        Assert.Empty(simulation.Hospitals[0].Transactions);
        Assert.Equal(1000L, simulation.Hospitals[0].Balance);
    }

    [Fact]
    public void TheCandidatesAreReplacedAtTheEndOfEachMonth()
    {
        var rules = new LevelRules(1000, [], [], new StaffRules(
            [60, 75, 25, 20],
            [-30, 30, 40, 30, 100, 20],
            10,
            250,
            750,
            [new StaffLevel(0, 8, 8, 3, 2, 4, 4, 4, 4, 4), new StaffLevel(1, Nurses: 1), new StaffLevel(2, Doctors: 5)]));
        var simulation = NewSimulation(rules);
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, new(1, 1)));
        var january = simulation.AvailableStaff(StaffClass.Doctor).ToList();

        // The staff levels of month 1 apply from the end of January.
        StepUntil(simulation, 31 * GameDate.HoursPerDay);
        Assert.Equal(8, simulation.AvailableStaff(StaffClass.Doctor).Count);
        Assert.Single(simulation.AvailableStaff(StaffClass.Nurse));
        Assert.NotEqual(january, simulation.AvailableStaff(StaffClass.Doctor));

        StepUntil(simulation, (31 + 28) * GameDate.HoursPerDay);
        Assert.Equal(5, simulation.AvailableStaff(StaffClass.Doctor).Count);
        Assert.Single(simulation.AvailableStaff(StaffClass.Nurse));
    }

    [Fact]
    public void TheHireCommandSurvivesTheWire()
    {
        var command = new HireStaffCommand(StaffClass.Handyman, 2, new(4, 5));
        using var stream = new MemoryStream();
        command.Write(new Serialization.SimWriter(stream));
        stream.Position = 0;

        Assert.Equal(command, Command.Read(new Serialization.SimReader(stream)));
    }

    [Fact]
    public void TheLevelsStartingStaffJoinTheHospitalForNothing()
    {
        var staffRules = new StaffRules(
            [60, 75, 25, 20],
            [-30, 30, 40, 30, 100, 20],
            10,
            250,
            750,
            StaffRules.Default.Levels,
            [new StartingStaff(StaffClass.Doctor, 1000, Psychiatrist: true), new StartingStaff(StaffClass.Nurse, 300), new StartingStaff(StaffClass.Doctor, 100, Surgeon: true)]);
        var simulation = NewSimulation(new LevelRules(1000, [], [], staffRules));

        var staff = simulation.Entities.OfType<Humanoid>().ToList();
        Assert.Equal(3, staff.Count);
        Assert.All(staff, member => Assert.Equal(0, member.Employer));
        Assert.All(staff, member => Assert.True(simulation.Map.Contains(member.Tile.X, member.Tile.Y) && simulation.Map[member.Tile.X, member.Tile.Y].Has(TileFlags.Hospital)));
        Assert.Equal(1000L, simulation.Hospitals[0].Balance);

        var consultant = staff[0].Profile!;
        Assert.Equal((StaffClass.Doctor, 1000, true, false, true), (consultant.Class, consultant.Skill, consultant.IsPsychiatrist, consultant.IsSurgeon, consultant.IsConsultant));
        Assert.Equal(StaffClass.Nurse, staff[1].Profile!.Class);
        Assert.Equal(300, staff[1].Profile!.Skill);
        var junior = staff[2].Profile!;
        Assert.True(junior is { IsJunior: true, IsSurgeon: true, IsPsychiatrist: false });
        Assert.Equal(junior.FairWage(staffRules), junior.Wage);

        // The starting staff are part of the level's saved settings.
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, Content);
        Assert.Equal(staffRules.StartStaff, loaded.Rules.Staff.StartStaff);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    private static void Run(Simulation simulation, Command command, int player = 0)
    {
        simulation.Schedule(player, command);
        simulation.Step();
    }

    private static void StepUntil(Simulation simulation, long tick)
    {
        while (simulation.Tick < tick)
        {
            simulation.Step();
        }
    }

    private static Simulation NewSimulation(LevelRules? rules)
    {
        var map = TestMaps.FromRows(
            "..........",
            "..........",
            "..........",
            "..........",
            "..........",
            "..........",
            "..........",
            "..........",
            "..........",
            "..........");
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Parcel = 1;
                map[x, y].Set(TileFlags.Buildable | TileFlags.Hospital | TileFlags.BuildableNorth | TileFlags.BuildableEast | TileFlags.BuildableSouth | TileFlags.BuildableWest, true);
            }
        }

        map.UpdatePathfinding();
        map.UpdateShadows();
        return new Simulation(map, seed: 11, Content, rules);
    }
}
