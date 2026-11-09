using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Serialization;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Sim.Tests;

/// <summary>Firing staff and raise requests (CorsixTH's Staff:fire, requestRaise, increaseWage, checkIfWaitedTooLongForRaise).</summary>
public class StaffEmploymentTests
{
    [Fact]
    public void FiredStaffGetAMonthsWageCostReputationAndLeave()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);
        var hospital = simulation.Hospitals[0];
        var balance = hospital.Balance;
        var reputation = hospital.Reputation;

        Run(simulation, new FireStaffCommand(doctor.Id));

        Assert.True(doctor.IsFired);
        Assert.IsType<LeavingBehaviour>(doctor.Behaviour);
        Assert.Equal(balance - doctor.Profile!.Wage, hospital.Balance);
        Assert.Contains(hospital.Transactions, t => t is { Kind: TransactionKind.Severance, Subject: "Doctor" } && t.Amount == -doctor.Profile.Wage);
        Assert.Equal(reputation - 3, hospital.Reputation);

        // Firing again does nothing, and the fired no longer tire or mind the temperature.
        Run(simulation, new FireStaffCommand(doctor.Id));
        Assert.Equal(balance - doctor.Profile.Wage, hospital.Balance);
        doctor.Happiness = 0.5m;
        Feelings.OnEndDay(simulation, doctor);
        Assert.Equal(0.5m, doctor.Happiness);
    }

    [Fact]
    public void OnlyTheirOwnHospitalCanFireThem()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);

        simulation.Schedule(1, new FireStaffCommand(doctor.Id));
        simulation.Step();

        Assert.False(doctor.IsFired);
    }

    [Fact]
    public void VeryUnhappyStaffAskForARaiseNowAndThen()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);
        doctor.Happiness = 0.05m;

        var hours = 0;
        while (doctor.RaiseRequest == 0 && hours < 100_000)
        {
            StaffEmployment.Tick(simulation, doctor);
            hours++;
        }

        // At least 200 hours of unhappiness first, then one time in five.
        Assert.True(hours > StaffEmployment.RaiseDelay);
        Assert.Equal(StaffEmployment.RiseAmount(simulation, doctor.Profile!), doctor.RaiseRequest);
        Assert.InRange(doctor.QuittingIn, 750, 800);

        // While a request waits, nobody asks again.
        var request = doctor.RaiseRequest;
        StaffEmployment.Tick(simulation, doctor);
        Assert.Equal(request, doctor.RaiseRequest);
    }

    [Fact]
    public void TheRaiseIsAHalfWayToTheFairWageOrATenthMore()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);
        var profile = doctor.Profile!;
        var fair = profile.FairWage(simulation.Rules.Staff);

        profile.Wage = fair - 100;
        Assert.Equal(50, StaffEmployment.RiseAmount(simulation, profile));
        profile.Wage = fair;
        Assert.Equal((int)decimal.Floor(fair * 0.1m), StaffEmployment.RiseAmount(simulation, profile));
    }

    [Fact]
    public void GrantingARaisePaysMoreAndCheersUp()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);
        var wage = doctor.Profile!.Wage;
        doctor.Happiness = 0.05m;
        doctor.RaiseRequest = 40;
        doctor.QuittingIn = 500;

        Run(simulation, new AnswerRaiseCommand(doctor.Id, Grant: true));

        Assert.Equal(wage + 40, doctor.Profile.Wage);
        Assert.Equal(1m, doctor.Happiness);
        Assert.Equal(0, doctor.RaiseRequest);
        Assert.Equal(-1, doctor.QuittingIn);
        Assert.False(doctor.IsFired);
    }

    [Fact]
    public void RefusingARaiseFiresThem()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);
        doctor.RaiseRequest = 40;
        doctor.QuittingIn = 500;

        Run(simulation, new AnswerRaiseCommand(doctor.Id, Grant: false));

        Assert.True(doctor.IsFired);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnansweredRequestEndsInQuittingOrARaiseByPolicy(bool grant)
    {
        var simulation = NewSimulation();
        Run(simulation, new SetPolicyCommand(PolicyKind.GrantWageIncrease, grant ? 1 : 0));
        var doctor = Hire(simulation, StaffClass.Doctor);
        var wage = doctor.Profile!.Wage;
        var rise = StaffEmployment.RiseAmount(simulation, doctor.Profile);
        doctor.RaiseRequest = 40;
        doctor.QuittingIn = 1;

        StaffEmployment.Tick(simulation, doctor);
        Assert.False(doctor.IsFired);
        StaffEmployment.Tick(simulation, doctor);

        Assert.Equal(!grant, doctor.IsFired);
        Assert.Equal(grant ? wage + rise : wage, doctor.Profile.Wage);
        Assert.Equal(-1, doctor.QuittingIn);
    }

    [Fact]
    public void NobodyOnTheHighestSalaryAsks()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);
        doctor.Profile!.Wage = simulation.Rules.MaxSalary;
        doctor.Happiness = 0.05m;

        for (var i = 0; i < 5 * StaffEmployment.RaiseDelay; i++)
        {
            StaffEmployment.Tick(simulation, doctor);
        }

        Assert.Equal(0, doctor.RaiseRequest);
    }

    [Fact]
    public void FiredStaffWalkOffAndAreNotPaid()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);
        Run(simulation, new FireStaffCommand(doctor.Id));
        var balance = simulation.Hospitals[0].Balance;

        for (var i = 0; i < 31 * 50 && simulation.Entities.Contains(doctor); i++)
        {
            simulation.Step();
        }

        Assert.DoesNotContain(doctor, simulation.Entities);
        Assert.DoesNotContain(simulation.Hospitals[0].Transactions, t => t.Kind == TransactionKind.Wages && t.Amount > 0);
        Assert.True(simulation.Hospitals[0].Balance >= balance - 1000);
    }

    [Fact]
    public void ABonusCostsATenthOfTheWageAndCheersUp()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);
        doctor.Happiness = 0.2m;
        var balance = simulation.Hospitals[0].Balance;

        Run(simulation, new PayBonusCommand(doctor.Id));

        Assert.Equal(balance - (doctor.Profile!.Wage / 10), simulation.Hospitals[0].Balance);
        Assert.Contains(simulation.Hospitals[0].Transactions, t => t.Kind == TransactionKind.PersonalBonus);
        Assert.True(doctor.Happiness >= 0.65m);
    }

    [Fact]
    public void ARaiseFromTheStaffScreenAddsATenthAndAnswersARequest()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);
        var wage = doctor.Profile!.Wage;
        doctor.RaiseRequest = 40;
        doctor.QuittingIn = 300;

        Run(simulation, new IncreaseSalaryCommand(doctor.Id));

        Assert.Equal(Math.Min(wage + (wage / 10), simulation.Rules.MaxSalary), doctor.Profile.Wage);
        Assert.Equal(0, doctor.RaiseRequest);
        Assert.Equal(-1, doctor.QuittingIn);
    }

    [Fact]
    public void TheCommandsAndSavesKeepItAll()
    {
        foreach (var command in new Command[] { new FireStaffCommand(7), new AnswerRaiseCommand(8, true), new PayBonusCommand(9), new IncreaseSalaryCommand(10) })
        {
            using var wire = new MemoryStream();
            command.Write(new SimWriter(wire));
            wire.Position = 0;
            Assert.Equal(command, Command.Read(new SimReader(wire)));
        }

        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);
        doctor.RaiseRequest = 40;
        doctor.QuittingIn = 300;
        doctor.RaiseTimer = 12;
        var nurse = Hire(simulation, StaffClass.Nurse);

        // Fired but not yet gone (the test map has no spawn points, so a step would see them out at once).
        StaffEmployment.Fire(simulation, nurse);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        var copy = (Humanoid)restored.FindEntity(doctor.Id)!;
        Assert.Equal(40, copy.RaiseRequest);
        Assert.True(((Humanoid)restored.FindEntity(nurse.Id)!).IsFired);
        Assert.IsType<LeavingBehaviour>(((Humanoid)restored.FindEntity(nurse.Id)!).Behaviour);
    }

    private static Humanoid Hire(Simulation simulation, StaffClass staffClass)
    {
        Run(simulation, new HireStaffCommand(staffClass, 0, new TilePosition(4, 4)));
        return simulation.Entities.OfType<Humanoid>().Last();
    }

    private static void Run(Simulation simulation, Command command)
    {
        simulation.Schedule(0, command);
        simulation.Step();
    }

    private static Simulation NewSimulation()
    {
        var map = new LevelMap(10, 10, playerCount: 2);
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                ref var tile = ref map[x, y];
                tile.Floor = 1;
                tile.Set(TileFlags.Passable, true);
                if (x is > 1 and < 8 && y is > 1 and < 8)
                {
                    tile.Parcel = 1;
                    tile.Set(TileFlags.Buildable | TileFlags.Hospital, true);
                }
            }
        }

        return new Simulation(map, seed: 1, rules: new LevelRules(10_000, [], []));
    }
}
