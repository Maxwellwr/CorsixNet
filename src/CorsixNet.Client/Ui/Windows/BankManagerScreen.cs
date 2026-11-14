// The bank manager as the original draws it, ported from CorsixTH
// CorsixTH/Lua/dialogs/fullscreen/bank_manager.lua (UIBankManager: its panels, buttons, tooltips, onTick's face, draw).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Client.Input;
using CorsixNet.Client.Maps;
using CorsixNet.Client.Rendering;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The fullscreen bank manager over Bank01V: the hospital's value, balance, loan and its monthly interest on the left,
/// with buttons to repay and borrow 5,000 (all you can with Ctrl); what the three insurance companies owe on the right,
/// each with a button for its graph; the inflation and interest rates; a click on the manager shows the statement
/// (Stat01V) and a click there goes back; the sign at the bottom opens the town map. His eyes blink, his brows lift
/// and he smiles now and then.
/// </summary>
internal sealed class BankManagerScreen
{
    /// <summary>One of CorsixTH's ticks, which the face's animation counts (the game's 18 a second).</summary>
    public const long TickMilliseconds = 1000 / 18;

    private const string Palette = "BANK01V";
    private const string StatPalette = "STAT01V";

    private static readonly Button[] Buttons =
    [
        new("close", new Rectangle(607, 448, 26, 26), 4, Page.Main),
        new("close", new Rectangle(601, 442, 26, 26), 4, Page.Statement),
        new("town", new Rectangle(250, 390, 200, 50), 0, Page.Main),
        new("borrow", new Rectangle(192, 265, 21, 21), 6, Page.Main),
        new("repay", new Rectangle(50, 265, 21, 21), 5, Page.Main),
        new("graph1", new Rectangle(547, 157, 42, 23), 3, Page.Main),
        new("graph2", new Rectangle(547, 217, 42, 23), 3, Page.Main),
        new("graph3", new Rectangle(547, 277, 42, 23), 3, Page.Main),
        new("ungraph", new Rectangle(547, 277, 42, 23), 2, Page.Main),
        new("statement", new Rectangle(230, 100, 160, 300), 0, Page.Main),
        new("main", new Rectangle(0, 0, 640, 440), 0, Page.Statement),
    ];

    private readonly long _opened = Environment.TickCount64;
    private bool _statement;
    private int? _graph;
    private string? _pressed;

    /// <summary>Whether the original's graphics for it are there.</summary>
    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Picture("BANK01V") is not null && graphics.Picture("STAT01V") is not null
        && graphics.Sheet("BANK02V", Palette) is not null && graphics.Font("FONT36V", Palette) is not null
        && graphics.Font("FONT37V", StatPalette) is not null;

    /// <summary>
    /// The face's sprites after <paramref name="ticks"/> of UIBankManager:onTick: the eyes blink (7, 8), the brows lift
    /// (9 to 11) and the mouth smiles (12 to 15) at set points of a cycle of 421 ticks.
    /// </summary>
    public static (int Eyes, int Brows, int Smile) Face(long ticks)
    {
        int eyes = 7, brows = 9, smile = 12, eyesClock = 0, browsClock = 0, smileClock = 0;
        var counter = (int)(ticks % 421);
        for (var c = 1; c <= counter; c++)
        {
            if (c is (>= 24 and < 36) or (>= 49 and < 55) or (>= 70 and < 76) or (>= 164 and < 176) or (>= 189 and < 195) or (>= 219 and < 225) or (>= 340 and < 352))
            {
                if (++eyesClock > 2)
                {
                    eyesClock = 0;
                    eyes = eyes == 8 ? 7 : eyes + 1;
                }
            }
            else if (c is (>= 88 and < 100) or (>= 248 and < 260) or (>= 298 and < 322))
            {
                if (++browsClock > 3)
                {
                    browsClock = 0;
                    brows = brows == 11 ? 9 : brows + 1;
                }
            }
            else if (c is (>= 132 and < 140) or (>= 272 and < 280))
            {
                if (++smileClock > 3)
                {
                    smileClock = 0;
                    smile = smile == 15 ? 12 : smile + 1;
                }
            }
        }

        return (eyes, brows, smile);
    }

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        var graphics = host.Graphics!;
        var hospital = host.Simulation.Hospitals[host.Player];
        var local = new Point(mouse.X, mouse.Y) - origin;
        if (_statement)
        {
            batch.Draw(graphics.Picture("STAT01V")!, origin.ToVector2(), Color.White);
            DrawPressed(batch, graphics, origin, local, mouse);
            DrawStatement(batch, host, graphics.Font("FONT37V", StatPalette)!, origin, hospital);
            return;
        }

        batch.Draw(graphics.Picture("BANK01V")!, origin.ToVector2(), Color.White);
        var sheet = graphics.Sheet("BANK02V", Palette)!;
        var (eyes, brows, smile) = Face((Environment.TickCount64 - _opened) / TickMilliseconds);
        sheet.Draw(batch, smile, origin.X + 303, origin.Y + 199, Color.White);
        sheet.Draw(batch, eyes, origin.X + 298, origin.Y + 173, Color.White);
        sheet.Draw(batch, brows, origin.X + 296, origin.Y + 165, Color.White);
        if (_graph is not null)
        {
            sheet.Draw(batch, 1, origin.X + 417, origin.Y + 150, Color.White);
        }

        DrawPressed(batch, graphics, origin, local, mouse);
        var font = graphics.Font("FONT36V", Palette)!;
        void Centred(string text, int x, int y, int width) =>
            font.Draw(batch, text, new Vector2(origin.X + x + ((width - font.Measure(text).Width) / 2), origin.Y + y));

        Centred(Text(host, 36, 1, "Hospital value"), 60, 109, 143);
        Centred(Money(hospital.Value), 60, 139, 143);
        Centred(Text(host, 36, 2, "Balance"), 60, 174, 143);
        Centred(Money(hospital.Balance), 60, 204, 143);
        Centred(Text(host, 36, 3, "Current loan"), 60, 239, 143);
        Centred(Money(hospital.Loan), 60, 269, 143);
        Centred(Text(host, 36, 4, "Interest payment"), 60, 305, 143);
        Centred(Money(hospital.Loan * hospital.InterestRate / 10000 / 12), 60, 334, 143);

        Centred(Text(host, 36, 5, "Insurance owed"), 430, 102, 158);
        if (_graph is { } chosen)
        {
            Centred(Company(host, chosen), 430, 132, 158);
        }
        else
        {
            for (var company = 1; company <= Hospital.InsuranceCompanies; company++)
            {
                Centred(Company(host, company), 430, 72 + (60 * company), 158);
                Centred(Money(hospital.InsuranceOwed(company)), 430, 102 + (60 * company), 100);
            }
        }

        Centred(Text(host, 36, 6, "Inflation rate"), 430, 312, 100);
        Centred(Percent(Hospital.InflationRate * 100), 550, 313, 38);
        Centred(Text(host, 36, 7, "Interest rate"), 430, 342, 100);
        Centred(Percent(hospital.InterestRate / 100m), 550, 342, 38);
    }

    /// <summary>A button pressed and let go over it does its job (CorsixTH's buttons act on the release).</summary>
    public void Mouse(IWindowHost host, UiWindow window, Point local, MouseState mouse, MouseState previous)
    {
        var under = ButtonAt(local)?.Key;
        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = under;
            return;
        }

        if (mouse.LeftButton == ButtonState.Released && previous.LeftButton == ButtonState.Pressed)
        {
            var pressed = _pressed;
            _pressed = null;
            if (pressed is not null && pressed == under)
            {
                Click(host, window, pressed);
            }
        }
    }

    public string? Tooltip(IWindowHost host, Point local)
    {
        string Tip(int index, string fallback) => OriginalTooltips.Get(host, index, fallback);
        if (ButtonAt(local)?.Key is { } key)
        {
            return key switch
            {
                "close" => _statement ? Tip(162, "Close the statement") : Tip(218, "Close the bank manager"),
                "borrow" => Tip(214, "Borrow $5,000 from the bank"),
                "repay" => Tip(213, "Repay $5,000 of the loan"),
                "graph1" or "graph2" or "graph3" => Format(Tip(220, "Show the graph of %s"), Company(host, key[^1] - '0')),
                "ungraph" => Tip(222, "Back to the companies"),
                "town" => "Town map",
                _ => null,
            };
        }

        if (_statement)
        {
            return null;
        }

        // makeTooltip's areas, from their corners.
        (int Index, string Fallback, Rectangle Area)[] areas =
        [
            (210, "The value of your hospital", Corners(60, 105, 203, 157)),
            (211, "Your balance", Corners(60, 170, 203, 222)),
            (212, "Your current loan", Corners(60, 235, 203, 287)),
            (215, "The interest you pay each month", Corners(60, 300, 203, 352)),
            (216, "The yearly inflation rate", Corners(430, 308, 589, 331)),
            (217, "The bank's yearly interest rate", Corners(430, 337, 589, 360)),
        ];
        foreach (var (index, fallback, area) in areas)
        {
            if (area.Contains(local))
            {
                return Tip(index, fallback);
            }
        }

        for (var company = 1; company <= Hospital.InsuranceCompanies && _graph is null; company++)
        {
            if (Corners(430, 68 + (60 * company), 589, 120 + (60 * company)).Contains(local))
            {
                return Format(Tip(219, "What %s owes you"), Company(host, company));
            }
        }

        return null;
    }

    private void Click(IWindowHost host, UiWindow window, string key)
    {
        var keyboard = InputScript.Keyboard();
        var all = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        var hospital = host.Simulation.Hospitals[host.Player];
        switch (key)
        {
            case "close":
                host.PlaySound("no4.wav");
                host.Close(window);
                break;
            case "town":
                host.PlaySound(WindowLayer.DefaultButtonSound);
                host.Open(new TownMapWindow());
                break;
            case "borrow":
                // UIBankManager:increaseLoan: Wrong2.wav when the bank will not lend more.
                var canBorrow = hospital.Loan + Hospital.LoanStep <= hospital.MaxLoan;
                host.PlaySound(canBorrow ? WindowLayer.DefaultButtonSound : "wrong2.wav");
                if (canBorrow)
                {
                    host.Submit(new BorrowCommand(all));
                }

                break;
            case "repay":
                var canRepay = hospital.Loan > 0 && hospital.Balance >= Math.Min(Hospital.LoanStep, hospital.Loan);
                host.PlaySound(canRepay ? WindowLayer.DefaultButtonSound : "wrong2.wav");
                if (canRepay)
                {
                    host.Submit(new RepayCommand(all));
                }

                break;
            case "graph1" or "graph2" or "graph3":
                host.PlaySound(WindowLayer.DefaultButtonSound);
                _graph = key[^1] - '0';
                break;
            case "ungraph":
                host.PlaySound(WindowLayer.DefaultButtonSound);
                _graph = null;
                break;
            case "statement":
                host.PlaySound(WindowLayer.DefaultButtonSound);
                _statement = true;
                _graph = null;
                break;
            case "main":
                host.PlaySound(WindowLayer.DefaultButtonSound);
                _statement = false;
                break;
        }
    }

    /// <summary>The statement page: the titles, the last transactions, oldest at the top, and the balance.</summary>
    private static void DrawStatement(SpriteBatch batch, IWindowHost host, TextRenderer font, Point origin, Hospital hospital)
    {
        void At(string text, int x, int y) => font.Draw(batch, text, new Vector2(origin.X + x, origin.Y + y));
        void Centred(string text, int x, int y, int width) => At(text, x + ((width - font.Measure(text).Width) / 2), y);

        Centred(Text(host, 37, 1, "Date"), 44, 37, 65);
        Centred(Text(host, 37, 2, "Details"), 125, 40, 230);
        Centred(Text(host, 37, 3, "Money out"), 373, 42, 70);
        Centred(Text(host, 37, 4, "Money in"), 449, 41, 70);
        Centred(Text(host, 37, 5, "Balance"), 525, 40, 70);
        var lines = hospital.Transactions.Take(Hospital.LogLength).Reverse().ToList();
        for (var no = 1; no <= lines.Count; no++)
        {
            var transaction = lines[no - 1];
            var y = (no * 15) + 60;
            var date = GameDate.FromHours(transaction.Hour);
            At(string.Create(CultureInfo.InvariantCulture, $"{date.Day} {host.Texts?.String(6, date.Month) ?? CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(date.Month)}"), 48, y);
            At(MoneyPanels.Describe(transaction), 129, y);
            At(Money(Math.Abs((long)transaction.Amount)), transaction.Amount < 0 ? 377 : 453, y);
            At(Money(transaction.Balance), 529, y);
        }

        Centred(Text(host, 37, 6, "Current balance"), 373, 420, 140);
        Centred(string.Create(CultureInfo.InvariantCulture, $"${hospital.Balance}"), 526, 421, 70);
    }

    private void DrawPressed(SpriteBatch batch, OriginalGraphics graphics, Point origin, Point local, MouseState mouse)
    {
        if (mouse.LeftButton == ButtonState.Pressed && _pressed is { } pressed && ButtonAt(local) is { Sprite: > 0 } button && button.Key == pressed)
        {
            graphics.Sheet("BANK02V", Palette)!.Draw(batch, button.Sprite, origin.X + button.Area.X, origin.Y + button.Area.Y, Color.White);
        }
    }

    private Button? ButtonAt(Point local)
    {
        var page = _statement ? Page.Statement : Page.Main;
        return Buttons.FirstOrDefault(button => button.Page == page && button.Area.Contains(local) && button.Key switch
        {
            "graph1" or "graph2" or "graph3" => _graph is null,
            "ungraph" => _graph is not null,
            _ => true,
        });
    }

    private static string Text(IWindowHost host, int section, int index, string fallback) => host.Texts?.String(section, index) ?? fallback;

    /// <summary>The insurance company's name: the first three of the original's (S[20]), as CorsixTH picks them.</summary>
    private static string Company(IWindowHost host, int company) =>
        host.Texts?.String(20, company + 1) ?? string.Create(CultureInfo.InvariantCulture, $"Company {company}");

    private static string Money(long amount) => string.Create(CultureInfo.InvariantCulture, $"$ {amount}");

    private static string Percent(decimal value) => string.Create(CultureInfo.InvariantCulture, $"{value:0.##} %");

    private static string Format(string template, string name) => template.Replace("%s", name, StringComparison.Ordinal);

    private static Rectangle Corners(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);

    private enum Page
    {
        Main,
        Statement,
    }

    private sealed record Button(string Key, Rectangle Area, int Sprite, Page Page);
}
