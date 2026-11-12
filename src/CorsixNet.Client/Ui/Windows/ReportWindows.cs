using System.Globalization;
using CorsixNet.Client.Maps;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The cheats window (CorsixTH's <c>dialogs/resizables/cheats_dialog.lua</c>, F11): each cheat as a button, the
/// switches showing whether they are on. Using one marks the hospital as having cheated, as in CorsixTH.
/// </summary>
internal sealed class CheatsWindow : UiWindow
{
    public override string Title => "Cheats";

    public override string? Hotkey => "F11";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var hospital = host.Simulation.Hospitals[host.Player];
        yield return hospital.Cheated ? WindowCell.Line("This hospital has cheated.", highlight: true) : WindowCell.Line("Cheats are marked on the hospital once used.", dim: true);
        yield return WindowRow.Of(Cheat(host, "Money +$10,000", CheatKind.Money), Cheat(host, "All research", CheatKind.AllResearch), Cheat(host, "Create patient", CheatKind.CreatePatient));
        yield return WindowRow.Of(Cheat(host, "VIP", CheatKind.Vip), Cheat(host, "Emergency", CheatKind.Emergency), Cheat(host, "Epidemic", CheatKind.Epidemic));
        yield return WindowRow.Of(Cheat(host, "Earthquake", CheatKind.Earthquake), Cheat(host, "Earthquakes on/off", CheatKind.ToggleEarthquakes), Cheat(host, "Epidemics on/off", CheatKind.ToggleEpidemics));
        yield return WindowRow.Of(Cheat(host, "Prices +50 %", CheatKind.IncreasePrices), Cheat(host, "Prices -50 %", CheatKind.DecreasePrices), Cheat(host, "Max reputation", CheatKind.MaxReputation));
        yield return WindowRow.Of(Cheat(host, "Reset deaths", CheatKind.ResetDeathCount), Cheat(host, "End month", CheatKind.EndMonth), Cheat(host, "End year", CheatKind.EndYear));
        yield return WindowRow.Of(
            Toggle(host, "Roujin's challenge", CheatKind.ToggleSpawnRate, hospital.ActiveCheats.HasFlag(ToggleCheats.SpawnRate)),
            Toggle(host, "No rest", CheatKind.ToggleNoRest, hospital.ActiveCheats.HasFlag(ToggleCheats.NoRest)),
            Toggle(host, "Queue jump", CheatKind.ToggleQueueJump, hospital.ActiveCheats.HasFlag(ToggleCheats.QueueJump)));
        yield return WindowRow.Of(
            Toggle(host, "Super doctors", CheatKind.ToggleSuperDoctor, hospital.ActiveCheats.HasFlag(ToggleCheats.SuperDoctor)),
            Cheat(host, "Win level", CheatKind.WinLevel),
            Cheat(host, "Lose level", CheatKind.LoseLevel));
        yield return WindowRow.Of(
            Cheat(host, "Repair all machines", CheatKind.RepairAllMachines),
            Toggle(host, "Invulnerable machines", CheatKind.ToggleInvulnerableMachines, hospital.ActiveCheats.HasFlag(ToggleCheats.InvulnerableMachines)));
    }

    /// <summary>The cheats' tooltips (<c>tooltip.cheats_window.cheats</c>); the fax code toggles have our own.</summary>
    internal static string Tooltip(CheatKind cheat) => cheat switch
    {
        CheatKind.Money => "Adds $10,000 to your bank balance",
        CheatKind.AllResearch => "Completes all research",
        CheatKind.Emergency => "Creates an emergency",
        CheatKind.Vip => "Creates a VIP",
        CheatKind.ToggleEarthquakes => "Toggle earthquakes",
        CheatKind.Earthquake => "Creates an earthquake",
        CheatKind.ToggleEpidemics => "Toggle whether epidemics can happen",
        CheatKind.Epidemic => "Creates a contagious patient who may cause an epidemic to happen",
        CheatKind.CreatePatient => "Creates a Patient at the map border",
        CheatKind.EndMonth => "Jumps to the end of the month",
        CheatKind.EndYear => "Jumps to the end of the year",
        CheatKind.LoseLevel => "Lose the current level",
        CheatKind.WinLevel => "Win the current level",
        CheatKind.IncreasePrices => "Increase all prices by 50% (max. 200%)",
        CheatKind.DecreasePrices => "Decrease all prices by 50% (min. 50%)",
        CheatKind.ResetDeathCount => "Reset the hospital death count to zero",
        CheatKind.MaxReputation => "Set the hospital reputation to the maximum",
        CheatKind.RepairAllMachines => "Repairs all machines in the hospital",
        CheatKind.ToggleInvulnerableMachines => "Toggle whether machines wear down when used",
        CheatKind.ToggleSpawnRate => "Toggle Roujin's challenge: many more patients arrive",
        CheatKind.ToggleNoRest => "Toggle whether staff never get tired",
        CheatKind.ToggleQueueJump => "Toggle whether new patients go to the front of every queue",
        CheatKind.ToggleSuperDoctor => "Toggle whether doctors have the best skills",
        _ => cheat.ToString(),
    };

    private static WindowCell Cheat(IWindowHost host, string text, CheatKind cheat) =>
        WindowCell.Button(text, () => Use(host, text, cheat)).WithTooltip(Tooltip(cheat));

    private static void Use(IWindowHost host, string text, CheatKind cheat)
    {
        host.Submit(new CheatCommand(cheat));
        host.Notice($"Cheat: {text}");
    }

    private static WindowCell Toggle(IWindowHost host, string text, CheatKind cheat, bool on) =>
        WindowCell.Button(on ? $"{text}: on" : $"{text}: off", () => host.Submit(new CheatCommand(cheat)), highlight: on).WithTooltip(Tooltip(cheat));
}

/// <summary>
/// The progress report (CorsixTH's <c>dialogs/fullscreen/progress_report.lua</c>, F7): the goals of the level the
/// hospital is closest to, and the lose goals it is near, with where it stands.
/// </summary>
internal sealed class ProgressReportWindow : UiWindow
{
    public override string Title => "Progress report";

    public override string? Hotkey => "F7";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var simulation = host.Simulation;
        var hospital = simulation.Hospitals[host.Player];
        var report = simulation.Rules.EndConditions.ProgressReport(simulation, hospital);
        if (report.Count == 0)
        {
            yield return WindowCell.Line("This level has no goals.", dim: true);
            yield break;
        }

        yield return WindowRow.Of("Goal", WindowCell.Number("Now"), WindowCell.Number("Target"), string.Empty);
        foreach (var entry in report)
        {
            var goal = entry.Goal;
            var now = EndConditions.Measure(simulation, hospital, goal.Criterion);
            var met = now is { } value && (goal.AtLeast ? value >= goal.Value : value <= goal.Value);
            yield return WindowRow.Of(
                new WindowCell(Name(goal.Criterion), Highlight: entry.IsLoseGoal),
                WindowCell.Number(now is { } shown ? Format(goal.Criterion, shown) : "-"),
                WindowCell.Number((entry.IsLoseGoal ? (goal.AtLeast ? "lose above " : "lose below ") : goal.AtLeast ? "at least " : "at most ") + Format(goal.Criterion, goal.Value)),
                entry.IsLoseGoal ? string.Create(CultureInfo.InvariantCulture, $"warning {(int)((entry.Progress ?? 0) * 100)} %") : met ? "met" : string.Empty);
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of("Cured", WindowCell.Number(hospital.Cured.ToString(CultureInfo.InvariantCulture)), "Died", WindowCell.Number(hospital.Deaths.ToString(CultureInfo.InvariantCulture)));
        yield return WindowRow.Of("Visitors", WindowCell.Number(hospital.Visitors.ToString(CultureInfo.InvariantCulture)), "Reputation", WindowCell.Number(hospital.Reputation.ToString(CultureInfo.InvariantCulture)));
    }

    public static string Name(EndCriterion criterion) => criterion switch
    {
        EndCriterion.Reputation => "Reputation",
        EndCriterion.Balance => "Bank balance",
        EndCriterion.PercentageCured => "Patients cured",
        EndCriterion.NumCured => "Number cured",
        EndCriterion.PercentageKilled => "Patients killed",
        EndCriterion.Value => "Hospital value",
        EndCriterion.StaffHappiness => "Staff happiness",
        EndCriterion.PatientHappiness => "Patient happiness",
        _ => "Months played",
    };

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
    public override string Title => "Annual report";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var hospital = host.Simulation.Hospitals[host.Player];
        if (hospital.LastYearAwards.Count == 0)
        {
            yield return WindowCell.Line("No trophies or awards this year.", dim: true);
        }
        else
        {
            yield return WindowRow.Of("Trophy or award", WindowCell.Number("Money"), WindowCell.Number("Reputation"));
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
        yield return WindowRow.Of("Hospital value", WindowCell.Number(MoneyText.Format(hospital.Value)));
        yield return WindowRow.Of("Your salary", WindowCell.Number(MoneyText.Format(hospital.PlayerSalary)));
        yield return WindowRow.Of("Reputation", WindowCell.Number(hospital.Reputation.ToString(CultureInfo.InvariantCulture)));
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
    public override string Title => "Level over";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var hospital = host.Simulation.Hospitals[host.Player];
        switch (hospital.Outcome)
        {
            case EndOutcome.Won:
                yield return WindowCell.Line("Congratulations! You have met every goal of this level.", highlight: true);
                if (hospital.SalaryOffer > 0)
                {
                    yield return WindowRow.Of("Your new salary", WindowCell.Number(MoneyText.Format(hospital.SalaryOffer)));
                }

                yield return WindowRow.Of(
                    WindowCell.Button("Next level", () => host.StartLevel(next: true), host.HasNextLevel).WithTooltip("Go on to the next level"),
                    WindowCell.Button("Stay here", () => host.Close(this)).WithTooltip("Keep playing this level"));
                break;
            case EndOutcome.Lost:
                yield return WindowCell.Line("You have lost this level.", highlight: true);
                if (hospital.LostTo is { } goal)
                {
                    yield return WindowRow.Of("Because of", ProgressReportWindow.Name(goal.Criterion));
                }

                yield return WindowRow.Of(WindowCell.Button("Restart the level", () => host.StartLevel(next: false)).WithTooltip("Play this level again from the start"));
                break;
            default:
                yield return WindowCell.Line("The level goes on.", dim: true);
                break;
        }
    }
}
