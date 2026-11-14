// The staff window as the original draws it, ported from CorsixTH CorsixTH/Lua/dialogs/staff_dialog.lua (UIStaff: its
// panels, buttons, tooltips, draw, the view circle, changeHandymanAttributes, changeParcel, fireStaff, pickupStaff).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Client.Rendering;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Staff;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// A member of staff's window of Req01V's panels, 220 wide (304 high, 332 for a handyman): name, portrait, happiness,
/// tiredness and skill bars, wage, a doctor's rank and special skills, a handyman's task priorities and parcel, the
/// sack and pick up buttons, and the view circle showing them in the hospital (a click there follows them, a right
/// click goes on to the next of their kind).
/// </summary>
internal sealed class StaffScreen(int staffId)
{
    private const long RepeatDelay = 300;
    private const long RepeatEvery = 80;

    private string? _pressed;
    private long _nextRepeat;

    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Sheet("REQ01V", OriginalGraphics.MainPalette) is not null
        && graphics.Font("FONT01V", OriginalGraphics.MainPalette) is not null;

    /// <summary>The window's size: a handyman's is taller for the task priorities.</summary>
    public static Point Size(bool handyman) => new(220, handyman ? 332 : 304);

    /// <summary>is_in_view_circle: within 39 pixels of the circle's middle (55, 248), or (55, 276) for a handyman.</summary>
    public static bool InViewCircle(Point local, bool handyman)
    {
        var dx = local.X - 55;
        var dy = local.Y - (handyman ? 276 : 248);
        return (dx * dx) + (dy * dy) < 39 * 39;
    }

    /// <summary>A bar's width for a share from 0 to 1: 40 pixels for the whole, rounded.</summary>
    public static int Bar(decimal share) => (int)Math.Floor((Math.Clamp(share, 0, 1) * 40) + 0.5m);

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse, Humanoid staff)
    {
        var graphics = host.Graphics!;
        var sheet = graphics.Sheet("REQ01V", OriginalGraphics.MainPalette)!;
        var font = graphics.Font("FONT01V", OriginalGraphics.MainPalette)!;
        var profile = staff.Profile!;
        var handyman = staff.Behaviour as HandymanBehaviour;
        var height = Size(handyman is not null).Y;
        var local = new Point(mouse.X, mouse.Y) - origin;
        var held = mouse.LeftButton == ButtonState.Pressed;
        void Panel(int sprite, int x, int y) => sheet.Draw(batch, sprite, origin.X + x, origin.Y + y, Color.White);
        void Button(string key, int sprite, int pressed, int x, int y, int width, int buttonHeight) =>
            Panel(held && _pressed == key && new Rectangle(x, y, width, buttonHeight).Contains(local) ? pressed : sprite, x, y);

        // The map round them first, under the frame's circle.
        if (host.CloseUp(staffId) is { } closeUp)
        {
            batch.Draw(closeUp, new Rectangle(origin.X + 17, origin.Y + height - 93, 75, 75), Color.White);
        }

        Panel(297, 15, 0);
        for (var y = 51; y <= 121; y += 10)
        {
            Panel(298, 15, y);
        }

        Panel(299, 104, 50);
        Panel(300, 105, 82);
        Panel(301, 15, 114);
        batch.Draw(graphics.Pixel, new Rectangle(origin.X + 35, origin.Y + 51, 71, 81), new Color(208, 252, 252));
        var offset = handyman is null ? 0 : 27;
        if (handyman is not null)
        {
            Panel(311, 15, 131);
            for (var y = 149; y <= 184; y += 5)
            {
                Panel(312, 15, y);
            }

            Panel(302, 5, 205);
            Panel(313, 15, 189);
            Button("litter", 314, 315, 37, 145, 49, 48);
            Button("plants", 316, 317, 92, 145, 49, 48);
            Button("machines", 318, 319, 148, 145, 49, 48);
            Panel(240, 21, 210);
            Panel(303, 0, 253);
            Panel(304, 6, 302);
        }
        else
        {
            Panel(302, 5, 178);
            Panel(303, 0, 226);
            Panel(304, 6, 274);
            if (profile.Class != StaffClass.Doctor)
            {
                // Hides the skills a doctor has.
                batch.Draw(graphics.Pixel, new Rectangle(origin.X + 32, origin.Y + 141, 171, 39), new Color(85, 202, 219));
            }
        }

        Button("sack", 307, 308, 106, 226 + offset, 50, 50);
        Button("pickup", 309, 310, 164, 226 + offset, 37, 50);
        Button("close", 305, 306, 178, 18, 24, 24);

        font.Draw(batch, host.Texts?.Staff.FullName(profile) ?? "Staff", new Vector2(origin.X + 42, origin.Y + 28));
        font.Draw(batch, string.Create(CultureInfo.InvariantCulture, $"${profile.Wage}"), new Vector2(origin.X + 135, origin.Y + 198 + offset));
        if (handyman is not null)
        {
            var parcel = handyman.Parcel == 0 ? "All parcels" : string.Create(CultureInfo.InvariantCulture, $"Parcel {handyman.Parcel}");
            font.Draw(batch, parcel, new Vector2(origin.X + 35 + ((50 - font.Measure(parcel).Width) / 2), origin.Y + 215));
            foreach (var (task, x) in new[] { (HandymanTaskType.Cleaning, 43), (HandymanTaskType.Watering, 99), (HandymanTaskType.Repairing, 155) })
            {
                for (var dx = 0; dx < Bar(handyman.Priority(task) / 1000m); dx++)
                {
                    Panel(351, x + dx, 200);
                }
            }
        }

        foreach (var (sprite, share, y) in new[] { (348, staff.Happiness, 56), (349, 1 - staff.Fatigue, 89), (350, profile.Skill / 1000m, 120) })
        {
            for (var dx = 0; dx < Bar(share); dx++)
            {
                Panel(sprite, 139 + dx, y);
            }
        }

        if (profile.Class == StaffClass.Doctor)
        {
            Panel(347, profile.IsJunior ? 38 : profile.IsConsultant ? 89 : 60, 173);
            if (profile.IsSurgeon)
            {
                Panel(344, 144, 148);
            }

            if (profile.IsPsychiatrist)
            {
                Panel(345, 155, 154);
            }

            if (profile.IsResearcher)
            {
                Panel(346, 178, 153);
            }
        }

        StaffFaces.Current?.Draw(batch, profile, origin + new Point(38, 54));
    }

    public void Mouse(IWindowHost host, UiWindow window, Point local, MouseState mouse, MouseState previous, Humanoid staff)
    {
        var handyman = staff.Behaviour as HandymanBehaviour;
        var now = Environment.TickCount64;
        if (mouse.RightButton == ButtonState.Pressed && previous.RightButton == ButtonState.Released
            && (InViewCircle(local, handyman is not null) || new Rectangle(33, 19, 139, 23).Contains(local)))
        {
            // onMouseUp's right button: the next member of staff of the same kind.
            if (NextOfKind(host.Simulation, host.Player, staff) is { } next)
            {
                host.Close(window);
                host.Open(new StaffWindow(next.Id));
                if (!InViewCircle(local, handyman is not null))
                {
                    host.CenterOn(next.Tile);
                }
            }

            return;
        }

        var under = ButtonAt(local, handyman is not null);
        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = under;
            if (under is "litter" or "plants" or "machines" && handyman is not null)
            {
                MorePriority(host, staff, under);
                _nextRepeat = now + RepeatDelay;
            }

            return;
        }

        if (mouse.LeftButton == ButtonState.Pressed)
        {
            if (_pressed == "circle" && InViewCircle(local, handyman is not null))
            {
                // do_scroll: the view follows them while the button is held in the circle.
                host.CenterOn(staff.Tile);
            }
            else if (_pressed is "litter" or "plants" or "machines" && under == _pressed && now >= _nextRepeat)
            {
                MorePriority(host, staff, _pressed);
                _nextRepeat = now + RepeatEvery;
            }

            return;
        }

        if (previous.LeftButton != ButtonState.Pressed)
        {
            return;
        }

        var pressed = _pressed;
        _pressed = null;
        if (pressed is null || pressed != under)
        {
            return;
        }

        switch (pressed)
        {
            case "close":
                host.PlaySound("no4.wav");
                host.Close(window);
                break;
            case "sack":
                host.PlaySound(WindowLayer.DefaultButtonSound);
                host.Open(new ConfirmWindow(host.Texts?.Named("confirmation.sack_staff") ?? "Are you sure you want to sack this member of staff?", () => host.Submit(new FireStaffCommand(staff.Id))));
                break;
            case "pickup":
                host.PlaySound(WindowLayer.DefaultButtonSound);
                host.Close(window);
                host.PickUpStaff(staff.Id);
                break;
            case "portrait":
                host.PlaySound(WindowLayer.DefaultButtonSound);
                host.Close(window);
                host.Open(new StaffManagementWindow());
                break;
            case "parcel" when handyman is not null:
                // changeParcel: all parcels, then each parcel the hospital owns in turn.
                host.PlaySound(WindowLayer.DefaultButtonSound);
                var map = host.Simulation.Map;
                var next = Enumerable.Range(1, Math.Max(0, map.ParcelCount - 1)).FirstOrDefault(parcel => parcel > handyman.Parcel && map.GetParcelOwner(parcel) == host.Player + 1);
                host.Submit(new SetHandymanParcelCommand(staff.Id, next));
                break;
        }
    }

    public static string? Tooltip(IWindowHost host, Point local, Humanoid staff)
    {
        string Tip(int index, string fallback) => OriginalTooltips.Get(host, index, fallback);
        var handyman = staff.Behaviour is HandymanBehaviour;
        var profile = staff.Profile!;
        var offset = handyman ? 27 : 0;
        // Staff's tooltips are S[32][98] on, a handyman's S[32][119] on.
        string Pick(int staffIndex, int handymanIndex, string fallback) => Tip(handyman ? handymanIndex : staffIndex, fallback);
        return ButtonAt(local, handyman) switch
        {
            "close" => Pick(99, 120, "Close this window"),
            "sack" => Pick(111, 130, "Sack this person"),
            "pickup" => Pick(112, 131, "Pick this person up to put them down elsewhere"),
            "portrait" => Pick(100, 121, "Open the staff management screen"),
            "litter" => Tip(125, "Sweep up more litter"),
            "plants" => Tip(126, "Water the plants more"),
            "machines" => Tip(127, "Repair the machines more"),
            "parcel" => "The plot where the handyman accepts tasks, click to change setting",
            "circle" => Pick(110, 129, "Left click to zoom to staff, right click to cycle through staff members"),
            _ => new Rectangle(33, 19, 139, 23).Contains(local) ? Pick(98, 119, "Name of the staff member")
                : new Rectangle(113, 49, 91, 25).Contains(local) ? Pick(101, 122, "Happiness")
                : new Rectangle(113, 74, 91, 35).Contains(local) ? Pick(102, 123, "Tiredness")
                : new Rectangle(113, 109, 91, 25).Contains(local) ? Pick(103, 124, "Ability")
                : new Rectangle(90, 191 + offset, 114, 23).Contains(local) ? Pick(109, 128, "Salary")
                : profile.Class == StaffClass.Doctor && new Rectangle(30, 141, 81, 41).Contains(local) ? Tip(104, "Seniority")
                : profile.Class == StaffClass.Doctor && new Rectangle(111, 146, 30, 33).Contains(local) ? Tip(105, "Skills")
                : profile is { IsSurgeon: true } && new Rectangle(143, 148, 12, 29).Contains(local) ? Tip(106, "Surgeon")
                : profile is { IsPsychiatrist: true } && new Rectangle(155, 148, 22, 29).Contains(local) ? Tip(107, "Psychiatrist")
                : profile is { IsResearcher: true } && new Rectangle(177, 148, 25, 29).Contains(local) ? Tip(108, "Researcher")
                : null,
        };
    }

    private static string? ButtonAt(Point local, bool handyman)
    {
        var offset = handyman ? 27 : 0;
        if (new Rectangle(178, 18, 24, 24).Contains(local))
        {
            return "close";
        }

        if (new Rectangle(106, 226 + offset, 50, 50).Contains(local))
        {
            return "sack";
        }

        if (new Rectangle(164, 226 + offset, 37, 50).Contains(local))
        {
            return "pickup";
        }

        if (new Rectangle(35, 51, 71, 81).Contains(local))
        {
            return "portrait";
        }

        if (handyman)
        {
            if (new Rectangle(37, 145, 49, 48).Contains(local))
            {
                return "litter";
            }

            if (new Rectangle(92, 145, 49, 48).Contains(local))
            {
                return "plants";
            }

            if (new Rectangle(148, 145, 49, 48).Contains(local))
            {
                return "machines";
            }

            if (new Rectangle(21, 210, 73, 30).Contains(local))
            {
                return "parcel";
            }
        }

        return InViewCircle(local, handyman) ? "circle" : null;
    }

    /// <summary>changeHandymanAttributes, once a click: the adviser explains it the first time.</summary>
    private static void MorePriority(IWindowHost host, Humanoid staff, string task)
    {
        if (!host.Simulation.Hospitals[host.Player].HandymanPrioritiesChanged)
        {
            host.Adviser.Say(AdviserChecks.HandymanAdjust.Text(host.Texts), Environment.TickCount64);
        }

        host.PlaySound(WindowLayer.DefaultButtonSound);
        host.Submit(new IncreaseHandymanPriorityCommand(staff.Id, task switch
        {
            "litter" => HandymanTaskType.Cleaning,
            "plants" => HandymanTaskType.Watering,
            _ => HandymanTaskType.Repairing,
        }));
    }

    /// <summary>The next member of the player's staff of the same class after this one, round to the first.</summary>
    private static Humanoid? NextOfKind(Simulation simulation, int player, Humanoid staff)
    {
        var kind = simulation.Entities.OfType<Humanoid>()
            .Where(h => h.Profile is { } p && p.Class == staff.Profile!.Class && h.Employer == player && !h.IsFired && !h.IsDespawning)
            .OrderBy(h => h.Id)
            .ToList();
        return kind.Count < 2 ? null : kind.FirstOrDefault(h => h.Id > staff.Id) ?? kind[0];
    }
}
