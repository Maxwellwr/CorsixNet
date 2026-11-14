// The staff management screen as the original draws it, ported from CorsixTH
// CorsixTH/Lua/dialogs/fullscreen/staff_management.lua (UIStaffManagement: its panels, buttons, blankers, tooltips,
// updateStaffList's ordering, draw, scrolling, payBonus, increaseSalary, fire and the view of the staff member).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Client.Rendering;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Staff;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>How the staff list is ordered (updateStaffList's list_order).</summary>
internal enum StaffOrder
{
    Hire,
    Morale,
    Tiredness,
    Skill,
}

/// <summary>
/// The fullscreen staff management over Staff01V: a tab for each kind of staff with how many there are, ten of them a
/// page with wage, morale, tiredness and skill (and their averages), ordered by a click on a heading; for the one chosen
/// the portrait, attention to detail, a doctor's rank and special skills (or how far trained), the bonus and pay rise
/// with their amounts, the sack button and a view of them in the hospital (a click there shows them).
/// </summary>
internal sealed class StaffManagementScreen
{
    public const int RowsPerPage = 10;

    private const string Palette = "STAFF01V";

    private static readonly Rectangle Close = new(603, 443, 26, 26);
    private static readonly Rectangle ScrollUp = new(12, 86, 31, 74);
    private static readonly Rectangle ScrollDown = new(12, 274, 31, 74);
    private static readonly Rectangle Bonus = new(319, 372, 112, 39);
    private static readonly Rectangle PayRise = new(319, 418, 112, 39);
    private static readonly Rectangle Sack = new(438, 372, 45, 85);
    private static readonly Rectangle View = new(497, 373, 83, 82);
    private static readonly int[] TabX = [53, 119, 185, 251];

    private static readonly Color RowBlank = new(60, 174, 203);
    private static readonly Color SkillBlank = new(57, 166, 198);
    private static readonly Color PortraitBack = new(210, 255, 255);
    private static readonly Color Red = new(221, 83, 0);

    private string? _pressed;
    private int? _hovered;

    public StaffClass Category { get; private set; } = StaffClass.Doctor;

    /// <summary>The page shown, from 1.</summary>
    public int Page { get; private set; } = 1;

    /// <summary>The member of staff chosen, by entity id.</summary>
    public int? Selected { get; private set; }

    public StaffOrder Order { get; private set; } = StaffOrder.Hire;

    /// <summary>list_direction "up": the largest first.</summary>
    public bool Descending { get; private set; }

    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Picture("STAFF01V") is not null && graphics.Sheet("STAFF02V", Palette) is not null
        && graphics.Font("FONT01V", Palette) is not null && graphics.Font("FONT02V", Palette) is not null;

    /// <summary>A bar's width for a share from 0 to 1: 40 pixels for the whole, rounded.</summary>
    public static int Bar(decimal share) => StaffScreen.Bar(share);

    /// <summary>The staff of the category shown, in the chosen order.</summary>
    public List<Humanoid> List(IWindowHost host) => Ordered(StaffLabels.Employed(host.Simulation, host.Player).Where(h => h.Profile!.Class == Category));

    /// <summary>updateStaffList's sort: hiring order, or ascending by the attribute ("down"), or descending ("up").</summary>
    public List<Humanoid> Ordered(IEnumerable<Humanoid> staff)
    {
        var hired = staff.ToList();
        if (Order == StaffOrder.Hire)
        {
            return Descending ? Enumerable.Reverse(hired).ToList() : hired;
        }

        decimal Key(Humanoid h) => Order switch
        {
            StaffOrder.Morale => h.Happiness,
            StaffOrder.Tiredness => h.Fatigue,
            _ => h.Profile!.Skill,
        };
        return Descending ? hired.OrderByDescending(Key).ToList() : hired.OrderBy(Key).ToList();
    }

    /// <summary>setCategory: the tab's staff from the first page, nobody chosen.</summary>
    public void SetCategory(StaffClass category)
    {
        Category = category;
        Selected = null;
        Page = 1;
    }

    /// <summary>order_by: a heading clicked once orders by it, again the other way, a third time back to hiring order.</summary>
    public void OrderBy(StaffOrder order)
    {
        if (Order != order)
        {
            Order = order;
            return;
        }

        if (!Descending)
        {
            Descending = true;
        }
        else
        {
            Order = StaffOrder.Hire;
            Descending = false;
        }
    }

    /// <summary>
    /// The key handlers: left and right go to the previous and next kind of staff (previousCategory, nextCategory), up
    /// and down choose the previous and next member of staff, turning the page (previousStaff, nextStaff, selectIndex).
    /// </summary>
    public void Key(IWindowHost host, string action) => Key(action, List(host).Select(h => h.Id).ToList());

    /// <summary><see cref="Key(IWindowHost, string)"/> over the ids listed.</summary>
    public void Key(string action, IReadOnlyList<int> listed)
    {
        var category = StaffClasses.HireOrder.ToList().IndexOf(Category);
        switch (action)
        {
            case "ingame_scroll_left" when category > 0:
                SetCategory(StaffClasses.HireOrder[category - 1]);
                break;
            case "ingame_scroll_right" when category < StaffClasses.HireOrder.Count - 1:
                SetCategory(StaffClasses.HireOrder[category + 1]);
                break;
            case "ingame_scroll_up" or "ingame_scroll_down":
                var index = Selected is { } id ? listed.ToList().IndexOf(id) : -1;
                var next = index < 0
                    ? (action == "ingame_scroll_up" ? listed.Count - 1 : 0)
                    : index + (action == "ingame_scroll_up" ? -1 : 1);
                if (next >= 0 && next < listed.Count)
                {
                    Selected = listed[next];
                    Page = (next / RowsPerPage) + 1;
                }

                break;
        }
    }

    /// <summary>scrollUp and scrollDown: a page at a time while there is more than one, the choice dropped.</summary>
    public void Scroll(int count, int by)
    {
        var page = Page + by;
        if (count > RowsPerPage && page >= 1 && (page - 1) * RowsPerPage < count)
        {
            Page = page;
            Selected = null;
        }
    }

    /// <summary>updateScrollDot: the dot's height for the page, from 168 down 83 pixels, or null with one page.</summary>
    public static int? ScrollDot(int count, int page)
    {
        if (count <= RowsPerPage)
        {
            return null;
        }

        var pages = (count + RowsPerPage - 1) / RowsPerPage;
        return 168 + (83 * (page - 1) / (pages - 1));
    }

    /// <summary>The row of the page under <paramref name="local"/>, from 0, if a member of staff is listed there.</summary>
    public int? RowAt(Point local, int count)
    {
        if (local.X <= 50 || local.X >= 624 || local.Y <= 81 || local.Y >= 351)
        {
            return null;
        }

        var row = (local.Y - 81) / 27;
        return count - ((Page - 1) * RowsPerPage) > row ? row : null;
    }

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        var graphics = host.Graphics!;
        var sheet = graphics.Sheet("STAFF02V", Palette)!;
        var white = graphics.Font("FONT01V", Palette)!;
        var blue = graphics.Font("FONT02V", Palette)!;
        var local = new Point(mouse.X, mouse.Y) - origin;
        var held = mouse.LeftButton == ButtonState.Pressed;
        var employed = StaffLabels.Employed(host.Simulation, host.Player).ToList();
        var list = List(host);
        var chosen = list.FirstOrDefault(h => h.Id == Selected);
        void Panel(int sprite, int x, int y) => sheet.Draw(batch, sprite, origin.X + x, origin.Y + y, Color.White);
        void Fill(int x, int y, int width, int height, Color colour) => batch.Draw(graphics.Pixel, new Rectangle(origin.X + x, origin.Y + y, width, height), colour);
        void Button(string key, Rectangle area, int sprite)
        {
            if (held && _pressed == key && area.Contains(local))
            {
                Panel(sprite, area.X, area.Y);
            }
        }

        void Centred(TextRenderer font, string text, int x, int y, int width) =>
            font.Draw(batch, text, new Vector2(origin.X + x + ((width - font.Measure(text).Width) / 2), origin.Y + y));
        void Bars(int sprite, int x, int y, int width)
        {
            for (var dx = 0; dx < width; dx++)
            {
                Panel(sprite, x + dx, y);
            }
        }

        batch.Draw(graphics.Picture("STAFF01V")!, origin.ToVector2(), Color.White);
        Button("close", Close, 10);
        for (var i = 0; i < TabX.Length; i++)
        {
            if (StaffClasses.HireOrder[i] == Category || (held && _pressed == "tab" + i && new Rectangle(TabX[i], 15, 58, 58).Contains(local)))
            {
                Panel(1, TabX[i], 15);
            }
        }

        Button("up", ScrollUp, 2);
        Button("down", ScrollDown, 3);
        Button("bonus", Bonus, 7);
        Button("raise", PayRise, 8);
        Button("sack", Sack, 9);
        if (ScrollDot(list.Count, Page) is { } dot)
        {
            Panel(11, 21, dot);
        }

        var first = (Page - 1) * RowsPerPage;
        for (var row = list.Count - first; row < RowsPerPage; row++)
        {
            Fill(50, 55 + ((row + 1) * 27), 580, 27, RowBlank);
        }

        if (Category != StaffClass.Doctor)
        {
            Fill(225, 365, 90, 39, SkillBlank);
            Fill(142, 406, 168, 54, SkillBlank);
        }

        Centred(white, host.Texts?.String(24, 3) ?? "MORALE", 323, 31, 95);
        Centred(white, host.Texts?.String(24, 4) ?? "TIREDNESS", 427, 31, 95);
        Centred(white, host.Texts?.String(24, 5) ?? "SKILL", 530, 31, 95);
        for (var i = 0; i < TabX.Length; i++)
        {
            Centred(white, employed.Count(h => h.Profile!.Class == StaffClasses.HireOrder[i]).ToString(CultureInfo.InvariantCulture), TabX[i], 57, 58);
        }

        for (var row = 0; row < RowsPerPage && first + row < list.Count; row++)
        {
            var staff = list[first + row];
            var y = 63 + ((row + 1) * 27);
            var font = row == _hovered ? blue : white;
            white.Draw(batch, (first + row + 1).ToString(CultureInfo.InvariantCulture), new Vector2(origin.X + 58, origin.Y + y));
            font.Draw(batch, StaffLabels.Name(host, staff), new Vector2(origin.X + 88, origin.Y + y));
            Centred(font, string.Create(CultureInfo.InvariantCulture, $"${staff.Profile!.Wage}"), 230, y, 80);
            Bars(16, 351, y + 2, Bar(staff.Happiness));
            Bars(15, 456, y + 2, Bar(1 - staff.Fatigue));
            Bars(14, 559, y + 2, Bar(staff.Profile.Skill / 1000m));
        }

        if (list.Count > 0)
        {
            Bars(16, 351, 59, Bar(list.Average(h => h.Happiness)));
            Bars(15, 456, 59, Bar(1 - list.Average(h => h.Fatigue)));
            Bars(14, 559, 59, Bar(list.Average(h => h.Profile!.Skill / 1000m)));
        }

        if (chosen is null)
        {
            return;
        }

        var profile = chosen.Profile!;
        var top = 54 + ((list.IndexOf(chosen) - first + 1) * 27);
        Fill(49, top, 581, 1, Red);
        Fill(49, top + 27, 581, 1, Red);
        Fill(49, top, 1, 28, Red);
        Fill(630, top, 1, 28, Red);
        if (host.CloseUp(chosen.Id) is { } closeUp)
        {
            batch.Draw(closeUp, new Rectangle(origin.X + View.X + 4, origin.Y + View.Y + 3, 75, 75), Color.White);
        }

        Fill(65, 374, 71, 81, PortraitBack);
        StaffFaces.Current?.Draw(batch, profile, origin + new Point(68, 377));
        var maxSalary = host.Simulation.Rules.MaxSalary;
        Centred(white, string.Create(CultureInfo.InvariantCulture, $"${profile.Wage / 10}"), 377, 387, 45);
        Centred(white, string.Create(CultureInfo.InvariantCulture, $"${Math.Min(profile.Wage * 11 / 10, maxSalary)}"), 377, 432, 45);
        Bars(13, 178, 387, Bar(profile.AttentionToDetail / 1000m));
        if (profile.Class != StaffClass.Doctor)
        {
            return;
        }

        foreach (var (specialism, x, progressSprite, qualifiedSprite) in Specialisms)
        {
            var progress = profile.SpecialismProgress(specialism);
            if (progress >= 1)
            {
                Panel(qualifiedSprite, x, 408);
            }
            else if (progress > 0 && !profile.IsConsultant)
            {
                Panel(progressSprite, x, 408);
                Bars(13, x + 8, 447, (int)Math.Floor((progress * 23) + 0.5m));
            }
        }

        Panel(12, profile.IsConsultant ? 259 + 29 : profile.IsJunior ? 259 - 23 : 259, 397);
    }

    private static readonly (Specialism Specialism, int X, int Progress, int Qualified)[] Specialisms =
    [
        (Specialism.Surgeon, 188, 17, 20),
        (Specialism.Psychiatrist, 228, 18, 21),
        (Specialism.Researcher, 268, 19, 22),
    ];

    public void Mouse(IWindowHost host, UiWindow window, Point local, MouseState mouse, MouseState previous)
    {
        var list = List(host);
        var hovered = RowAt(local, list.Count);
        if (hovered is not null && hovered != _hovered)
        {
            host.PlaySound("Hlight5.wav");
        }

        _hovered = hovered;
        if (mouse.ScrollWheelValue != previous.ScrollWheelValue)
        {
            Scroll(list.Count, mouse.ScrollWheelValue > previous.ScrollWheelValue ? -1 : 1);
        }

        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = ButtonAt(local, Selected is not null);
            if (hovered is { } row)
            {
                // onMouseDown: a row chooses its member of staff at once.
                Selected = list[((Page - 1) * RowsPerPage) + row].Id;
                host.PlaySound(WindowLayer.DefaultButtonSound);
            }
            else if (local.X > 321 && local.X < 629 && local.Y > 22 && local.Y < 46)
            {
                if (local.X < 422)
                {
                    OrderBy(StaffOrder.Morale);
                }
                else if (local.X > 425 && local.X < 527)
                {
                    OrderBy(StaffOrder.Tiredness);
                }
                else if (local.X > 529)
                {
                    OrderBy(StaffOrder.Skill);
                }
            }

            return;
        }

        if (mouse.LeftButton != ButtonState.Released || previous.LeftButton != ButtonState.Pressed)
        {
            return;
        }

        var pressed = _pressed;
        _pressed = null;
        if (pressed is null || pressed != ButtonAt(local, Selected is not null))
        {
            return;
        }

        var chosen = list.FirstOrDefault(h => h.Id == Selected);
        switch (pressed)
        {
            case "close":
                host.PlaySound(WindowLayer.DefaultButtonSound);
                host.Close(window);
                break;
            case { } tab when tab.StartsWith("tab", StringComparison.Ordinal):
                host.PlaySound(WindowLayer.DefaultButtonSound);
                SetCategory(StaffClasses.HireOrder[tab[3] - '0']);
                break;
            case "up":
                host.PlaySound(WindowLayer.DefaultButtonSound);
                Scroll(list.Count, -1);
                break;
            case "down":
                host.PlaySound(WindowLayer.DefaultButtonSound);
                Scroll(list.Count, 1);
                break;
            case "bonus":
                if (chosen is not null && host.Simulation.Hospitals[host.Player].Balance > chosen.Profile!.Wage / 10)
                {
                    host.Submit(new PayBonusCommand(chosen.Id));
                    host.PlaySound("cashreg.wav");
                }
                else
                {
                    host.PlaySound("wrong2.wav");
                }

                break;
            case "raise":
                if (chosen is not null && chosen.Profile!.Wage < host.Simulation.Rules.MaxSalary)
                {
                    host.Submit(new IncreaseSalaryCommand(chosen.Id));
                    host.PlaySound("bonusal2.wav");
                }
                else
                {
                    host.PlaySound("wrong2.wav");
                }

                break;
            case "sack":
                if (chosen is not null)
                {
                    host.PlaySound(WindowLayer.DefaultButtonSound);
                    var id = chosen.Id;
                    host.Open(new ConfirmWindow(host.Texts?.Named("confirmation.sack_staff") ?? "Are you sure you want to sack this member of staff?", () =>
                    {
                        host.Submit(new FireStaffCommand(id));
                        if (Selected == id)
                        {
                            Selected = null;
                        }
                    }));
                }
                else
                {
                    host.PlaySound("wrong2.wav");
                }

                break;
            case "view" when chosen is not null:
                // onMouseUp in the view: the map goes to them and their window opens in place of this screen.
                host.CenterOn(chosen.Tile);
                host.Close(window);
                host.Open(new StaffWindow(chosen.Id));
                break;
        }
    }

    public string? Tooltip(IWindowHost host, Point local)
    {
        string Tip(int index, string fallback) => OriginalTooltips.Get(host, index, fallback);
        var doctor = Category == StaffClass.Doctor;
        var count = List(host).Count;
        var chosen = List(host).FirstOrDefault(h => h.Id == Selected);
        switch (ButtonAt(local, true))
        {
            case "close":
                return Tip(60, "Return to the previous screen");
            case "tab0":
                return Tip(44, "Show a list of all the doctors employed in your hospital");
            case "tab1":
                return Tip(45, "Show a list of all the nurses employed in your hospital");
            case "tab2":
                return Tip(46, "Show a list of all the handymen employed in your hospital");
            case "tab3":
                return Tip(47, "Show a list of all the receptionists employed in your hospital");
            case "up":
                return Tip(55, "Show the previous page");
            case "down":
                return Tip(56, "Show the next page");
            case "bonus":
                return Tip(57, "Pay this person a 10% bonus");
            case "raise":
                return Tip(59, "Give this person a 10% pay rise");
            case "sack":
                return Tip(58, "Sack this person");
            case "view":
                return Tip(63, "Zoom to the person's location in the hospital");
        }

        if (chosen?.Profile is { Class: StaffClass.Doctor } profile)
        {
            foreach (var (specialism, x, _, _) in Specialisms)
            {
                var progress = profile.SpecialismProgress(specialism);
                if (progress > 0 && (progress >= 1 || !profile.IsConsultant) && new Rectangle(x, 408, 40, 52).Contains(local))
                {
                    var index = specialism switch { Specialism.Surgeon => 64, Specialism.Psychiatrist => 65, _ => 66 };
                    return progress >= 1
                        ? Tip(index, specialism.ToString())
                        : Tip(index + 3, "%d%% trained").Replace("%d", ((int)Math.Floor(progress * 100)).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("%%", "%", StringComparison.Ordinal);
                }
            }
        }

        if (local.Y is >= 51 and <= 75)
        {
            return local.X is >= 321 and <= 421 ? Tip(48, "Staff morale")
                : local.X is >= 426 and <= 526 ? Tip(49, "Tiredness")
                : local.X is >= 530 and <= 629 ? Tip(50, "Skill level")
                : null;
        }

        if (local.Y is >= 84 and <= 351 && (local.Y - 84) % 27 <= 24 && ((Page - 1) * RowsPerPage) + ((local.Y - 84) / 27) < count)
        {
            return local.X is >= 193 and <= 313 ? Tip(51, "Current salary")
                : local.X is >= 321 and <= 421 ? Tip(52, "Morale")
                : local.X is >= 425 and <= 525 ? Tip(53, "Tiredness")
                : local.X is >= 529 and <= 628 ? Tip(54, "Skill level")
                : null;
        }

        return new Rectangle(146, 367, 80, 40).Contains(local) ? Tip(62, "Attention to detail")
            : doctor && new Rectangle(230, 367, 80, 40).Contains(local) ? Tip(61, "Seniority")
            : doctor && new Rectangle(146, 406, 40, 54).Contains(local) ? Tip(70, "Special skills")
            : null;
    }

    private static string? ButtonAt(Point local, bool chosen)
    {
        if (Close.Contains(local))
        {
            return "close";
        }

        for (var i = 0; i < TabX.Length; i++)
        {
            if (new Rectangle(TabX[i], 15, 58, 58).Contains(local))
            {
                return "tab" + i;
            }
        }

        return ScrollUp.Contains(local) ? "up"
            : ScrollDown.Contains(local) ? "down"
            : Bonus.Contains(local) ? "bonus"
            : PayRise.Contains(local) ? "raise"
            : Sack.Contains(local) ? "sack"
            : chosen && View.Contains(local) ? "view"
            : null;
    }
}
