using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;

namespace CorsixNet.Sim.Tests;

/// <summary>Loans from the bank, interest and overdrafts.</summary>
public class LoanTests
{
    [Fact]
    public void AHospitalMayBorrowAThirdOfItsValueAndTenThousand()
    {
        var hospital = new Hospital(0, 1000, value: 50_000);
        Assert.Equal(25_000, hospital.MaxLoan); // floor(16,500 / 5,000) * 5,000 + 10,000

        hospital.Borrow(1, asMuchAsAllowed: false);
        Assert.Equal((5000, 6000L), (hospital.Loan, hospital.Balance));
        Assert.Equal(new Transaction(1, TransactionKind.Loan, string.Empty, 5000, 6000), hospital.Transactions[0]);

        hospital.Borrow(2, asMuchAsAllowed: true);
        Assert.Equal((25_000, 26_000L), (hospital.Loan, hospital.Balance));

        // No more: the loan may not grow past the limit.
        hospital.Borrow(3, asMuchAsAllowed: false);
        Assert.Equal(25_000, hospital.Loan);
        Assert.Equal(2, hospital.Transactions.Count);
    }

    [Fact]
    public void LoansArePaidBackInStepsOfFiveThousand()
    {
        var hospital = new Hospital(0, 0, value: 60_000);
        hospital.Borrow(1, asMuchAsAllowed: true);
        Assert.Equal(25_000, hospital.Loan);

        hospital.Repay(2, asMuchAsPossible: false);
        Assert.Equal((20_000, 20_000L), (hospital.Loan, hospital.Balance));

        // With 12,000 in the bank as much as possible is 10,000.
        hospital.Spend(3, 8000, TransactionKind.Wages, string.Empty);
        hospital.Repay(4, asMuchAsPossible: true);
        Assert.Equal((10_000, 2000L), (hospital.Loan, hospital.Balance));

        // Without 5,000 in the bank nothing is paid back.
        hospital.Repay(5, asMuchAsPossible: false);
        hospital.Repay(5, asMuchAsPossible: true);
        Assert.Equal(10_000, hospital.Loan);
        Assert.Equal(TransactionKind.LoanRepayment, hospital.Transactions[0].Kind);
    }

    [Fact]
    public void InterestIsRunUpDailyAndPaidMonthly()
    {
        // 1 % a year on 36,500 is a unit a day; an overdraft costs 3 % a year.
        var hospital = new Hospital(0, 0, value: 100_000);
        hospital.Borrow(1, asMuchAsAllowed: true);
        Assert.Equal(40_000, hospital.Loan);
        hospital.Spend(1, 40_000 + 36_500, TransactionKind.Wages, string.Empty);
        Assert.Equal(-36_500, hospital.Balance);

        for (var day = 0; day < 30; day++)
        {
            hospital.OnEndDay();
        }

        hospital.PayInterest(100);

        // 30 × 40,000 × 0.01 / 365 = 32.88; 30 × 36,500 × 0.03 / 365 = 90.
        Assert.Equal(new Transaction(100, TransactionKind.LoanInterest, string.Empty, -33, -36_533), hospital.Transactions[1]);
        Assert.Equal(new Transaction(100, TransactionKind.Overdraft, string.Empty, -90, -36_623), hospital.Transactions[0]);
    }

    [Fact]
    public void InterestThatRoundsToNothingWaits()
    {
        var hospital = new Hospital(0, 0, value: 0);
        hospital.Borrow(1, asMuchAsAllowed: false);
        hospital.Borrow(1, asMuchAsAllowed: false);
        Assert.Equal(10_000, hospital.Loan);

        // 10,000 × 0.01 / 365 = 0.27 a day: nothing to pay after one day, 1 after two.
        hospital.OnEndDay();
        hospital.PayInterest(10);
        Assert.Equal(TransactionKind.Loan, hospital.Transactions[0].Kind);
        hospital.OnEndDay();
        hospital.PayInterest(20);
        Assert.Equal(new Transaction(20, TransactionKind.LoanInterest, string.Empty, -1, 9999), hospital.Transactions[0]);
    }

    [Fact]
    public void TheLevelSetsTheRatesAndCommandsBorrowAndRepay()
    {
        var map = TestMaps.FromRows("....", "....");
        var rules = new LevelRules(0, [], [], interestRate: 365, overdraftDifferential: 0, endConditions: new EndConditions([new(EndCriterion.Balance, 1, true, 0, 0, 0)], []));
        var simulation = new Simulation(map, seed: 1, rules: rules);
        var hospital = simulation.Hospitals[0];
        Assert.Equal((365, 0), (hospital.InterestRate, hospital.OverdraftDifferential));

        simulation.Schedule(0, new BorrowCommand(AsMuchAsAllowed: true));
        simulation.Step();
        Assert.Equal(15_000, hospital.Loan); // floor(6,600 / 5,000) * 5,000 + 10,000 on the value of 20,000

        // 3.65 % on 15,000 is 1.5 a day: the month's interest is paid at its end, after the wages.
        while (simulation.Date.Month == 1)
        {
            simulation.Step();
        }

        Assert.Equal(new Transaction(simulation.Tick - 1, TransactionKind.LoanInterest, string.Empty, -47, 15_000 - 47), hospital.Transactions[0]);

        // The balance goal is met at the end of March, but a hospital with a loan does not win.
        while (simulation.Date.Month < 4)
        {
            simulation.Step();
        }

        Assert.Equal(EndOutcome.None, hospital.Outcome);

        simulation.Schedule(0, new RepayCommand(AsMuchAsPossible: true));
        simulation.Step();
        Assert.Equal(5000, hospital.Loan);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(5000, restored.Hospitals[0].Loan);
        Assert.Equal(365, restored.Rules.InterestRate);
    }
}
