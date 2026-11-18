using System.Globalization;
using OpenWards.Client.Ui;
using OpenWards.Sim.Calendar;
using OpenWards.Sim.Economy;

namespace OpenWards.Client.Maps;

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
                Describe(transaction, hospital),
                transaction.Amount,
                transaction.Balance);
        }
    }

    /// <summary>
    /// The name of the hospital's insurance company numbered <paramref name="company"/> (from 1): <c>hospital.insurance</c>,
    /// the original's S[20] string of the company it picked, or null without the game's strings.
    /// </summary>
    public static string? InsuranceCompanyName(OpenWards.Content.OriginalTexts? texts, Hospital hospital, int company) =>
        company is >= 1 and <= Hospital.InsuranceCompanies ? texts?.String(20, hospital.Insurers[company - 1] + 1) : null;

    /// <summary>What a transaction was for, as the statement reads it.</summary>
    public static string Describe(Transaction transaction, Hospital hospital) => transaction.Kind switch
    {
        TransactionKind.BuyObject => $"{UiText.Get("transactions.buy_object", "Buy")}: {UiText.ObjectName(transaction.Subject)}",
        TransactionKind.SellObject => $"{UiText.Get("transactions.sell_object", "Sell")}: {UiText.ObjectOrRoomName(transaction.Subject)}",
        TransactionKind.BuildRoom => $"{UiText.Get("transactions.build_room", "Build")}: {UiText.RoomName(transaction.Subject)}",
        TransactionKind.HireStaff => UiText.Get("transactions.hire_staff", "Hire staff"),
        TransactionKind.Wages => UiText.Get("transactions.wages", "Wages"),
        TransactionKind.Treatment => $"{UiText.Get("transactions.cure_colon", "Treatment:")} {UiText.Current?.DiseaseName(transaction.Subject) ?? transaction.Subject}",
        TransactionKind.Insurance => $"{UiText.Get("transactions.insurance_colon", "Insurance:")} {(int.TryParse(transaction.Subject, out var company) ? InsuranceCompanyName(UiText.Current, hospital, company) : null) ?? transaction.Subject}",
        TransactionKind.DrugCost => UiText.Get("transactions.drug_cost", "Drug costs"),
        TransactionKind.TrophyBonus => UiText.Get("transactions.eoy_trophy_bonus", "Trophy money"),
        TransactionKind.AwardBonus => UiText.Get("transactions.eoy_bonus_penalty", "Awards and fines"),
        TransactionKind.Loan => UiText.Get("transactions.bank_loan", "Bank loan"),
        TransactionKind.LoanRepayment => UiText.Get("transactions.loan_repayment", "Loan repayment"),
        TransactionKind.LoanInterest => UiText.Get("transactions.loan_interest", "Loan interest"),
        TransactionKind.Overdraft => UiText.Get("transactions.overdraft", "Overdraft"),
        TransactionKind.BuyLand => $"{UiText.Get("transactions.buy_land", "Buy parcel")} {transaction.Subject}",
        TransactionKind.Heating => UiText.Get("transactions.heating", "Heating"),
        TransactionKind.Cheat => UiText.Get("transactions.cheat", "Cheat"),
        TransactionKind.Severance => $"{UiText.Get("transactions.severance", "Severance pay")}: {transaction.Subject}",
        TransactionKind.PersonalBonus => $"{UiText.Get("transactions.personal_bonus", "Staff bonus")}: {transaction.Subject}",
        TransactionKind.RemoveRoom => UiText.Get("transactions.remove_room", "Build: Remove destroyed room"),
        _ => transaction.Kind.ToString(),
    };
}
