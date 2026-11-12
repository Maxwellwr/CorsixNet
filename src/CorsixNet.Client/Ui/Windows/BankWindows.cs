using System.Globalization;
using CorsixNet.Client.Maps;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The bank manager (CorsixTH's <c>dialogs/fullscreen/bank_manager.lua</c>, F1): balance, the loan and its limit, the
/// interest rate, the hospital's value and what the insurance companies owe; borrowing and paying back 5,000 at a time,
/// or as much as allowed (the buttons with Ctrl).
/// </summary>
internal sealed class BankManagerWindow : UiWindow
{
    public override string Title => "Bank manager";

    public override string? Hotkey => "F1";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var hospital = host.Simulation.Hospitals[host.Player];
        yield return WindowRow.Of("Balance", WindowCell.Number(MoneyText.Format(hospital.Balance)));
        yield return WindowRow.Of("Hospital value", WindowCell.Number(MoneyText.Format(hospital.Value)));
        yield return WindowRow.Of("Current loan", WindowCell.Number(MoneyText.Format(hospital.Loan)));
        yield return WindowRow.Of("Most you can borrow", WindowCell.Number(MoneyText.Format(hospital.MaxLoan)));
        yield return WindowRow.Of("Interest a year", WindowCell.Number(string.Create(CultureInfo.InvariantCulture, $"{hospital.InterestRate / 100m:0.##} %")));
        yield return WindowRow.Gap;
        var canBorrow = hospital.Loan < hospital.MaxLoan;
        var canRepay = hospital.Loan > 0 && hospital.Balance >= Math.Min(Hospital.LoanStep, hospital.Loan);
        yield return WindowRow.Of(
            WindowCell.Button(string.Create(CultureInfo.InvariantCulture, $"Borrow {MoneyText.Format(Hospital.LoanStep)}"), () => Loan(host, new BorrowCommand(false)), canBorrow).WithTooltip(OriginalTooltips.Get(host, 214, "Borrow $5,000 from the bank")),
            WindowCell.Button("Borrow all allowed", () => Loan(host, new BorrowCommand(true)), canBorrow).WithTooltip(OriginalTooltips.Get(host, 214, "Borrow $5,000 from the bank")));
        yield return WindowRow.Of(
            WindowCell.Button(string.Create(CultureInfo.InvariantCulture, $"Repay {MoneyText.Format(Hospital.LoanStep)}"), () => Loan(host, new RepayCommand(false)), canRepay).WithTooltip(OriginalTooltips.Get(host, 213, "Repay $5,000 of the loan")),
            WindowCell.Button("Repay all you can", () => Loan(host, new RepayCommand(true)), canRepay).WithTooltip(OriginalTooltips.Get(host, 213, "Repay $5,000 of the loan")));
        yield return WindowRow.Gap;
        yield return WindowCell.Line("Insurance companies owe", highlight: true);
        for (var company = 1; company <= Hospital.InsuranceCompanies; company++)
        {
            yield return WindowRow.Of(string.Create(CultureInfo.InvariantCulture, $"Company {company}"), WindowCell.Number(MoneyText.Format(hospital.InsuranceOwed(company))));
        }
    }

    private static void Loan(IWindowHost host, Command command)
    {
        host.Submit(command);
        host.PlaySound("cashreg.wav");
    }
}

/// <summary>
/// The bank statement (CorsixTH's bank manager's statement page, F2): the transaction log, latest first, with the
/// balance after each.
/// </summary>
internal sealed class BankStatementWindow : UiWindow
{
    public override string Title => "Bank statement";

    public override string? Hotkey => "F2";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var hospital = host.Simulation.Hospitals[host.Player];
        yield return WindowRow.Of("Balance", WindowCell.Number(MoneyText.Format(hospital.Balance)), "Loan", WindowCell.Number(MoneyText.Format(hospital.Loan)));
        yield return WindowRow.Gap;
        yield return WindowRow.Of("Date", "Details", WindowCell.Number("Money out"), WindowCell.Number("Money in"), WindowCell.Number("Balance"));
        foreach (var line in MoneyPanels.StatementLines(hospital))
        {
            yield return WindowRow.Of(
                line.Date,
                line.What,
                WindowCell.Number(line.Amount < 0 ? MoneyText.Format(-line.Amount) : string.Empty),
                WindowCell.Number(line.Amount >= 0 ? MoneyText.Format(line.Amount) : string.Empty),
                WindowCell.Number(MoneyText.Format(line.Balance)));
        }
    }
}

/// <summary>The graphs' figures (CorsixTH's <c>dialogs/fullscreen/graphs.lua</c>) as a table, latest month first.</summary>
internal sealed class GraphFiguresWindow : UiWindow
{
    public override string Title => "Graph figures";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var statistics = host.Simulation.Hospitals[host.Player].Statistics;
        yield return WindowRow.Of("Month", WindowCell.Number("In"), WindowCell.Number("Out"), WindowCell.Number("Wages"), WindowCell.Number("Balance"), WindowCell.Number("Visitors"), WindowCell.Number("Cures"), WindowCell.Number("Deaths"), WindowCell.Number("Rep"));
        for (var i = statistics.Count - 1; i >= Math.Max(0, statistics.Count - MoneyPanels.GraphMonths); i--)
        {
            var month = statistics[i];
            yield return WindowRow.Of(
                i == 0 ? "Start" : string.Create(CultureInfo.InvariantCulture, $"Month {i}"),
                WindowCell.Number(MoneyText.Format(month.MoneyIn)),
                WindowCell.Number(MoneyText.Format(month.MoneyOut)),
                WindowCell.Number(MoneyText.Format(month.Wages)),
                WindowCell.Number(MoneyText.Format(month.Balance)),
                WindowCell.Number(month.Visitors.ToString(CultureInfo.InvariantCulture)),
                WindowCell.Number(month.Cures.ToString(CultureInfo.InvariantCulture)),
                WindowCell.Number(month.Deaths.ToString(CultureInfo.InvariantCulture)),
                WindowCell.Number(month.Reputation.ToString(CultureInfo.InvariantCulture)));
        }
    }
}
