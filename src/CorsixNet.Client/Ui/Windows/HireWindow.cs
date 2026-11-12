using System.Globalization;
using CorsixNet.Client.Maps;
using CorsixNet.Client.Rendering;
using CorsixNet.Sim.Staff;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The hire window (CorsixTH's <c>dialogs/hire_staff.lua</c>), Y here: the candidates of a class with name, skill,
/// wage, rank and description, and the portrait of the one picked (<c>drawFace</c>). Hiring one puts them on the cursor
/// to be placed in the hospital (<c>UIPlaceStaff</c>), if the hospital can afford a month's wage.
/// </summary>
internal sealed class HireWindow : UiWindow
{
    private const int PortraitScale = 2;

    private StaffClass _category = StaffClass.Doctor;

    /// <summary>The class of candidates shown.</summary>
    public StaffClass Category => _category;

    /// <summary>The candidate whose portrait shows, by index in the class's list.</summary>
    private int _selected;

    /// <summary>The picked candidate as the table last showed them, for the portrait.</summary>
    private StaffProfile? _shown;

    public override Point? CanvasSize => StaffFaces.Current is null ? null : new(StaffFaces.Width * PortraitScale, StaffFaces.Height * PortraitScale);

    public override void DrawCanvas(SpriteBatch batch, Rectangle area, Texture2D pixel, TextRenderer text)
    {
        if (_shown is { } profile)
        {
            StaffFaces.Current?.Draw(batch, profile, area.Location, PortraitScale);
        }
    }

    public override string Title => "Hire staff";

    public override string? Hotkey => "Y";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var simulation = host.Simulation;
        var hospital = simulation.Hospitals[host.Player];
        // hire_staff_window's doctors, nurses, handymen and receptionists tooltips are strings 23 to 26, in the hire order.
        yield return new WindowRow(StaffClasses.HireOrder.Select((staffClass, order) => WindowCell.Button(
            string.Create(CultureInfo.InvariantCulture, $"{Plural(staffClass)} ({simulation.AvailableStaff(staffClass).Count})"),
            () =>
            {
                _category = staffClass;
                _selected = 0;
                host.PlaySound("selectx.wav");
            },
            highlight: staffClass == _category || Tutorial.Lit("staff:" + staffClass)).WithTooltip(OriginalTooltips.Get(host, 23 + order, "See the " + Plural(staffClass).ToLowerInvariant() + " looking for work"))).ToList());
        yield return WindowRow.Gap;
        var candidates = simulation.AvailableStaff(_category);
        _selected = Math.Clamp(_selected, 0, Math.Max(0, candidates.Count - 1));
        _shown = candidates.Count > 0 ? candidates[_selected] : null;
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
                new WindowCell(name, () => _selected = index, Highlight: index == _selected) { Tooltip = "Show the portrait" },
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
                }, highlight: index == _selected && Tutorial.Lit("hire-button")).WithTooltip(OriginalTooltips.Get(host, 29, "Hire this person")));
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
