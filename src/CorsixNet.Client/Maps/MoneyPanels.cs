using System.Globalization;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Economy;

namespace CorsixNet.Client.Maps;

/// <summary>One line of the bank statement.</summary>
internal sealed record StatementLine(string Date, string What, long Amount, long Balance);

/// <summary>
/// The figures of CorsixTH's bank statement (<c>dialogs/fullscreen/bank_manager.lua</c>, F2) and graphs
/// (<c>dialogs/fullscreen/graphs.lua</c>, F8), for their windows.
/// </summary>
/// <remarks>
/// The labels are plain English for now; the original's come from its language files (<c>_S.transactions</c>).
/// </remarks>
internal static class MoneyPanels
{
    /// <summary>Months the graphs show at most, as CorsixTH's monthly view.</summary>
    public const int GraphMonths = 12;

    /// <summary>The transaction log, latest first.</summary>
    public static IEnumerable<StatementLine> StatementLines(Hospital hospital)
    {
        foreach (var transaction in hospital.Transactions)
        {
            var date = GameDate.FromHours(transaction.Hour);
            yield return new StatementLine(
                string.Create(CultureInfo.InvariantCulture, $"{date.Day,2}/{date.Month,2}/{date.Year}"),
                Describe(transaction),
                transaction.Amount,
                transaction.Balance);
        }
    }

    /// <summary>What a transaction was for, as the statement reads it.</summary>
    public static string Describe(Transaction transaction) => transaction.Kind switch
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
        TransactionKind.Severance => $"Severance pay: {transaction.Subject}",
        TransactionKind.PersonalBonus => $"Staff bonus: {transaction.Subject}",
        TransactionKind.RemoveRoom => "Build: Remove destroyed room",
        _ => transaction.Kind.ToString(),
    };
}
