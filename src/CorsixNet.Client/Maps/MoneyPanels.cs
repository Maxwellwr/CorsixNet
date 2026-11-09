using System.Globalization;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Economy;

namespace CorsixNet.Client.Maps;

/// <summary>
/// Text stand-ins for CorsixTH's bank statement (<c>dialogs/fullscreen/bank_manager.lua</c>, F2) and graphs
/// (<c>dialogs/fullscreen/graphs.lua</c>, F8) until the original's screens are drawn: the same figures as lines.
/// </summary>
/// <remarks>
/// The labels are plain English for now; the original's come from its language files (<c>_S.transactions</c>), which
/// are not read yet.
/// </remarks>
internal static class MoneyPanels
{
    /// <summary>Months the graphs show at most, as CorsixTH's monthly view.</summary>
    private const int GraphMonths = 12;

    /// <summary>The transaction log, latest first: date, what for, amount and the balance afterwards.</summary>
    public static IEnumerable<string> Statement(Hospital hospital)
    {
        yield return string.Create(CultureInfo.InvariantCulture, $"Bank statement (F2 closes). Balance {MoneyText.Format(hospital.Balance)}, loan {MoneyText.Format(hospital.Loan)}");
        foreach (var transaction in hospital.Transactions)
        {
            var date = GameDate.FromHours(transaction.Hour);
            var what = Describe(transaction);
            yield return string.Create(CultureInfo.InvariantCulture, $"{date.Day,2}/{date.Month,2}/{date.Year}  {what,-28} {MoneyText.Format(transaction.Amount),10} {MoneyText.Format(transaction.Balance),10}");
        }
    }

    /// <summary>The last months' figures, latest first: what the graphs plot.</summary>
    public static IEnumerable<string> Graphs(Hospital hospital)
    {
        yield return "Graphs (F8 closes)   month     in       out     wages    balance  visitors cures deaths  rep";
        var statistics = hospital.Statistics;
        for (var i = statistics.Count - 1; i >= Math.Max(0, statistics.Count - GraphMonths); i--)
        {
            var month = statistics[i];
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"{(i == 0 ? "start" : $"month {i}"),26} {MoneyText.Format(month.MoneyIn),9} {MoneyText.Format(month.MoneyOut),9} {MoneyText.Format(month.Wages),9} {MoneyText.Format(month.Balance),10} {month.Visitors,8} {month.Cures,5} {month.Deaths,6} {month.Reputation,4}");
        }
    }

    private static string Describe(Transaction transaction) => transaction.Kind switch
    {
        TransactionKind.BuyObject => $"Buy {transaction.Subject}",
        TransactionKind.SellObject => $"Sell {transaction.Subject}",
        TransactionKind.BuildRoom => $"Build {transaction.Subject}",
        TransactionKind.HireStaff => "Hire staff",
        TransactionKind.Wages => "Wages",
        TransactionKind.Treatment => $"Treatment: {transaction.Subject}",
        TransactionKind.Insurance => $"Insurance company {transaction.Subject}",
        TransactionKind.DrugCost => "Drug costs",
        TransactionKind.TrophyBonus => "Trophy money",
        TransactionKind.AwardBonus => "Awards and fines",
        TransactionKind.Loan => "Bank loan",
        TransactionKind.LoanRepayment => "Loan repayment",
        TransactionKind.LoanInterest => "Loan interest",
        TransactionKind.Overdraft => "Overdraft",
        TransactionKind.BuyLand => $"Buy parcel {transaction.Subject}",
        TransactionKind.Heating => "Heating",
        TransactionKind.Cheat => "Cheat",
        _ => transaction.Kind.ToString(),
    };
}
