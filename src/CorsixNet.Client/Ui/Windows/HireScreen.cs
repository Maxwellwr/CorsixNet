// The hire window as the original draws it, ported from CorsixTH CorsixTH/Lua/dialogs/hire_staff.lua (UIHireStaff: its
// panels, tabs, buttons, tooltips, draw, moveBy, setCategory, hire, cannotAfford).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Client.Rendering;
using CorsixNet.Sim.Staff;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The hire window of Req11V's panels, 242 by 323: the four classes as tabs with how many are looking for work, and for
/// the chosen class one candidate at a time with name, portrait, skill bar, wage, description and, for doctors, rank and
/// special skills; previous, hire, cancel and next below.
/// </summary>
internal sealed class HireScreen
{
    public static readonly Point Size = new(242, 323);

    private static readonly Color Blue = new(60, 174, 203);
    private static readonly Color PortraitBack = new(211, 255, 255);

    private static readonly (StaffClass Class, int Sprite, int Y)[] Tabs =
    [
        (StaffClass.Doctor, 264, 8),
        (StaffClass.Nurse, 266, 87),
        (StaffClass.Handyman, 268, 166),
        (StaffClass.Receptionist, 270, 245),
    ];

    private static readonly (string Key, int Sprite, int X, Rectangle Hit)[] Buttons =
    [
        ("previous", 272, 56, new Rectangle(8, 10, 43, 27)),
        ("hire", 274, 106, new Rectangle(0, 10, 58, 27)),
        ("cancel", 276, 163, new Rectangle(0, 10, 28, 27)),
        ("next", 278, 190, new Rectangle(0, 10, 44, 27)),
    ];

    private string? _pressed;

    /// <summary>The class shown, or null before one is chosen (<c>setCategory</c>).</summary>
    public StaffClass? Category { get; private set; }

    /// <summary>The candidate shown, by index in the class's list.</summary>
    public int Index { get; private set; }

    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Sheet("REQ11V", OriginalGraphics.MainPalette) is not null
        && graphics.Font("FONT01V", OriginalGraphics.MainPalette) is not null;

    /// <summary>The skill bar's width (draw): 40 pixels for the whole.</summary>
    public static int SkillWidth(StaffProfile profile) => (int)Math.Floor((profile.Skill * 40 / 1000.0) + 0.5);

    /// <summary>The rank marker's x for a doctor: junior, doctor or consultant.</summary>
    public static int RankX(StaffProfile profile) => profile.IsJunior ? 79 : profile.IsConsultant ? 131 : 101;

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        var graphics = host.Graphics!;
        var sheet = graphics.Sheet("REQ11V", OriginalGraphics.MainPalette)!;
        var font = graphics.Font("FONT01V", OriginalGraphics.MainPalette)!;
        var simulation = host.Simulation;
        var local = new Point(mouse.X, mouse.Y) - origin;
        var held = mouse.LeftButton == ButtonState.Pressed;
        void Panel(int sprite, int x, int y) => sheet.Draw(batch, sprite, origin.X + x, origin.Y + y, Color.White);
        void Colour(int x, int y, int width, int height, Color colour) => batch.Draw(graphics.Pixel, new Rectangle(origin.X + x, origin.Y + y, width, height), colour);

        Panel(253, 0, 0);
        Panel(254, 0, 83);
        Panel(254, 0, 162);
        Panel(255, 0, 241);
        foreach (var (staffClass, sprite, y) in Tabs)
        {
            var lit = Tutorial.Lit("staff:" + staffClass);
            Panel(Category == staffClass || lit ? sprite + 1 : sprite, 8, y);
        }

        Panel(256, 56, 0);
        for (var y = 49; y <= 113; y += 11)
        {
            Panel(257, 56, y);
        }

        Panel(260, 55, 114);
        if (Category is not null)
        {
            Panel(259, 68, 95);
        }

        Panel(261, 55, 160);
        if (Category != StaffClass.Doctor)
        {
            Colour(73, 133, 156, 34, Blue);
        }

        for (var y = 207; y <= 262; y += 3)
        {
            Panel(262, 54, y);
        }

        Colour(155, 45, 72, 81, PortraitBack);
        Panel(263, 56, 263);
        if (Category is null)
        {
            Colour(68, 15, 161, 257, Blue);
        }

        foreach (var (key, sprite, x, hit) in Buttons)
        {
            var area = new Rectangle(x + hit.X, 277 + hit.Y, hit.Width, hit.Height);
            var down = (held && _pressed == key && area.Contains(local)) || (key == "hire" && Tutorial.Lit("hire-button"));
            Panel(down ? sprite + 1 : sprite, x, 277);
        }

        void Centred(string text, int x, int y, int width) => font.Draw(batch, text, new Vector2(origin.X + x + ((width - font.Measure(text).Width) / 2), origin.Y + y));
        foreach (var (staffClass, _, y) in Tabs)
        {
            Centred(simulation.AvailableStaff(staffClass).Count.ToString(CultureInfo.InvariantCulture), 16, y + 50, 26);
        }

        if (Profile(host) is not { } profile)
        {
            return;
        }

        font.Draw(batch, host.Texts?.Staff.FullName(profile) ?? "Candidate", new Vector2(origin.X + 79, origin.Y + 21));
        StaffFaces.Current?.Draw(batch, profile, origin + new Point(158, 48));
        font.Draw(batch, string.Create(CultureInfo.InvariantCulture, $"${profile.Wage}"), new Vector2(origin.X + 116, origin.Y + 179));
        if (host.Texts?.Staff.Description(profile) is { Length: > 0 } description)
        {
            var lines = Maps.MapViewScreen.WrapToWidth(description, 149, text => font.Measure(text).Width);
            for (var i = 0; i < lines.Count; i++)
            {
                font.Draw(batch, lines[i], new Vector2(origin.X + 74, origin.Y + 205 + (i * font.LineHeight)));
            }
        }

        for (var dx = 0; dx < SkillWidth(profile); dx++)
        {
            Panel(3, 68 + 22 + dx, 95 + 9);
        }

        if (Category == StaffClass.Doctor)
        {
            Panel(258, 71, 49);
            Panel(296, RankX(profile), 80);
            if (profile.IsSurgeon)
            {
                Panel(292, 55 + 65, 114 + 22);
            }

            if (profile.IsPsychiatrist)
            {
                Panel(293, 55 + 82, 114 + 28);
            }

            if (profile.IsResearcher)
            {
                Panel(294, 55 + 109, 114 + 27);
            }
        }
    }

    public void Mouse(IWindowHost host, UiWindow window, Point local, MouseState mouse, MouseState previous)
    {
        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = ButtonAt(local);
            return;
        }

        if (mouse.LeftButton != ButtonState.Released || previous.LeftButton != ButtonState.Pressed)
        {
            return;
        }

        var pressed = _pressed;
        _pressed = null;
        if (pressed is null || pressed != ButtonAt(local))
        {
            return;
        }

        switch (pressed)
        {
            case "previous":
                MoveBy(host, -1);
                break;
            case "next":
                MoveBy(host, 1);
                break;
            case "cancel":
                host.PlaySound("no4.wav");
                host.Close(window);
                break;
            case "hire":
                Hire(host);
                break;
            default:
                // A tab: the class, or none again when it is chosen already; Wrong2.wav when nobody is looking for work.
                var staffClass = Enum.Parse<StaffClass>(pressed);
                if (host.Simulation.AvailableStaff(staffClass).Count == 0)
                {
                    host.PlaySound("wrong2.wav");
                }
                else
                {
                    host.PlaySound(WindowLayer.DefaultButtonSound);
                    Category = Category == staffClass ? null : staffClass;
                    Index = 0;
                }

                break;
        }
    }

    public string? Tooltip(IWindowHost host, Point local)
    {
        string Tip(int index, string fallback) => OriginalTooltips.Get(host, index, fallback);
        switch (ButtonAt(local))
        {
            case "previous":
                return Tip(27, "Look at the previous person");
            case "next":
                return Tip(28, "Look at the next person");
            case "hire":
                return Tip(29, "Hire this person");
            case "cancel":
                return Tip(30, "Cancel");
            case { } tab:
                return Tip(23 + Array.FindIndex(Tabs, t => t.Class.ToString() == tab), "See who is looking for work");
        }

        if (Category is null)
        {
            return null;
        }

        var profile = Profile(host);
        return new Rectangle(68, 173, 159, 21).Contains(local) ? Tip(33, "The salary")
            : new Rectangle(68, 95, 109, 20).Contains(local) ? Tip(32, "Skill")
            : Category == StaffClass.Doctor && new Rectangle(68, 44, 83, 51).Contains(local) ? Tip(31, "Seniority")
            : Category == StaffClass.Doctor && new Rectangle(68, 134, 39, 33).Contains(local) ? Tip(34, "Qualifications")
            : profile is { IsSurgeon: true } && new Rectangle(120, 136, 17, 31).Contains(local) ? Tip(35, "Surgeon")
            : profile is { IsPsychiatrist: true } && new Rectangle(137, 136, 27, 31).Contains(local) ? Tip(36, "Psychiatrist")
            : profile is { IsResearcher: true } && new Rectangle(164, 136, 27, 31).Contains(local) ? Tip(37, "Researcher")
            : null;
    }

    /// <summary>moveBy: to the previous or next candidate, Wrong2.wav at either end.</summary>
    private void MoveBy(IWindowHost host, int by)
    {
        if (Category is { } staffClass && Index + by >= 0 && Index + by < host.Simulation.AvailableStaff(staffClass).Count)
        {
            Index += by;
            host.PlaySound(WindowLayer.DefaultButtonSound);
            return;
        }

        host.PlaySound("wrong2.wav");
    }

    /// <summary>hire: the candidate goes on the cursor (YesX.wav), unless there is none or a month's wage is too much.</summary>
    private void Hire(IWindowHost host)
    {
        if (Category is not { } staffClass || Profile(host) is not { } profile)
        {
            host.PlaySound("wrong2.wav");
            return;
        }

        if (host.Simulation.Hospitals[host.Player].Balance < profile.Wage)
        {
            // cannotAfford: the adviser says so.
            host.Adviser.Say(host.Texts?.Named("adviser.warnings.cannot_afford") ?? "You don't have enough money in the bank to hire that person!", Environment.TickCount64);
            host.PlaySound("wrong2.wav");
            return;
        }

        host.PlaySound("YesX.wav");
        host.StartHiring(staffClass, Index);
    }

    private StaffProfile? Profile(IWindowHost host)
    {
        if (Category is not { } staffClass)
        {
            return null;
        }

        var candidates = host.Simulation.AvailableStaff(staffClass);
        Index = Math.Clamp(Index, 0, Math.Max(0, candidates.Count - 1));
        return candidates.Count > 0 ? candidates[Index] : null;
    }

    private static string? ButtonAt(Point local)
    {
        foreach (var (key, _, x, hit) in Buttons)
        {
            if (new Rectangle(x + hit.X, 277 + hit.Y, hit.Width, hit.Height).Contains(local))
            {
                return key;
            }
        }

        foreach (var (staffClass, _, y) in Tabs)
        {
            if (new Rectangle(8, y, 40, 69).Contains(local))
            {
                return staffClass.ToString();
            }
        }

        return null;
    }
}
