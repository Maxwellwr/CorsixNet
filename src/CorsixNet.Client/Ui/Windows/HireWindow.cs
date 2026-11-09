using System.Globalization;
using CorsixNet.Client.Maps;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The hire window (CorsixTH's <c>dialogs/hire_staff.lua</c>), Y here: the candidates of a class with name, skill,
/// wage, rank and description. Hiring one puts them on the cursor to be placed in the hospital (<c>UIPlaceStaff</c>),
/// if the hospital can afford a month's wage.
/// </summary>
internal sealed class HireWindow : UiWindow
{
    private StaffClass _category = StaffClass.Doctor;

    public override string Title => "Hire staff";

    public override string? Hotkey => "Y";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var simulation = host.Simulation;
        var hospital = simulation.Hospitals[host.Player];
        yield return new WindowRow(StaffClasses.HireOrder.Select(staffClass => WindowCell.Button(
            string.Create(CultureInfo.InvariantCulture, $"{Plural(staffClass)} ({simulation.AvailableStaff(staffClass).Count})"),
            () =>
            {
                _category = staffClass;
                host.PlaySound("selectx.wav");
            },
            highlight: staffClass == _category)).ToList());
        yield return WindowRow.Gap;
        var candidates = simulation.AvailableStaff(_category);
        if (candidates.Count == 0)
        {
            yield return WindowCell.Line("Nobody is looking for work this month.", dim: true);
            yield break;
        }

        yield return WindowRow.Of("Name", WindowCell.Number("Skill"), WindowCell.Number("Wage"), "Rank", string.Empty);
        for (var i = 0; i < candidates.Count; i++)
        {
            var profile = candidates[i];
            var index = i;
            var name = host.Texts?.Staff.FullName(profile) ?? string.Create(CultureInfo.InvariantCulture, $"Candidate {i + 1}");
            yield return WindowRow.Of(
                name,
                WindowCell.Number(StaffLabels.Skill(profile)),
                WindowCell.Number(MoneyText.Format(profile.Wage)),
                StaffLabels.Abilities(profile),
                WindowCell.Button("Hire", () =>
                {
                    if (hospital.Balance < profile.Wage)
                    {
                        // UIHireStaff:cannotAfford.
                        host.Notice($"You cannot afford {name}: a month's wage is {MoneyText.Format(profile.Wage)}");
                        host.PlaySound("wrong2.wav");
                        return;
                    }

                    host.StartHiring(_category, index);
                }));
            if (host.Texts?.Staff.Description(profile) is { Length: > 0 } description)
            {
                yield return WindowCell.Line("    " + description, dim: true);
            }
        }
    }

    private static string Plural(StaffClass staffClass) => staffClass switch
    {
        StaffClass.Doctor => "Doctors",
        StaffClass.Nurse => "Nurses",
        StaffClass.Handyman => "Handymen",
        _ => "Receptionists",
    };
}
