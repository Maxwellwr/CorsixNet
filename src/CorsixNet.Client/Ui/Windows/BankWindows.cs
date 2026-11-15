using System.Globalization;
using CorsixNet.Client.Maps;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The bank manager (CorsixTH's <c>dialogs/fullscreen/bank_manager.lua</c>, F1): balance, the loan and its limit, the
/// interest rate, the hospital's value and what the insurance companies owe; borrowing and paying back 5,000 at a time,
/// or as much as allowed (the buttons with Ctrl).
/// </summary>
internal sealed class BankManagerWindow : UiWindow
{
    public override string Title => UiText.Get("corsixnet.bank.bank_manager_caption", "Bank manager");

    public override string? Hotkey => "F1";

    private readonly BankManagerScreen _screen = new();

    /// <summary>The original's look (<see cref="BankManagerScreen"/>) where its graphics are there; else the table below.</summary>
    public override bool IsFullscreen(IWindowHost host) => BankManagerScreen.IsAvailable(host);

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse) => _screen.Draw(batch, host, origin, mouse);

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous) => _screen.Mouse(host, this, local, mouse, previous);

    public override string? FullscreenTooltip(IWindowHost host, Point local) => _screen.Tooltip(host, local);

    public override GameCursor? FullscreenCursor(IWindowHost host, Point local) => _screen.Cursor(local);
    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var hospital = host.Simulation.Hospitals[host.Player];
        yield return WindowRow.Of(UiText.Get("bank_manager.balance", "Balance"), WindowCell.Number(MoneyText.Format(hospital.Balance)));
        yield return WindowRow.Of(UiText.Get("bank_manager.hospital_value", "Hospital value"), WindowCell.Number(MoneyText.Format(hospital.Value)));
        yield return WindowRow.Of(UiText.Get("bank_manager.current_loan", "Current loan"), WindowCell.Number(MoneyText.Format(hospital.Loan)));
        yield return WindowRow.Of(UiText.Get("corsixnet.bank.most_you_can_borrow", "Most you can borrow"), WindowCell.Number(MoneyText.Format(hospital.MaxLoan)));
        yield return WindowRow.Of(UiText.Get("corsixnet.bank.interest_a_year", "Interest a year"), WindowCell.Number(string.Create(CultureInfo.InvariantCulture, $"{hospital.InterestRate / 100m:0.##} %")));
        yield return WindowRow.Gap;
        var canBorrow = hospital.Loan < hospital.MaxLoan;
        var canRepay = hospital.Loan > 0 && hospital.Balance >= Math.Min(Hospital.LoanStep, hospital.Loan);
        yield return WindowRow.Of(
            WindowCell.Button(UiText.Format("corsixnet.bank.borrow", "Borrow %1%", MoneyText.Format(Hospital.LoanStep)), () => Loan(host, new BorrowCommand(false)), canBorrow).WithTooltip(OriginalTooltips.Get(host, 214, "Borrow $5,000 from the bank")),
            WindowCell.Button(UiText.Get("corsixnet.bank.borrow_all", "Borrow all allowed"), () => Loan(host, new BorrowCommand(true)), canBorrow).WithTooltip(OriginalTooltips.Get(host, 214, "Borrow $5,000 from the bank")));
        yield return WindowRow.Of(
            WindowCell.Button(UiText.Format("corsixnet.bank.repay", "Repay %1%", MoneyText.Format(Hospital.LoanStep)), () => Loan(host, new RepayCommand(false)), canRepay).WithTooltip(OriginalTooltips.Get(host, 213, "Repay $5,000 of the loan")),
            WindowCell.Button(UiText.Get("corsixnet.bank.repay_all", "Repay all you can"), () => Loan(host, new RepayCommand(true)), canRepay).WithTooltip(OriginalTooltips.Get(host, 213, "Repay $5,000 of the loan")));
        yield return WindowRow.Gap;
        yield return WindowCell.Line(UiText.Get("corsixnet.bank.insurance_owed", "Insurance companies owe"), highlight: true);
        for (var company = 1; company <= Hospital.InsuranceCompanies; company++)
        {
            yield return WindowRow.Of(MoneyPanels.InsuranceCompanyName(host.Texts, hospital, company) ?? UiText.Format("corsixnet.bank.company", "Company %1%", company), WindowCell.Number(MoneyText.Format(hospital.InsuranceOwed(company))));
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
    public override string Title => UiText.Get("corsixnet.bank.statement_caption", "Bank statement");

    public override string? Hotkey => "F2";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var hospital = host.Simulation.Hospitals[host.Player];
        yield return WindowRow.Of(UiText.Get("bank_manager.balance", "Balance"), WindowCell.Number(MoneyText.Format(hospital.Balance)), UiText.Get("corsixnet.bank.loan", "Loan"), WindowCell.Number(MoneyText.Format(hospital.Loan)));
        yield return WindowRow.Gap;
        yield return WindowRow.Of(UiText.Get("bank_manager.statistics_page.date", "Date"), UiText.Get("bank_manager.statistics_page.details", "Details"), WindowCell.Number(UiText.Get("bank_manager.statistics_page.money_out", "Money out")), WindowCell.Number(UiText.Get("bank_manager.statistics_page.money_in", "Money in")), WindowCell.Number(UiText.Get("bank_manager.balance", "Balance")));
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
    public override string Title => UiText.Get("corsixnet.bank.graph_figures_caption", "Graph figures");

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var statistics = host.Simulation.Hospitals[host.Player].Statistics;
        yield return WindowRow.Of(UiText.Get("corsixnet.bank.month", "Month"), WindowCell.Number(UiText.Get("corsixnet.bank.in", "In")), WindowCell.Number(UiText.Get("corsixnet.bank.out", "Out")), WindowCell.Number(UiText.Get("corsixnet.bank.wages", "Wages")), WindowCell.Number(UiText.Get("bank_manager.balance", "Balance")), WindowCell.Number(UiText.Get("corsixnet.bank.visitors", "Visitors")), WindowCell.Number(UiText.Get("corsixnet.bank.cures", "Cures")), WindowCell.Number(UiText.Get("corsixnet.bank.deaths", "Deaths")), WindowCell.Number(UiText.Get("corsixnet.bank.reputation", "Rep")));
        for (var i = statistics.Count - 1; i >= Math.Max(0, statistics.Count - MoneyPanels.GraphMonths); i--)
        {
            var month = statistics[i];
            yield return WindowRow.Of(
                i == 0 ? UiText.Get("corsixnet.bank.start", "Start") : UiText.Format("corsixnet.bank.month_number", "Month %1%", i),
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
