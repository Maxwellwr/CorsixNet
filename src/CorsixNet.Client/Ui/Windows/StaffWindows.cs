using System.Globalization;
using CorsixNet.Client.Maps;
using CorsixNet.Client.Rendering;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Staff;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>What the staff windows say about a member of staff.</summary>
internal static class StaffLabels
{
    /// <summary>The player's staff still employed, in hiring order.</summary>
    public static IEnumerable<Humanoid> Employed(Simulation simulation, int player) =>
        simulation.Entities.OfType<Humanoid>().Where(h => h.Profile is not null && h.Employer == player && !h.IsFired && !h.IsDespawning);

    public static string Name(IWindowHost host, Humanoid staff) =>
        host.Texts?.Staff.FullName(staff.Profile!) ?? string.Create(CultureInfo.InvariantCulture, $"{staff.Profile!.Class} {staff.Id}");

    public static string Percent(decimal fraction) => string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Round(Math.Clamp(fraction, 0m, 1m) * 100m)} %");

    public static string Skill(StaffProfile profile) => string.Create(CultureInfo.InvariantCulture, $"{profile.Skill / 10} %");

    /// <summary>A doctor's rank and special skills, as the staff window's icons show them.</summary>
    public static string Abilities(StaffProfile profile)
    {
        if (profile.Class != StaffClass.Doctor)
        {
            return string.Empty;
        }

        var parts = new List<string> { profile.IsConsultant ? "Consultant" : profile.IsJunior ? "Junior" : "Doctor" };
        if (profile.IsSurgeon)
        {
            parts.Add("surgeon");
        }

        if (profile.IsPsychiatrist)
        {
            parts.Add("psychiatrist");
        }

        if (profile.IsResearcher)
        {
            parts.Add("researcher");
        }

        return string.Join(", ", parts);
    }

    /// <summary>What the member of staff is doing, roughly as the staff window's action text says.</summary>
    public static string Doing(Simulation simulation, Humanoid staff) => staff.Behaviour switch
    {
        DoctorBehaviour { RoomId: > 0 } doctor when simulation.FindEntity(doctor.RoomId) is Room room => $"Working in the {simulation.Content.FindRoom(room.RoomType)?.Name ?? room.RoomType}",
        NurseBehaviour { RoomId: > 0 } nurse when simulation.FindEntity(nurse.RoomId) is Room room => $"Working in the {simulation.Content.FindRoom(room.RoomType)?.Name ?? room.RoomType}",
        ReceptionistBehaviour => "At the reception desk",
        HandymanBehaviour => "Looking for work",
        LeavingBehaviour => "Leaving",
        null => "Idle",
        _ => "Wandering about",
    };
}

/// <summary>
/// The staff management screen (CorsixTH's <c>dialogs/fullscreen/staff_management.lua</c>, Shift+F3 here): the staff of
/// one class with wage, skill, tiredness and happiness; for the selected one a bonus of a tenth of the wage, a tenth more
/// salary, firing (after a confirmation, as the original's dialog asks) and answers to a pending request for a raise.
/// </summary>
internal sealed class StaffManagementWindow : UiWindow
{
    private StaffClass _category = StaffClass.Doctor;
    private int? _selected;
    private int? _confirmFire;

    public override string Title => "Staff management";

    public override string? Hotkey => "Shift+F3";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var simulation = host.Simulation;
        var employed = StaffLabels.Employed(simulation, host.Player).ToList();
        yield return new WindowRow(StaffClasses.HireOrder.Select(staffClass => WindowCell.Button(
            string.Create(CultureInfo.InvariantCulture, $"{Plural(staffClass)} ({employed.Count(h => h.Profile!.Class == staffClass)})"),
            () =>
            {
                _category = staffClass;
                _selected = null;
                _confirmFire = null;
                host.PlaySound("selectx.wav");
            },
            highlight: staffClass == _category)).ToList());
        yield return WindowRow.Gap;
        yield return WindowRow.Of("Name", WindowCell.Number("Wage"), WindowCell.Number("Skill"), WindowCell.Number("Tired"), WindowCell.Number("Happy"), "Abilities", "Raise asked for");
        var members = employed.Where(h => h.Profile!.Class == _category).ToList();
        if (_selected is { } id && members.All(h => h.Id != id))
        {
            _selected = null;
        }

        foreach (var staff in members)
        {
            var profile = staff.Profile!;
            var selected = staff.Id == _selected;
            yield return WindowRow.Of(
                WindowCell.Button(StaffLabels.Name(host, staff), () => Select(host, staff.Id), highlight: selected),
                WindowCell.Number(MoneyText.Format(profile.Wage), highlight: selected),
                WindowCell.Number(StaffLabels.Skill(profile), highlight: selected),
                WindowCell.Number(StaffLabels.Percent(staff.Fatigue), highlight: selected),
                WindowCell.Number(StaffLabels.Percent(staff.Happiness), highlight: selected),
                new WindowCell(StaffLabels.Abilities(profile), Highlight: selected),
                new WindowCell(staff.RaiseRequest > 0 ? $"+{MoneyText.Format(staff.RaiseRequest)}" : string.Empty, Highlight: true));
        }

        if (members.Count == 0)
        {
            yield return WindowCell.Line("None hired.", dim: true);
        }

        yield return WindowRow.Gap;
        if (members.FirstOrDefault(h => h.Id == _selected) is not { } chosen)
        {
            yield return WindowCell.Line("Click a name to choose a member of staff.", dim: true);
            yield break;
        }

        var bonus = chosen.Profile!.Wage / 10;
        var hospital = simulation.Hospitals[host.Player];
        var atMaximum = chosen.Profile.Wage >= simulation.Rules.MaxSalary;
        yield return WindowRow.Of(
            WindowCell.Button(string.Create(CultureInfo.InvariantCulture, $"Bonus {MoneyText.Format(bonus)}"), () =>
            {
                host.Submit(new PayBonusCommand(chosen.Id));
                host.PlaySound("cashreg.wav");
            }, hospital.Balance > bonus),
            WindowCell.Button(string.Create(CultureInfo.InvariantCulture, $"Raise salary {MoneyText.Format(chosen.Profile.Wage / 10)}"), () =>
            {
                host.Submit(new IncreaseSalaryCommand(chosen.Id));
                host.PlaySound("bonusal2.wav");
            }, !atMaximum),
            WindowCell.Button("Staff window", () => host.Open(new StaffWindow(chosen.Id))),
            WindowCell.Button("Show", () => host.CenterOn(chosen.Tile)),
            _confirmFire == chosen.Id
                ? WindowCell.Button("Sure? Fire", () =>
                {
                    host.Submit(new FireStaffCommand(chosen.Id));
                    _confirmFire = null;
                    _selected = null;
                })
                : WindowCell.Button("Fire", () => _confirmFire = chosen.Id));
        if (chosen.RaiseRequest > 0)
        {
            yield return WindowRow.Of(
                $"Asks for a raise of {MoneyText.Format(chosen.RaiseRequest)} or quits",
                WindowCell.Button("Grant", () => host.Submit(new AnswerRaiseCommand(chosen.Id, true))),
                WindowCell.Button("Refuse (fires)", () => host.Submit(new AnswerRaiseCommand(chosen.Id, false))));
        }
    }

    private void Select(IWindowHost host, int id)
    {
        _selected = id;
        _confirmFire = null;
        host.PlaySound("selectx.wav");
    }

    private static string Plural(StaffClass staffClass) => staffClass switch
    {
        StaffClass.Doctor => "Doctors",
        StaffClass.Nurse => "Nurses",
        StaffClass.Handyman => "Handymen",
        _ => "Receptionists",
    };
}

/// <summary>
/// The window of one member of staff (CorsixTH's <c>dialogs/staff_dialog.lua</c>), opened by clicking them: name,
/// profession, wage, skill, abilities, tiredness, happiness and what they are doing; picking them up, firing them, and
/// for a handyman the parcel they look after (<c>UIStaff:changeParcel</c>).
/// </summary>
internal sealed class StaffWindow(int staffId) : UiWindow
{
    private bool _confirmFire;

    /// <summary>The member of staff as the table last showed them, for the portrait (staff_dialog.lua's <c>drawFace</c>).</summary>
    private StaffProfile? _shown;

    public override Point? CanvasSize => StaffFaces.Current is null ? null : new(StaffFaces.Width, StaffFaces.Height);

    public override void DrawCanvas(SpriteBatch batch, Rectangle area, Texture2D pixel, TextRenderer text)
    {
        if (_shown is { } profile)
        {
            StaffFaces.Current?.Draw(batch, profile, area.Location);
        }
    }

    public int StaffId { get; } = staffId;

    public override string Title => "Staff";

    public override bool IsSameAs(UiWindow other) => other is StaffWindow window && window.StaffId == StaffId;

    public override bool IsDone(IWindowHost host) => Staff(host) is null;

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        if (Staff(host) is not { } staff)
        {
            yield break;
        }

        var profile = staff.Profile!;
        _shown = profile;
        var simulation = host.Simulation;
        yield return WindowCell.Line(StaffLabels.Name(host, staff), highlight: true);
        yield return WindowCell.Line(host.Texts?.Staff.Profession(profile.Class) ?? profile.Class.ToString(), dim: true);
        if (host.Texts?.Staff.Description(profile) is { Length: > 0 } description)
        {
            yield return WindowCell.Line(description, dim: true);
        }

        yield return WindowRow.Of("Wage", WindowCell.Number(MoneyText.Format(profile.Wage)));
        yield return WindowRow.Of("Skill", WindowCell.Number(StaffLabels.Skill(profile)));
        if (StaffLabels.Abilities(profile) is { Length: > 0 } abilities)
        {
            yield return WindowRow.Of("Rank", abilities);
        }

        yield return WindowRow.Of("Tiredness", WindowCell.Number(StaffLabels.Percent(staff.Fatigue)), staff.IsTiredMoodShown ? "Tired" : string.Empty);
        yield return WindowRow.Of("Happiness", WindowCell.Number(StaffLabels.Percent(staff.Happiness)));
        yield return WindowRow.Of("Doing", StaffLabels.Doing(simulation, staff));
        if (staff.RaiseRequest > 0)
        {
            yield return WindowRow.Of("Asks for a raise", WindowCell.Number(MoneyText.Format(staff.RaiseRequest)));
        }

        if (staff.Behaviour is HandymanBehaviour handyman)
        {
            // UIStaff:changeParcel: all parcels (0), then each parcel the hospital owns in turn.
            var parcels = OwnedParcels(simulation, host.Player);
            yield return WindowRow.Of(
                "Looks after",
                handyman.Parcel == 0 ? "All parcels" : string.Create(CultureInfo.InvariantCulture, $"Parcel {handyman.Parcel}"),
                WindowCell.Button("Next parcel", () =>
                {
                    var next = parcels.FirstOrDefault(parcel => parcel > handyman.Parcel);
                    host.Submit(new SetHandymanParcelCommand(staff.Id, next));
                }, parcels.Count > 0));
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(
            WindowCell.Button("Pick up", () => host.PickUpStaff(staff.Id)),
            WindowCell.Button("Show", () => host.CenterOn(staff.Tile)),
            _confirmFire
                ? WindowCell.Button("Sure? Fire", () => host.Submit(new FireStaffCommand(staff.Id)))
                : WindowCell.Button("Fire", () => _confirmFire = true));
    }

    private Humanoid? Staff(IWindowHost host) =>
        host.Simulation.FindEntity(StaffId) is Humanoid { Profile: not null, IsFired: false, IsDespawning: false } staff && staff.Employer == host.Player ? staff : null;

    private static List<int> OwnedParcels(Simulation simulation, int player)
    {
        var parcels = new List<int>();
        for (var parcel = 1; parcel < simulation.Map.ParcelCount; parcel++)
        {
            if (simulation.Map.GetParcelOwner(parcel) == player + 1)
            {
                parcels.Add(parcel);
            }
        }

        return parcels;
    }
}
