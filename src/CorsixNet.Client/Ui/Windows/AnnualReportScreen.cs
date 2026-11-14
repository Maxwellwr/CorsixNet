// The annual report as the original draws it, ported from CorsixTH
// CorsixTH/Lua/dialogs/fullscreen/annual_report.lua (UIAnnualReport: the statistics and awards pages, addTrophy,
// addAward, the motivations, drawStatisticsScreen).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Client.Rendering;
using CorsixNet.Sim.Economy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The fullscreen annual report: first the year's statistics over Award02V (the hospitals ranked by money, salary,
/// cures, deaths, visitors and value), then over Award01V the trophies and the framed awards or penalties of the year
/// that ended; a click on one shows its plaque or scroll with what it was for and what it brought.
/// </summary>
internal sealed class AnnualReportScreen
{
    private const string Palette = "AWARD02V";

    private static readonly Rectangle Close = new(608, 449, 26, 26);
    private static readonly Rectangle ToAwards = new(274, 435, 91, 42);
    private static readonly Rectangle ToStatistics = new(272, 367, 91, 42);
    private static readonly Rectangle PlaqueClose = new(389, 378, 26, 26);
    private static readonly Rectangle ScrollClose = new(369, 358, 26, 26);

    /// <summary>The three trophies' sprites and places (trophy_prop).</summary>
    private static readonly (int Sprite, Rectangle Area)[] TrophyPlaces =
    [
        (12, new Rectangle(142, 324, 61, 144)),
        (14, new Rectangle(466, 331, 72, 145)),
        (13, new Rectangle(407, 324, 60, 144)),
    ];

    /// <summary>The awards' shadows (award_shadows).</summary>
    private static readonly int[] Shadows = [4, 4, 4, 7, 7, 9];

    private bool _awardsPage;
    private int? _trophy;
    private int? _award;
    private string? _pressed;

    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Picture("AWARD01V") is not null && graphics.Picture("AWARD02V") is not null
        && graphics.Sheet("AWARD03V", Palette) is not null && graphics.Font("FONT45V", Palette) is not null
        && graphics.Font("FONT46V", Palette) is not null && graphics.Font("FONT47V", Palette) is not null;

    /// <summary>Whether a year-end prize is a trophy (a figure) rather than an award or penalty (a frame).</summary>
    public static bool IsTrophy(YearEndAwardKind kind) => kind is YearEndAwardKind.ConsistentReputation or YearEndAwardKind.AllCuredTrophy
        or YearEndAwardKind.NoDeathsTrophy or YearEndAwardKind.ManyCuredTrophy or YearEndAwardKind.HappyVips or YearEndAwardKind.DrinksSoldTrophy;

    /// <summary>What a prize was for: the original's text (<c>_S.trophy_room</c>), its section and index.</summary>
    public static (int Section, int Index) Text(YearEndAwardKind kind) => kind switch
    {
        YearEndAwardKind.ConsistentReputation => (27, 5),
        YearEndAwardKind.AllCuredTrophy => (27, 30),
        YearEndAwardKind.NoDeathsTrophy => (27, 14),
        YearEndAwardKind.ManyCuredTrophy => (27, 2),
        YearEndAwardKind.HappyVips => (27, 11),
        YearEndAwardKind.DrinksSoldTrophy => (27, 26),
        YearEndAwardKind.HighReputation => (55, 2),
        YearEndAwardKind.PoorReputation => (56, 2),
        YearEndAwardKind.HighValue => (55, 20),
        YearEndAwardKind.LowValue => (56, 20),
        YearEndAwardKind.ManyCures => (55, 5),
        YearEndAwardKind.FewCures => (56, 5),
        YearEndAwardKind.AllCures => (27, 29),
        YearEndAwardKind.FewDeaths => (55, 8),
        YearEndAwardKind.ManyDeaths => (56, 8),
        YearEndAwardKind.CuresOverDeaths => (55, 14),
        _ => (56, 14),
    };

    /// <summary>Where the <paramref name="index"/>th award's frame goes (addAward): three down the left, three down the right.</summary>
    public static Point AwardPlace(int index) => new(index < 3 ? 16 : 525, (index % 3) switch { 0 => 74, 1 => 189, _ => 304 });

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        var graphics = host.Graphics!;
        var hospital = host.Simulation.Hospitals[host.Player];
        var sheet = graphics.Sheet("AWARD03V", Palette)!;
        var local = new Point(mouse.X, mouse.Y) - origin;
        var held = mouse.LeftButton == ButtonState.Pressed;
        batch.Draw(graphics.Picture(_awardsPage ? "AWARD01V" : "AWARD02V")!, origin.ToVector2(), Color.White);
        void Draw(int sprite, int x, int y) => sheet.Draw(batch, sprite, origin.X + x, origin.Y + y, Color.White);
        void PressedAt(string key, Rectangle area, int sprite)
        {
            if (held && _pressed == key && area.Contains(local))
            {
                Draw(sprite, area.X, area.Y);
            }
        }

        PressedAt("close", Close, 1);
        if (!_awardsPage)
        {
            PressedAt("awards", ToAwards, 3);
            DrawStatistics(batch, host, graphics.Font("FONT45V", Palette)!, origin);
            return;
        }

        var (trophies, awards) = Prizes(hospital);
        for (var i = 0; i < Math.Min(3, trophies.Count); i++)
        {
            Draw(TrophyPlaces[i].Sprite, TrophyPlaces[i].Area.X, TrophyPlaces[i].Area.Y);
        }

        for (var i = 0; i < Math.Min(6, awards.Count); i++)
        {
            var at = AwardPlace(i);
            Draw(awards[i].Money + awards[i].Reputation > 0 ? 10 : 11, at.X + 12, at.Y + 11);
            Draw(23, at.X, at.Y);
            Draw(Shadows[i], at.X, at.Y);
        }

        if (_trophy is { } shownTrophy && shownTrophy < trophies.Count)
        {
            Draw(19, 206, 87);
            Draw(20, 206, 161);
            Draw(21, 206, 233);
            Draw(22, 206, 321);
            PressedAt("plaque", PlaqueClose, 2);
            Motivation(batch, host, graphics.Font("FONT46V", Palette)!, origin, trophies[shownTrophy], 225, 105, 185, 330);
        }
        else if (_award is { } shownAward && shownAward < awards.Count)
        {
            Draw(16, 206, 87);
            Draw(17, 206, 200);
            Draw(18, 206, 304);
            if (awards[shownAward].Money + awards[shownAward].Reputation <= 0)
            {
                Draw(15, 300, 341);
            }

            PressedAt("scroll", ScrollClose, 2);
            Motivation(batch, host, graphics.Font("FONT47V", Palette)!, origin, awards[shownAward], 235, 125, 165, 290);
        }
        else
        {
            PressedAt("statistics", ToStatistics, 3);
        }
    }

    public void Mouse(IWindowHost host, UiWindow window, Point local, MouseState mouse, MouseState previous)
    {
        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = ButtonAt(host, local);
            return;
        }

        if (mouse.LeftButton != ButtonState.Released || previous.LeftButton != ButtonState.Pressed)
        {
            return;
        }

        var pressed = _pressed;
        _pressed = null;
        if (pressed is null || pressed != ButtonAt(host, local))
        {
            return;
        }

        host.PlaySound(pressed == "close" ? "no4.wav" : WindowLayer.DefaultButtonSound);
        switch (pressed)
        {
            case "close":
                host.Close(window);
                break;
            case "awards":
                _awardsPage = true;
                break;
            case "statistics":
                _awardsPage = false;
                break;
            case "plaque":
                _trophy = null;
                break;
            case "scroll":
                _award = null;
                break;
            default:
                if (pressed.StartsWith("trophy", StringComparison.Ordinal))
                {
                    _trophy = pressed[^1] - '0';
                    _award = null;
                }
                else if (pressed.StartsWith("award", StringComparison.Ordinal))
                {
                    _award = pressed[^1] - '0';
                    _trophy = null;
                }

                break;
        }
    }

    /// <summary>The button under the point on the page shown, if any.</summary>
    private string? ButtonAt(IWindowHost host, Point local)
    {
        if (Close.Contains(local))
        {
            return "close";
        }

        if (!_awardsPage)
        {
            return ToAwards.Contains(local) ? "awards" : null;
        }

        if (_trophy is not null)
        {
            return PlaqueClose.Contains(local) ? "plaque" : null;
        }

        if (_award is not null)
        {
            return ScrollClose.Contains(local) ? "scroll" : null;
        }

        if (ToStatistics.Contains(local))
        {
            return "statistics";
        }

        var (trophies, awards) = Prizes(host.Simulation.Hospitals[host.Player]);
        for (var i = 0; i < Math.Min(3, trophies.Count); i++)
        {
            if (TrophyPlaces[i].Area.Contains(local))
            {
                return "trophy" + i;
            }
        }

        for (var i = 0; i < Math.Min(6, awards.Count); i++)
        {
            if (new Rectangle(AwardPlace(i), new Point(105, 103)).Contains(local))
            {
                return "award" + i;
            }
        }

        return null;
    }

    private static (List<YearEndAward> Trophies, List<YearEndAward> Awards) Prizes(Hospital hospital) =>
        (hospital.LastYearAwards.Where(award => IsTrophy(award.Kind)).ToList(), hospital.LastYearAwards.Where(award => !IsTrophy(award.Kind)).ToList());

    /// <summary>The plaque's or scroll's text: what the prize was for, cash or reputation, and how much.</summary>
    private static void Motivation(SpriteBatch batch, IWindowHost host, TextRenderer font, Point origin, YearEndAward prize, int x, int y, int width, int kindY)
    {
        var (section, index) = Text(prize.Kind);
        var text = host.Texts?.String(section, index) ?? prize.Kind.ToString();
        var lines = Maps.MapViewScreen.WrapToWidth(text.Replace("%d", string.Empty, StringComparison.Ordinal), width, line => font.Measure(line).Width);
        for (var i = 0; i < lines.Count; i++)
        {
            font.Draw(batch, lines[i], new Vector2(origin.X + x + ((width - font.Measure(lines[i]).Width) / 2), origin.Y + y + (i * font.LineHeight)));
        }

        var cash = prize.Money != 0;
        var kind = cash ? host.Texts?.String(63, 2) ?? "Cash" : host.Texts?.String(63, 1) ?? "Reputation";
        var amount = (cash ? prize.Money : prize.Reputation).ToString("+0;-0", CultureInfo.InvariantCulture);
        font.Draw(batch, kind, new Vector2(origin.X + 220 + ((200 - font.Measure(kind).Width) / 2), origin.Y + kindY));
        font.Draw(batch, amount, new Vector2(origin.X + 220 + ((200 - font.Measure(amount).Width) / 2), origin.Y + kindY + 25));
    }

    /// <summary>drawStatisticsScreen: the six rankings, each hospital in its place, the values right-aligned.</summary>
    private static void DrawStatistics(SpriteBatch batch, IWindowHost host, TextRenderer font, Point origin)
    {
        void Centred(string text, int x, int y, int width) =>
            font.Draw(batch, text, new Vector2(origin.X + x + ((width - font.Measure(text).Width) / 2), origin.Y + y));

        var simulation = host.Simulation;
        Centred(string.Create(CultureInfo.InvariantCulture, $"Charts {simulation.Date.Year + 1998}"), 210, 30, 200);
        (int Index, string Fallback, int X, int Y, bool Ascending, Func<Hospital, long> Value)[] rankings =
        [
            (1, "Money", 140, 98, false, h => h.Balance - h.Loan),
            (2, "Salary", 328, 98, false, h => h.PlayerSalary),
            (4, "Cures", 140, 205, false, h => h.Cured),
            (5, "Deaths", 328, 205, true, h => h.Deaths),
            (10, "Visitors", 140, 310, false, h => h.Visitors),
            (11, "Total value", 328, 310, false, h => h.Value),
        ];
        foreach (var (index, fallback, x, y, ascending, value) in rankings)
        {
            Centred(host.Texts?.String(26, index) ?? fallback, x, y, 170);
            var order = simulation.Hospitals.Select((hospital, i) => (Hospital: hospital, Index: i));
            order = ascending
                ? order.OrderBy(entry => value(entry.Hospital)).ThenBy(entry => entry.Index)
                : order.OrderByDescending(entry => value(entry.Hospital)).ThenBy(entry => entry.Index);
            var row = 0;
            foreach (var (hospital, i) in order)
            {
                var rowY = y + 30 + (row * 15);
                var name = (i == host.Player ? host.PlayerName : string.Create(CultureInfo.InvariantCulture, $"Hospital {i + 1}")).ToUpperInvariant();
                font.Draw(batch, name, new Vector2(origin.X + x, origin.Y + rowY));
                var number = value(hospital).ToString(CultureInfo.InvariantCulture);
                font.Draw(batch, number, new Vector2(origin.X + x + 100 + 70 - font.Measure(number).Width, origin.Y + rowY));
                row++;
            }
        }
    }
}
