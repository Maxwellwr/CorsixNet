using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenWards.Client.Maps;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Economy;

namespace OpenWards.Client.Ui.Windows;

/// <summary>
/// The cheats window (CorsixTH's <c>dialogs/resizables/cheats_dialog.lua</c>, F11): each cheat as a button, the
/// switches showing whether they are on. Using one marks the hospital as having cheated, as in CorsixTH.
/// </summary>
internal sealed class CheatsWindow : UiWindow
{
    /// <summary>A window CorsixTH added to the game, drawn in its own look.</summary>
    public override bool UsesResizableLook => true;

    public override string Title => UiText.Get("cheats_window.caption", "Cheats");

    public override string? Hotkey => "F11";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var hospital = host.Simulation.Hospitals[host.Player];
        yield return hospital.Cheated ? WindowCell.Line(UiText.Get("cheats_window.cheated.yes", "This hospital has cheated."), highlight: true) : WindowCell.Line(UiText.Get("openwards.cheats.marked", "Cheats are marked on the hospital once used."), dim: true);
        yield return WindowRow.Of(Cheat(host, UiText.Get("cheats_window.cheats.money", "Money +$10,000"), CheatKind.Money), Cheat(host, UiText.Get("cheats_window.cheats.all_research", "All research"), CheatKind.AllResearch), Cheat(host, UiText.Get("cheats_window.cheats.create_patient", "Create patient"), CheatKind.CreatePatient));
        yield return WindowRow.Of(Cheat(host, UiText.Get("cheats_window.cheats.vip", "VIP"), CheatKind.Vip), Cheat(host, UiText.Get("cheats_window.cheats.emergency", "Emergency"), CheatKind.Emergency), Cheat(host, UiText.Get("cheats_window.cheats.epidemic", "Epidemic"), CheatKind.Epidemic));
        yield return WindowRow.Of(Cheat(host, UiText.Get("cheats_window.cheats.earthquake", "Earthquake"), CheatKind.Earthquake), Cheat(host, UiText.Get("cheats_window.cheats.toggle_earthquake", "Earthquakes on/off"), CheatKind.ToggleEarthquakes), Cheat(host, UiText.Get("cheats_window.cheats.toggle_epidemic", "Epidemics on/off"), CheatKind.ToggleEpidemics));
        yield return WindowRow.Of(Cheat(host, UiText.Get("cheats_window.cheats.increase_prices", "Prices +50 %"), CheatKind.IncreasePrices), Cheat(host, UiText.Get("cheats_window.cheats.decrease_prices", "Prices -50 %"), CheatKind.DecreasePrices), Cheat(host, UiText.Get("cheats_window.cheats.max_reputation", "Max reputation"), CheatKind.MaxReputation));
        yield return WindowRow.Of(Cheat(host, UiText.Get("cheats_window.cheats.reset_death_count", "Reset deaths"), CheatKind.ResetDeathCount), Cheat(host, UiText.Get("cheats_window.cheats.end_month", "End month"), CheatKind.EndMonth), Cheat(host, UiText.Get("cheats_window.cheats.end_year", "End year"), CheatKind.EndYear));
        yield return WindowRow.Of(
            Toggle(host, UiText.Get("openwards.cheats.roujin", "Roujin's challenge"), CheatKind.ToggleSpawnRate, hospital.ActiveCheats.HasFlag(ToggleCheats.SpawnRate)),
            Toggle(host, UiText.Get("openwards.cheats.no_rest", "No rest"), CheatKind.ToggleNoRest, hospital.ActiveCheats.HasFlag(ToggleCheats.NoRest)),
            Toggle(host, UiText.Get("openwards.cheats.queue_jump", "Queue jump"), CheatKind.ToggleQueueJump, hospital.ActiveCheats.HasFlag(ToggleCheats.QueueJump)));
        yield return WindowRow.Of(
            Toggle(host, UiText.Get("openwards.cheats.super_doctors", "Super doctors"), CheatKind.ToggleSuperDoctor, hospital.ActiveCheats.HasFlag(ToggleCheats.SuperDoctor)),
            Cheat(host, UiText.Get("cheats_window.cheats.win_level", "Win level"), CheatKind.WinLevel),
            Cheat(host, UiText.Get("cheats_window.cheats.lose_level", "Lose level"), CheatKind.LoseLevel));
        yield return WindowRow.Of(
            Cheat(host, UiText.Get("cheats_window.cheats.repair_all_machines", "Repair all machines"), CheatKind.RepairAllMachines),
            Toggle(host, UiText.Get("cheats_window.cheats.toggle_invulnerable_machines", "Invulnerable machines"), CheatKind.ToggleInvulnerableMachines, hospital.ActiveCheats.HasFlag(ToggleCheats.InvulnerableMachines)),
            Cheat(host, UiText.Get("cheats_window.cheats.show_infected", "Show infected"), CheatKind.ShowInfected));
    }

    /// <summary>The cheats' tooltips (<c>tooltip.cheats_window.cheats</c>); the fax code toggles have our own.</summary>
    internal static string Tooltip(CheatKind cheat) => cheat switch
    {
        CheatKind.Money => UiText.Get("tooltip.cheats_window.cheats.money", "Adds $10,000 to your bank balance"),
        CheatKind.AllResearch => UiText.Get("tooltip.cheats_window.cheats.all_research", "Completes all research"),
        CheatKind.Emergency => UiText.Get("tooltip.cheats_window.cheats.emergency", "Creates an emergency"),
        CheatKind.Vip => UiText.Get("tooltip.cheats_window.cheats.vip", "Creates a VIP"),
        CheatKind.ToggleEarthquakes => UiText.Get("tooltip.cheats_window.cheats.toggle_earthquake", "Toggle earthquakes"),
        CheatKind.Earthquake => UiText.Get("tooltip.cheats_window.cheats.earthquake", "Creates an earthquake"),
        CheatKind.ToggleEpidemics => UiText.Get("tooltip.cheats_window.cheats.toggle_epidemic", "Toggle whether epidemics can happen"),
        CheatKind.Epidemic => UiText.Get("tooltip.cheats_window.cheats.epidemic", "Creates a contagious patient who may cause an epidemic to happen"),
        CheatKind.CreatePatient => UiText.Get("tooltip.cheats_window.cheats.create_patient", "Creates a Patient at the map border"),
        CheatKind.EndMonth => UiText.Get("tooltip.cheats_window.cheats.end_month", "Jumps to the end of the month"),
        CheatKind.EndYear => UiText.Get("tooltip.cheats_window.cheats.end_year", "Jumps to the end of the year"),
        CheatKind.LoseLevel => UiText.Get("tooltip.cheats_window.cheats.lose_level", "Lose the current level"),
        CheatKind.WinLevel => UiText.Get("tooltip.cheats_window.cheats.win_level", "Win the current level"),
        CheatKind.IncreasePrices => UiText.Get("tooltip.cheats_window.cheats.increase_prices", "Increase all prices by 50% (max. 200%)"),
        CheatKind.DecreasePrices => UiText.Get("tooltip.cheats_window.cheats.decrease_prices", "Decrease all prices by 50% (min. 50%)"),
        CheatKind.ResetDeathCount => UiText.Get("tooltip.cheats_window.cheats.reset_death_count", "Reset the hospital death count to zero"),
        CheatKind.MaxReputation => UiText.Get("tooltip.cheats_window.cheats.max_reputation", "Set the hospital reputation to the maximum"),
        CheatKind.RepairAllMachines => UiText.Get("tooltip.cheats_window.cheats.repair_all_machines", "Repairs all machines in the hospital"),
        CheatKind.ToggleInvulnerableMachines => UiText.Get("tooltip.cheats_window.cheats.toggle_invulnerable_machines", "Toggle whether machines wear down when used"),
        CheatKind.ShowInfected => UiText.Get("tooltip.cheats_window.cheats.show_infected", "Shows all infected patients' contagious icon"),
        CheatKind.ToggleSpawnRate => UiText.Get("openwards.cheats.roujin_tooltip", "Toggle Roujin's challenge: many more patients arrive"),
        CheatKind.ToggleNoRest => UiText.Get("openwards.cheats.no_rest_tooltip", "Toggle whether staff never get tired"),
        CheatKind.ToggleQueueJump => UiText.Get("openwards.cheats.queue_jump_tooltip", "Toggle whether new patients go to the front of every queue"),
        CheatKind.ToggleSuperDoctor => UiText.Get("openwards.cheats.super_doctors_tooltip", "Toggle whether doctors have the best skills"),
        _ => cheat.ToString(),
    };

    private static WindowCell Cheat(IWindowHost host, string text, CheatKind cheat) =>
        WindowCell.Button(text, () => Use(host, text, cheat)).WithTooltip(Tooltip(cheat));

    private static void Use(IWindowHost host, string text, CheatKind cheat)
    {
        host.Submit(new CheatCommand(cheat));
        host.Notice(UiText.Format("openwards.cheats.used", "Cheat: %1%", text));
    }

    private static WindowCell Toggle(IWindowHost host, string text, CheatKind cheat, bool on) =>
        WindowCell.Button(on ? UiText.Format("openwards.cheats.toggle_on", "%1%: on", text) : UiText.Format("openwards.cheats.toggle_off", "%1%: off", text), () => host.Submit(new CheatCommand(cheat)), highlight: on).WithTooltip(Tooltip(cheat));
}

/// <summary>
/// The progress report (CorsixTH's <c>dialogs/fullscreen/progress_report.lua</c>, F7): the goals of the level the
/// hospital is closest to, and the lose goals it is near, with where it stands.
/// </summary>
internal sealed class ProgressReportWindow : UiWindow
{
    public override string Title => UiText.Get("openwards.progress.caption", "Progress report");

    public override string? Hotkey => "F7";

    private readonly ProgressReportScreen _screen = new();

    /// <summary>The original's look (<see cref="ProgressReportScreen"/>) where its graphics are there; else the table below.</summary>
    public override bool IsFullscreen(IWindowHost host) => ProgressReportScreen.IsAvailable(host);

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse) => _screen.Draw(batch, host, origin, mouse);

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous) => _screen.Mouse(host, this, local, mouse, previous);

    public override string? FullscreenTooltip(IWindowHost host, Point local) => ProgressReportScreen.Tooltip(host, local);
    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var simulation = host.Simulation;
        var hospital = simulation.Hospitals[host.Player];
        var report = simulation.Rules.EndConditions.ProgressReport(simulation, hospital);
        if (report.Count == 0)
        {
            yield return WindowCell.Line(UiText.Get("openwards.progress.no_goals", "This level has no goals."), dim: true);
            yield break;
        }

        yield return WindowRow.Of(UiText.Get("openwards.progress.goal", "Goal"), WindowCell.Number(UiText.Get("openwards.progress.now", "Now")), WindowCell.Number(UiText.Get("openwards.progress.target", "Target")), string.Empty);
        foreach (var entry in report)
        {
            var goal = entry.Goal;
            var now = EndConditions.Measure(simulation, hospital, goal.Criterion);
            var met = now is { } value && (goal.AtLeast ? value >= goal.Value : value <= goal.Value);
            yield return WindowRow.Of(
                new WindowCell(Name(goal.Criterion), Highlight: entry.IsLoseGoal).WithTooltip(Tooltip(host, goal.Criterion, entry.IsLoseGoal, goal.Value, now ?? 0)),
                WindowCell.Number(now is { } shown ? Format(goal.Criterion, shown) : "-"),
                WindowCell.Number(UiText.Format(
                    entry.IsLoseGoal ? (goal.AtLeast ? "openwards.progress.lose_above" : "openwards.progress.lose_below") : goal.AtLeast ? "openwards.progress.at_least" : "openwards.progress.at_most",
                    entry.IsLoseGoal ? (goal.AtLeast ? "lose above %1%" : "lose below %1%") : goal.AtLeast ? "at least %1%" : "at most %1%",
                    Format(goal.Criterion, goal.Value))),
                entry.IsLoseGoal ? UiText.Format("openwards.progress.warning", "warning %1% %%", (int)((entry.Progress ?? 0) * 100)) : met ? UiText.Get("openwards.progress.met", "met") : string.Empty);
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(UiText.Get("openwards.casebook.cured", "Cured"), WindowCell.Number(hospital.Cured.ToString(CultureInfo.InvariantCulture)), UiText.Get("openwards.casebook.died", "Died"), WindowCell.Number(hospital.Deaths.ToString(CultureInfo.InvariantCulture)));
        yield return WindowRow.Of(UiText.Get("graphs.visitors", "Visitors"), WindowCell.Number(hospital.Visitors.ToString(CultureInfo.InvariantCulture)), UiText.Get("graphs.reputation", "Reputation"), WindowCell.Number(hospital.Reputation.ToString(CultureInfo.InvariantCulture)));
    }

    public static string Name(EndCriterion criterion) => criterion switch
    {
        EndCriterion.Reputation => UiText.Get("graphs.reputation", "Reputation"),
        EndCriterion.Balance => UiText.Get("openwards.progress.bank_balance", "Bank balance"),
        EndCriterion.PercentageCured => UiText.Get("openwards.progress.patients_cured", "Patients cured"),
        EndCriterion.NumCured => UiText.Get("openwards.progress.number_cured", "Number cured"),
        EndCriterion.PercentageKilled => UiText.Get("openwards.progress.patients_killed", "Patients killed"),
        EndCriterion.Value => UiText.Get("bank_manager.hospital_value", "Hospital value"),
        EndCriterion.StaffHappiness => UiText.Get("openwards.progress.staff_happiness", "Staff happiness"),
        EndCriterion.PatientHappiness => UiText.Get("openwards.progress.patient_happiness", "Patient happiness"),
        _ => UiText.Get("openwards.progress.months_played", "Months played"),
    };

    /// <summary>
    /// A goal's tooltip (<c>UIProgressReport</c>, <c>tooltip.status</c>): the target and where the hospital stands. The
    /// happiness and months goals say "over" for a goal to win and "under" for one to lose by; months that make whole
    /// years are told as years. The original's strings are used when loaded (section 64); the stand-ins for those
    /// english.lua does not correct are ours.
    /// </summary>
    internal static string Tooltip(IWindowHost host, EndCriterion criterion, bool isLoseGoal, decimal target, decimal now)
    {
        var goal = (long)Math.Floor(target);
        var current = (long)Math.Floor(now);
        string Original(int index, string fallback) => string.Format(CultureInfo.InvariantCulture, ToComposite(host.Texts?.String(64, index) ?? fallback), goal, current);
        var over = !isLoseGoal;
        return criterion switch
        {
            EndCriterion.Reputation => UiText.Format("tooltip.status.reputation", "Your Reputation should not be less than %d. Currently it's %d", goal, current),
            EndCriterion.Balance => UiText.Format("tooltip.status.balance", "Your bank balance should not be less than %d. Currently it's %d", goal, current),
            EndCriterion.NumCured => Original(4, "You need to cure %d patients. Currently you have cured %d"),
            EndCriterion.PercentageKilled => Original(5, "You should kill less than %d% of your patients. Currently you have killed %d%"),
            EndCriterion.Value => Original(6, "Your hospital should be worth at least %d. Currently it's worth %d"),
            EndCriterion.PercentageCured => Original(7, "You need to cure %d% of the patients who come to your hospital. Currently you have cured %d%"),
            EndCriterion.StaffHappiness => over
                ? UiText.Format("tooltip.status.over.staff_happiness", "Your average staff happiness should be over %d%. Currently it's %d%", goal, current)
                : UiText.Format("tooltip.status.under.staff_happiness", "Your average staff happiness should not be less than %d%. Currently it's %d%", goal, current),
            EndCriterion.PatientHappiness => over
                ? UiText.Format("tooltip.status.over.patient_happiness", "Your average patient happiness should be over %d%. Currently it's %d%", goal, current)
                : UiText.Format("tooltip.status.under.patient_happiness", "Your average patient happiness should not be less than %d%. Currently it's %d%", goal, current),
            _ => goal % 12 == 0
                ? UiText.Format(over ? "tooltip.status.over.years_played" : "tooltip.status.under.years_played", "You need to reach the year %d. Currently it's the year %d", (goal / 12) + 2000, ((current - 1) / 12) + 2000)
                : over
                    ? UiText.Format("tooltip.status.over.months_played", "You need to reach the end of %s %d. Currently it's %s %d", Month(host, goal), (goal / 12) + 2000, Month(host, current), ((current - 1) / 12) + 2000)
                    : UiText.Format("tooltip.status.under.months_played", "You need to win by the end of %s %d. Currently it's %s %d", Month(host, goal), (goal / 12) + 2000, Month(host, current), ((current - 1) / 12) + 2000),
        };
    }

    /// <summary>A Lua <c>%d</c> template as a .NET composite format with two arguments.</summary>
    private static string ToComposite(string template)
    {
        var escaped = template.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);
        var first = escaped.IndexOf("%d", StringComparison.Ordinal);
        if (first < 0)
        {
            return escaped;
        }

        escaped = string.Concat(escaped.AsSpan(0, first), "{0}", escaped.AsSpan(first + 2));
        var second = escaped.IndexOf("%d", StringComparison.Ordinal);
        return second < 0 ? escaped : string.Concat(escaped.AsSpan(0, second), "{1}", escaped.AsSpan(second + 2));
    }

    /// <summary><c>get_month_string</c>: the month's name (<c>_S.months</c>, section 6), month 12 for whole years.</summary>
    private static string Month(IWindowHost host, long months)
    {
        var month = (int)(months % 12);
        if (month <= 0)
        {
            month += 12;
        }

        return host.Texts?.String(6, month) ?? CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);
    }

    private static string Format(EndCriterion criterion, decimal value) => criterion switch
    {
        EndCriterion.Balance or EndCriterion.Value => MoneyText.Format((long)value),
        EndCriterion.PercentageCured or EndCriterion.PercentageKilled or EndCriterion.StaffHappiness or EndCriterion.PatientHappiness => string.Create(CultureInfo.InvariantCulture, $"{(int)value} %"),
        _ => ((long)value).ToString(CultureInfo.InvariantCulture),
    };
}

/// <summary>
/// The annual report's awards (CorsixTH's <c>dialogs/fullscreen/annual_report.lua</c>): the trophies, awards and
/// fines of the year that ended, with their money and reputation, and the year's figures. It opens at the year end.
/// </summary>
internal sealed class AnnualReportWindow : UiWindow
{
    public override string Title => UiText.Get("openwards.menu.annual_report", "Annual report");

    private readonly AnnualReportScreen _screen = new();

    /// <summary>The original's look (<see cref="AnnualReportScreen"/>) where its graphics are there; else the table below.</summary>
    public override bool IsFullscreen(IWindowHost host) => AnnualReportScreen.IsAvailable(host);

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse) => _screen.Draw(batch, host, origin, mouse);

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous) => _screen.Mouse(host, this, local, mouse, previous);
    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var hospital = host.Simulation.Hospitals[host.Player];
        if (hospital.LastYearAwards.Count == 0)
        {
            yield return WindowCell.Line(UiText.Get("openwards.progress.no_awards", "No trophies or awards this year."), dim: true);
        }
        else
        {
            yield return WindowRow.Of(UiText.Get("openwards.progress.award", "Trophy or award"), WindowCell.Number(UiText.Get("openwards.progress.money", "Money")), WindowCell.Number(UiText.Get("graphs.reputation", "Reputation")));
            foreach (var award in hospital.LastYearAwards)
            {
                var bad = award.Money < 0 || award.Reputation < 0;
                yield return WindowRow.Of(
                    new WindowCell(Describe(award.Kind), Highlight: !bad, Dim: bad),
                    WindowCell.Number(award.Money == 0 ? string.Empty : MoneyText.Format(award.Money)),
                    WindowCell.Number(award.Reputation == 0 ? string.Empty : award.Reputation.ToString("+0;-0", CultureInfo.InvariantCulture)));
            }
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(UiText.Get("bank_manager.hospital_value", "Hospital value"), WindowCell.Number(MoneyText.Format(hospital.Value)));
        yield return WindowRow.Of(UiText.Get("openwards.progress.your_salary", "Your salary"), WindowCell.Number(MoneyText.Format(hospital.PlayerSalary)));
        yield return WindowRow.Of(UiText.Get("graphs.reputation", "Reputation"), WindowCell.Number(hospital.Reputation.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>The award's name: its kind's name, split into words.</summary>
    private static string Describe(YearEndAwardKind kind)
    {
        var name = kind.ToString();
        var words = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && words.Length > 0)
            {
                words.Append(' ');
                words.Append(char.ToLowerInvariant(c));
            }
            else
            {
                words.Append(c);
            }
        }

        return words.ToString();
    }
}

/// <summary>
/// The level's end (CorsixTH's winning letter and losing fax in <c>world.lua</c>): won, with the salary offered and
/// the next level; or lost, and why, with a restart.
/// </summary>
internal sealed class LevelEndWindow : UiWindow
{
    /// <summary>
    /// World:loseGame's message (<c>information.level_lost</c>): the level is lost, the reason, and the goal passed with
    /// its limit; or, lost by the cheat, the cheat's line.
    /// </summary>
    public static IReadOnlyList<string> LostLines(OpenWards.Content.OriginalTexts? texts, EndGoal? goal)
    {
        string Named(string key, string fallback) => texts?.Named("information.level_lost." + key) ?? fallback;
        var lines = new List<string> { Named("1", "Bummer! You failed the level. Better luck next time!") };
        if (goal is not { } lost)
        {
            lines.Add(Named("cheat", "Hope you didn't click the Lose Level button by accident!"));
            return lines;
        }

        lines.Add(Named("2", "The reason you lost:"));
        var (key, fallback) = lost.Criterion switch
        {
            EndCriterion.Reputation => ("reputation", "Your reputation fell below %d."),
            EndCriterion.Balance => ("balance", "Your bank balance fell below %d."),
            EndCriterion.PercentageKilled => ("percentage_killed", "You killed more than %d percent of the patients."),
            EndCriterion.StaffHappiness => ("staff_happiness", "Your average staff happiness fell below %d%."),
            EndCriterion.PatientHappiness => ("patient_happiness", "Your average patient happiness fell below %d%."),
            EndCriterion.MonthsPlayed => ("months_played", "You didn't meet the level goals within %d months."),
            _ => (string.Empty, string.Empty),
        };
        if (key.Length > 0)
        {
            lines.Add(Named(key, fallback).Replace("%d", lost.Value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        }

        return lines;
    }

    public override string Title => UiText.Get("openwards.progress.level_over", "Level over");

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var hospital = host.Simulation.Hospitals[host.Player];
        switch (hospital.Outcome)
        {
            case EndOutcome.Won:
                yield return WindowCell.Line(UiText.Get("openwards.progress.won", "Congratulations! You have met every goal of this level."), highlight: true);
                if (hospital.SalaryOffer > 0)
                {
                    yield return WindowRow.Of(UiText.Get("openwards.progress.new_salary", "Your new salary"), WindowCell.Number(MoneyText.Format(hospital.SalaryOffer)));
                }

                yield return WindowRow.Of(
                    WindowCell.Button(UiText.Get("openwards.progress.next_level", "Next level"), () => host.StartLevel(next: true), host.HasNextLevel).WithTooltip(UiText.Get("openwards.progress.next_level_tooltip", "Go on to the next level")),
                    WindowCell.Button(UiText.Get("openwards.progress.stay", "Stay here"), () => host.Close(this)).WithTooltip(UiText.Get("openwards.progress.stay_tooltip", "Keep playing this level")));
                break;
            case EndOutcome.Lost:
                yield return WindowCell.Line(UiText.Get("openwards.progress.lost", "You have lost this level."), highlight: true);
                if (hospital.LostTo is { } goal)
                {
                    yield return WindowRow.Of(UiText.Get("openwards.progress.because_of", "Because of"), ProgressReportWindow.Name(goal.Criterion));
                }

                yield return WindowRow.Of(WindowCell.Button(UiText.Get("openwards.progress.restart", "Restart the level"), () => host.StartLevel(next: false)).WithTooltip(UiText.Get("openwards.progress.restart_tooltip", "Play this level again from the start")));
                break;
            default:
                yield return WindowCell.Line(UiText.Get("openwards.progress.goes_on", "The level goes on."), dim: true);
                break;
        }
    }
}
