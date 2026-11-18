// A member of staff asking for a raise, ported from CorsixTH CorsixTH/Lua/dialogs/staff_rise.lua (UIStaffRise: its
// panels, buttons, tooltips, draw, drawDoctorAttributes, increaseSalary, fireStaff, mustPause) and dialogs/message.lua
// (UIMessage:openMessage for a "strike": the request opens from its icon on the bottom panel).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenWards.Client.Maps;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Entities;
using OpenWards.Sim.People;
using OpenWards.Sim.Staff;

namespace OpenWards.Client.Ui.Windows;

/// <summary>
/// A raise request (<c>UIStaffRise</c>), opened from its icon on the bottom panel: the member of staff's name, portrait,
/// the hospital round them, wage, ability and a doctor's rank and skills, their complaint (one of the original's
/// <c>pay_rise.regular</c> texts, picked at random) and two buttons, give the rise or refuse it and see them go. Esc
/// does not close it and the game stands still while it is open. It goes by itself when the request is settled
/// otherwise (the member of staff gave up waiting, the raise was given in staff management).
/// </summary>
internal sealed partial class StaffRiseWindow : UiWindow
{
    private readonly StaffRiseScreen _screen = new();

    /// <summary>Which of <c>pay_rise.regular</c>'s six texts (S[3][4] to S[3][9]) the complaint is.</summary>
    private readonly int _text;

    public StaffRiseWindow(int staffId, int amount, int text = -1)
    {
        StaffId = staffId;
        Amount = amount;
        _text = text is >= 0 and < 6 ? text : Random.Shared.Next(6);
    }

    public int StaffId { get; }

    /// <summary>The rise asked for (<c>rise_amount</c>).</summary>
    public int Amount { get; }

    public override string Title => UiText.Get("openwards.staff.asks_for_raise", "Asks for a raise");

    public override bool IsSameAs(UiWindow other) => other is StaffRiseWindow window && window.StaffId == StaffId;

    /// <summary>esc_closes = false.</summary>
    public override bool ClosesWithEsc => false;

    /// <summary>UIStaffRise:mustPause.</summary>
    public override bool MustPause => true;

    /// <summary>
    /// Staff:checkIfWaitedTooLongForRaise closes the window when the request runs out; so does anything else that
    /// settles it.
    /// </summary>
    public override bool IsDone(IWindowHost host) => Staff(host) is null;

    /// <summary>setDefaultPosition(0.5, 0.5).</summary>
    public override Vector2? DefaultPosition => new(0.5f, 0.5f);

    public override bool IsFullscreen(IWindowHost host) => StaffRiseScreen.IsAvailable(host);

    public override Point FullscreenSize => StaffRiseScreen.Size;

    public override Vector2? FullscreenPosition => new(0.5f, 0.5f);

    /// <summary>The complaint: <c>_S.pay_rise.regular.__random:format(rise_amount, final_wage)</c>.</summary>
    public string Complaint(IWindowHost host, int wage)
    {
        var text = host.Texts?.String(3, 4 + _text) ?? "I'm totally frazzled. I need a good rest, plus a rise of %d if you don't want to see me jack in this poxy job.";
        return FormatLua(text, Amount, wage + Amount);
    }

    /// <summary>Lua's <c>string.format</c> for the original's %d and %s: each takes the next argument.</summary>
    internal static string FormatLua(string text, params object[] arguments)
    {
        var next = 0;
        return LuaPlaceholder().Replace(text, match => match.Value == "%%" ? "%"
            : next < arguments.Length ? Convert.ToString(arguments[next++], CultureInfo.InvariantCulture) ?? string.Empty
            : match.Value);
    }

    [GeneratedRegex("%%|%[ds]")]
    private static partial Regex LuaPlaceholder();

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        if (Staff(host) is { } staff)
        {
            _screen.Draw(batch, host, origin, mouse, staff, Complaint(host, staff.Profile!.Wage));
        }
    }

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous)
    {
        switch (_screen.Clicked(local, mouse, previous))
        {
            case "accept":
                Answer(host, grant: true);
                break;
            case "decline":
                Answer(host, grant: false);
                break;
        }
    }

    public override string? FullscreenTooltip(IWindowHost host, Point local) =>
        Staff(host) is { } staff ? StaffRiseScreen.Tooltip(host, local, staff.Profile!) : null;

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        if (Staff(host) is not { } staff)
        {
            yield break;
        }

        var profile = staff.Profile!;
        yield return WindowCell.Line(StaffLabels.Name(host, staff), highlight: true);
        yield return WindowCell.Line(host.Texts?.Staff.Profession(profile.Class) ?? profile.Class.ToString(), dim: true);
        yield return WindowRow.Of(UiText.Get("openwards.staff.wage", "Wage"), WindowCell.Number(MoneyText.Format(profile.Wage)));
        yield return WindowRow.Of(UiText.Get("openwards.staff.skill", "Skill"), WindowCell.Number(StaffLabels.Skill(profile)));
        yield return WindowRow.Gap;
        yield return WindowCell.Line(Complaint(host, profile.Wage));
        yield return WindowRow.Gap;
        yield return WindowRow.Of(
            WindowCell.Button(UiText.Get("openwards.staff.grant", "Grant"), () => Answer(host, grant: true)).WithTooltip(OriginalTooltips.Get(host, 236, "Give the pay rise")),
            WindowCell.Button(UiText.Get("openwards.staff.refuse", "Refuse (fires)"), () => Answer(host, grant: false)).WithTooltip(OriginalTooltips.Get(host, 237, "Refuse the pay rise: the person leaves")));
    }

    /// <summary>
    /// increaseSalary (Staff:increaseWage plays bonusal2.wav) or fireStaff; the window closes either way.
    /// </summary>
    private void Answer(IWindowHost host, bool grant)
    {
        host.PlaySound(grant ? "bonusal2.wav" : WindowLayer.DefaultButtonSound);
        host.Submit(new AnswerRaiseCommand(StaffId, grant));
        host.Close(this);
    }

    /// <summary>The member of staff while their request is still waiting, or null.</summary>
    private Humanoid? Staff(IWindowHost host) =>
        host.Simulation.FindEntity(StaffId) is Humanoid { Profile: not null, IsFired: false, RaiseRequest: > 0 } staff && staff.Employer == host.Player ? staff : null;
}

/// <summary>
/// UIStaffRise's look, 362 by 288: Req12V's panels, the hospital round the member of staff on the left over their
/// portrait, name (Font01V), wage and ability bar; the complaint in black (Font00V) on the right above the accept and
/// decline buttons.
/// </summary>
internal sealed class StaffRiseScreen
{
    public static readonly Point Size = new(362, 288);

    private static readonly Rectangle Accept = new(180, 233, 90, 45);
    private static readonly Rectangle Decline = new(270, 233, 90, 45);

    private string? _pressed;

    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Sheet("REQ12V", OriginalGraphics.MainPalette) is not null
        && graphics.Font("FONT01V", OriginalGraphics.MainPalette) is not null
        && graphics.Font("FONT00V", OriginalGraphics.MainPalette) is not null;

    /// <summary>The ability bar's width: 40 pixels for the most, rounded.</summary>
    public static int AbilityBar(StaffProfile profile) => (int)Math.Floor((Math.Clamp(profile.Skill / 1000m, 0, 1) * 40) + 0.5m);

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse, Humanoid staff, string complaint)
    {
        var graphics = host.Graphics!;
        var sheet = graphics.Sheet("REQ12V", OriginalGraphics.MainPalette)!;
        var white = graphics.Font("FONT01V", OriginalGraphics.MainPalette)!;
        var black = graphics.Font("FONT00V", OriginalGraphics.MainPalette)!;
        var profile = staff.Profile!;
        var local = new Point(mouse.X, mouse.Y) - origin;
        var held = mouse.LeftButton == ButtonState.Pressed;
        void Panel(int sprite, int x, int y) => sheet.Draw(batch, sprite, origin.X + x, origin.Y + y, Color.White);
        void Fill(int x, int y, int width, int height, Color colour) => batch.Draw(graphics.Pixel, new Rectangle(origin.X + x, origin.Y + y, width, height), colour);

        // Left hand side.
        Panel(280, 0, 0);
        foreach (var y in new[] { 54, 64, 74, 84, 94, 104 })
        {
            Panel(281, 0, y);
        }

        Panel(282, 0, 114);
        Panel(283, 0, 171);
        Panel(284, 0, 214);
        Fill(96, 44, 72, 81, new Color(211, 255, 255)); // The portrait's background.

        // Right hand side.
        Panel(285, 180, 0);
        for (var y = 20; y <= 160; y += 28)
        {
            Panel(286, 180, y);
        }

        Panel(287, 180, 188);
        Panel(held && _pressed == "accept" && Accept.Contains(local) ? 289 : 288, Accept.X, Accept.Y);
        Panel(held && _pressed == "decline" && Decline.Contains(local) ? 291 : 290, Decline.X, Decline.Y);
        if (profile.Class != StaffClass.Doctor)
        {
            // Hides what only a doctor has.
            Fill(10, 130, 160, 40, new Color(60, 174, 203));
            Fill(89, 198, 81, 45, new Color(60, 174, 203));
        }

        StaffFaces.Current?.Draw(batch, profile, origin + new Point(99, 47));

        // The viewport: map:draw of 71 by 81 world pixels round them, with them a little low.
        if (host.CloseUp(staff.Id) is { } closeUp)
        {
            var source = new Rectangle(0, 0, closeUp.Width * 71 / MapViewScreen.CloseUpSize, closeUp.Height);
            batch.Draw(closeUp, new Rectangle(origin.X + 16, origin.Y + 44, 71, MapViewScreen.CloseUpSize), source, Color.White);
        }

        white.Draw(batch, host.Texts?.Staff.FullName(profile) ?? "Staff", new Vector2(origin.X + 20, origin.Y + 20));
        white.Draw(batch, string.Create(CultureInfo.InvariantCulture, $"${profile.Wage}"), new Vector2(origin.X + 60, origin.Y + 178));

        // The bar looks like attention to detail's but shows the ability, as in the original.
        for (var dx = 0; dx < AbilityBar(profile); dx++)
        {
            Panel(295, 42 + dx, 230);
        }

        if (profile.Class == StaffClass.Doctor)
        {
            Panel(296, 98 + (profile.IsConsultant ? 52 : profile.IsJunior ? 0 : 22), 230);
            if (profile.IsSurgeon)
            {
                Panel(292, 74, 133);
            }

            if (profile.IsPsychiatrist)
            {
                Panel(293, 90, 139);
            }

            if (profile.IsResearcher)
            {
                Panel(294, 120, 138);
            }
        }

        var lineY = origin.Y + 20;
        foreach (var line in MapViewScreen.WrapToWidth(complaint, 140, t => black.Measure(t).Width))
        {
            black.Draw(batch, line, new Vector2(origin.X + 200, lineY));
            lineY += black.LineHeight;
        }
    }

    /// <summary>The button pressed and let go over, or null.</summary>
    public string? Clicked(Point local, MouseState mouse, MouseState previous)
    {
        var under = Accept.Contains(local) ? "accept" : Decline.Contains(local) ? "decline" : null;
        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = under;
            return null;
        }

        if (mouse.LeftButton == ButtonState.Released && previous.LeftButton == ButtonState.Pressed)
        {
            var pressed = _pressed;
            _pressed = null;
            return pressed is not null && pressed == under ? pressed : null;
        }

        return null;
    }

    public static string? Tooltip(IWindowHost host, Point local, StaffProfile profile)
    {
        string Tip(int index, string fallback) => OriginalTooltips.Get(host, index, fallback);
        var doctor = profile.Class == StaffClass.Doctor;
        return Accept.Contains(local) ? Tip(236, "Pay the staff member the money they are asking for")
            : Decline.Contains(local) ? Tip(237, "Refuse to pay and sack them instead")
            : new Rectangle(14, 15, 155, 23).Contains(local) ? Tip(98, "Name of the staff member")
            : new Rectangle(96, 44, 72, 81).Contains(local) ? Tip(100, "Face of the staff member")
            : new Rectangle(14, 171, 154, 22).Contains(local) ? Tip(109, "Salary")
            : new Rectangle(12, 213, 77, 30).Contains(local) ? Tip(103, "Ability")
            : doctor && new Rectangle(89, 197, 79, 46).Contains(local) ? Tip(104, "Seniority")
            : doctor && profile.IsSurgeon && new Rectangle(72, 133, 15, 31).Contains(local) ? Tip(106, "Surgeon")
            : doctor && profile.IsPsychiatrist && new Rectangle(87, 133, 29, 31).Contains(local) ? Tip(107, "Psychiatrist")
            : doctor && profile.IsResearcher && new Rectangle(116, 133, 30, 31).Contains(local) ? Tip(108, "Researcher")
            : doctor && new Rectangle(14, 132, 33, 34).Contains(local) ? Tip(105, "Skills")
            : null;
    }
}
