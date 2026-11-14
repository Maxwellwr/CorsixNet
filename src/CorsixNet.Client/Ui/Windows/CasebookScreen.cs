// The drug casebook as the original draws it, ported from CorsixTH CorsixTH/Lua/dialogs/fullscreen/drug_casebook.lua
// (UICasebook: its buttons, tooltips, updateDiseaseList, updateIcons, draw, scrolling, prices, onMouseDown, onTick).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Client.Input;
using CorsixNet.Client.Rendering;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Rooms;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The fullscreen casebook over DrugN01V: the known diseases (diagnoses first, then by name) on the right with the
/// chosen one in the middle; its reputation, charge (as a percentage for a moment after a change), earnings, cures,
/// deaths and patients sent home; the cure's kind and whether the hospital can cure it now; the research button.
/// </summary>
internal sealed class CasebookScreen
{
    private const string Palette = "DRUGN01V";

    /// <summary>How long the charge shows as a percentage after a change or a new choice (onTick's 50 ticks).</summary>
    private const long PercentMilliseconds = 50 * 1000 / 18;

    /// <summary>How soon a held button repeats, and then how often (makeRepeatButton).</summary>
    private const long RepeatDelay = 300;
    private const long RepeatEvery = 80;

    private static readonly Button[] Buttons =
    [
        new("close", new Rectangle(607, 449, 26, 26), 3),
        new("up", new Rectangle(439, 29, 70, 46), 1, Repeats: true),
        new("down", new Rectangle(437, 394, 77, 53), 2, Repeats: true),
        new("increase", new Rectangle(354, 133, 22, 22), 5, Repeats: true),
        new("decrease", new Rectangle(237, 133, 22, 22), 4, Repeats: true),
        new("research", new Rectangle(235, 400, 140, 20), 0),
    ];

    /// <summary>The disease chosen last, kept between openings (<c>ui.casebook_selected_disease</c>).</summary>
    private static string? _chosen;

    private string? _pressed;
    private long _nextRepeat;
    private long _percentUntil;

    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Picture("DRUGN01V") is not null && graphics.Sheet("DRUGN02V", Palette) is not null
        && graphics.Font("FONT25V", Palette) is not null && graphics.Font("FONT26V", Palette) is not null && graphics.Font("FONT24V", Palette) is not null;

    /// <summary><c>updateDiseaseList</c>: the discovered diseases, the pseudo ones (diagnoses) first, then by name.</summary>
    public static List<DiseaseDefinition> Known(IWindowHost host)
    {
        var hospital = host.Simulation.Hospitals[host.Player];
        var rules = host.Simulation.Rules.Diseases;
        return DiseaseCatalog.All
            .Where(disease => hospital.PeekCase(disease, rules).Discovered)
            .OrderByDescending(disease => disease.IsPseudo)
            .ThenBy(disease => Name(host, disease).ToUpperInvariant(), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The kind of cure: its sprite (6 machine, 7 drug, 8 surgery, 10 psychiatry), from the treatment rooms.</summary>
    public static int CureSprite(DiseaseDefinition disease) =>
        disease.TreatmentRooms.Contains("pharmacy") ? 7
        : disease.TreatmentRooms.Contains("operating_theatre") ? 8
        : disease.TreatmentRooms.Contains("psych") ? 10
        : 6;

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        var graphics = host.Graphics!;
        var simulation = host.Simulation;
        var hospital = simulation.Hospitals[host.Player];
        var known = Known(host);
        var index = Selected(known);
        batch.Draw(graphics.Picture("DRUGN01V")!, origin.ToVector2(), Color.White);
        var sheet = graphics.Sheet("DRUGN02V", Palette)!;
        var local = new Point(mouse.X, mouse.Y) - origin;
        var under = ButtonAt(local);
        if (mouse.LeftButton == ButtonState.Pressed && _pressed is { } pressed && under is { Sprite: > 0 } button && button.Key == pressed)
        {
            sheet.Draw(batch, button.Sprite, origin.X + button.Area.X, origin.Y + button.Area.Y, Color.White);
        }

        var titles = graphics.Font("FONT25V", Palette)!;
        var selectedFont = graphics.Font("FONT26V", Palette)!;
        void At(TextRenderer font, string text, int x, int y) => font.Draw(batch, text, new Vector2(origin.X + x, origin.Y + y));
        void Centred(string text, int x, int y, int width) => At(titles, text, x + ((width - titles.Measure(text).Width) / 2), y);

        At(titles, Text(host, 29, 1, "Reputation"), 278, 68);
        At(titles, Text(host, 29, 2, "Treatment charge"), 260, 113);
        At(titles, Text(host, 29, 3, "Money earned"), 265, 157);
        At(titles, Text(host, 29, 4, "Cured"), 276, 201);
        At(titles, Text(host, 29, 5, "Deaths"), 279, 245);
        At(titles, Text(host, 29, 6, "Sent home"), 270, 289);
        At(titles, Text(host, 29, 8, "Cure"), 255, 354);
        if (index < 0)
        {
            return;
        }

        var disease = known[index];
        var entry = hospital.PeekCase(disease, simulation.Rules.Diseases);
        if (hospital.Research.CanConcentrate(simulation, hospital, disease))
        {
            var concentrated = string.Equals(hospital.Research.Concentrated, disease.Id, StringComparison.Ordinal);
            At(concentrated ? selectedFont : titles, Text(host, 29, 7, "Research"), 245, 398);
        }

        Centred(Math.Clamp(entry.Reputation ?? hospital.Reputation, Hospital.MinReputation, Hospital.MaxReputation).ToString(CultureInfo.InvariantCulture), 248, 92, 114);
        var charge = Environment.TickCount64 < _percentUntil
            ? string.Create(CultureInfo.InvariantCulture, $"{entry.PricePercent}%")
            : string.Create(CultureInfo.InvariantCulture, $"${hospital.TreatmentPrice(disease, simulation.Rules.Diseases)}");
        Centred(charge, 262, 137, 90);
        Centred(string.Create(CultureInfo.InvariantCulture, $"${entry.MoneyEarned}"), 248, 181, 114);
        Centred(entry.Recoveries.ToString(CultureInfo.InvariantCulture), 248, 225, 114);
        Centred(entry.Fatalities.ToString(CultureInfo.InvariantCulture), 248, 269, 114);
        Centred(entry.TurnedAway.ToString(CultureInfo.InvariantCulture), 248, 313, 114);

        // updateIcons: the cure's kind (unknown while a room of it is not discovered), and whether it can be cured now.
        var discovered = disease.TreatmentRooms.All(hospital.IsRoomDiscovered);
        var kind = discovered ? CureSprite(disease) : 9;
        if (!disease.IsPseudo || kind != 6)
        {
            sheet.Draw(batch, kind, origin.X + 306, origin.Y + 352, Color.White);
        }

        if (kind == 7)
        {
            var drugFont = graphics.Font("FONT24V", Palette)!;
            var percent = entry.CureEffectiveness.ToString(CultureInfo.InvariantCulture);
            At(drugFont, percent, 313 + ((16 - drugFont.Measure(percent).Width) / 2), 364);
        }

        if (!disease.IsPseudo)
        {
            sheet.Draw(batch, MissingRooms(host, disease).Count == 0 ? 11 : 12, origin.X + 335, origin.Y + 352, Color.White);
        }

        // The list: up to seven names above the chosen one, it in the middle, up to seven below.
        for (var i = 1; i <= 7 && index - i >= 0; i++)
        {
            At(titles, Name(host, known[index - i]).ToUpperInvariant(), 409, 203 - (i * 18));
        }

        At(selectedFont, Name(host, disease).ToUpperInvariant(), 409, 227);
        for (var i = 1; i <= 7 && index + i < known.Count; i++)
        {
            At(titles, Name(host, known[index + i]).ToUpperInvariant(), 409, 251 + (i * 18));
        }
    }

    public void Mouse(IWindowHost host, UiWindow window, Point local, MouseState mouse, MouseState previous)
    {
        var now = Environment.TickCount64;
        var wheel = mouse.ScrollWheelValue - previous.ScrollWheelValue;
        if (wheel != 0)
        {
            Scroll(host, wheel > 0 ? -1 : 1);
        }

        var under = ButtonAt(local);
        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = under?.Key;
            if (under is { Repeats: true })
            {
                // makeRepeatButton: it acts at once, then again while held.
                Act(host, window, under.Key);
                _nextRepeat = now + RepeatDelay;
            }
            else if (under is null)
            {
                ClickList(host, local);
            }

            return;
        }

        if (mouse.LeftButton == ButtonState.Pressed && under is { Repeats: true } held && held.Key == _pressed && now >= _nextRepeat)
        {
            Act(host, window, held.Key);
            _nextRepeat = now + RepeatEvery;
        }

        if (mouse.LeftButton == ButtonState.Released && previous.LeftButton == ButtonState.Pressed)
        {
            var pressed = _pressed;
            _pressed = null;
            if (pressed is not null && under is { Repeats: false } released && released.Key == pressed)
            {
                Act(host, window, pressed);
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
                "close" => Tip(138, "Close the casebook"),
                "up" => Tip(136, "Scroll up"),
                "down" => Tip(137, "Scroll down"),
                "increase" => Tip(146, "Raise the charge"),
                "decrease" => Tip(145, "Lower the charge"),
                _ => Tip(147, "Concentrate research on treating this disease"),
            };
        }

        (int Index, string Fallback, Rectangle Area)[] areas =
        [
            (139, "The disease's reputation", Corners(249, 72, 362, 117)),
            (140, "The treatment charge", Corners(249, 117, 362, 161)),
            (141, "The money earned", Corners(247, 161, 362, 205)),
            (142, "The patients cured", Corners(247, 205, 362, 249)),
            (143, "The deaths", Corners(247, 249, 362, 293)),
            (144, "The patients sent home", Corners(247, 293, 362, 337)),
        ];
        foreach (var (index, fallback, area) in areas)
        {
            if (area.Contains(local))
            {
                return Tip(index, fallback);
            }
        }

        var known = Known(host);
        var chosen = Selected(known);
        if (chosen < 0)
        {
            return null;
        }

        var disease = known[chosen];
        var hospital = host.Simulation.Hospitals[host.Player];
        if (new Rectangle(306, 352, 29, 29).Contains(local) && (!disease.IsPseudo || CureSprite(disease) != 6))
        {
            return !disease.TreatmentRooms.All(hospital.IsRoomDiscovered) ? "Unknown cure"
                : CureSprite(disease) switch
                {
                    7 => Tip(149, "Drug, %d%% effective").Replace("%d", hospital.PeekCase(disease, host.Simulation.Rules.Diseases).CureEffectiveness.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("%%", "%", StringComparison.Ordinal),
                    8 => Tip(151, "Surgery"),
                    10 => Tip(150, "Psychiatry"),
                    _ => Tip(152, "Machine"),
                };
        }

        if (new Rectangle(335, 352, 29, 29).Contains(local) && !disease.IsPseudo)
        {
            var missing = MissingRooms(host, disease);
            if (missing.Count == 0)
            {
                return Tip(153, "You can treat this disease");
            }

            var names = string.Join(", ", missing.Select(room => host.Simulation.Content.FindRoom(room)?.Name ?? room));
            return string.Create(CultureInfo.InvariantCulture, $"{Tip(155, "Build")} ({names}).");
        }

        return null;
    }

    /// <summary>The treatment rooms of the disease the hospital has no finished room of.</summary>
    private static List<string> MissingRooms(IWindowHost host, DiseaseDefinition disease)
    {
        var simulation = host.Simulation;
        var built = simulation.Entities.OfType<Room>()
            .Where(room => room.IsActive && RoomBuilder.IsOwnedBy(simulation.Map, room, host.Player))
            .Select(room => room.RoomType)
            .ToHashSet(StringComparer.Ordinal);
        return disease.TreatmentRooms.Where(room => !built.Contains(room)).ToList();
    }

    private void Act(IWindowHost host, UiWindow window, string key)
    {
        var keyboard = InputScript.Keyboard();
        var ctrl = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        var shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        switch (key)
        {
            case "close":
                host.PlaySound("no4.wav");
                host.Close(window);
                break;
            case "up":
                Scroll(host, ctrl ? -int.MaxValue / 2 : -1);
                break;
            case "down":
                Scroll(host, ctrl ? int.MaxValue / 2 : 1);
                break;
            case "increase" or "decrease":
                ChangePrice(host, (key == "increase" ? 1 : -1) * (ctrl ? 25 : shift ? 5 : 1));
                break;
            case "research":
                var known = Known(host);
                var chosen = Selected(known);
                var hospital = host.Simulation.Hospitals[host.Player];
                if (chosen >= 0 && hospital.Research.CanConcentrate(host.Simulation, hospital, known[chosen]))
                {
                    host.PlaySound(WindowLayer.DefaultButtonSound);
                    host.Submit(new ConcentrateResearchCommand(known[chosen].Id));
                }

                break;
        }
    }

    /// <summary>scrollUp and scrollDown: pagetur2.wav, or Wrong2.wav at the end of the list.</summary>
    private void Scroll(IWindowHost host, int by)
    {
        var known = Known(host);
        var index = Selected(known);
        var target = Math.Clamp((long)index + by, 0, known.Count - 1);
        if (index < 0 || target == index)
        {
            host.PlaySound("wrong2.wav");
            return;
        }

        Choose(known[(int)target]);
        host.PlaySound("pagetur2.wav");
    }

    /// <summary>increasePay and decreasePay: from 50 to 200 percent, Wrong2.wav at the ends.</summary>
    private void ChangePrice(IWindowHost host, int by)
    {
        var known = Known(host);
        var index = Selected(known);
        if (index < 0)
        {
            return;
        }

        var disease = known[index];
        var price = host.Simulation.Hospitals[host.Player].PeekCase(disease, host.Simulation.Rules.Diseases).PricePercent + by;
        host.PlaySound(price is < DiseaseCase.MinPrice or > DiseaseCase.MaxPrice ? "wrong2.wav" : WindowLayer.DefaultButtonSound);
        host.Submit(new SetTreatmentPriceCommand(disease.Id, Math.Clamp(price, DiseaseCase.MinPrice, DiseaseCase.MaxPrice)));
        _percentUntil = Environment.TickCount64 + PercentMilliseconds;
    }

    /// <summary>onMouseDown on the list: a name above or below the chosen one is chosen.</summary>
    private void ClickList(IWindowHost host, Point local)
    {
        if (local.X < 395 || local.X > 540 || local.Y < 77 || local.Y > 394)
        {
            return;
        }

        int by;
        if (local.Y < 203)
        {
            by = -7 + ((local.Y - 77) / 18);
        }
        else if (local.Y > 269)
        {
            by = ((local.Y - 269) / 18) + 1;
        }
        else
        {
            return;
        }

        var known = Known(host);
        var index = Selected(known) + by;
        if (index >= 0 && index < known.Count)
        {
            Choose(known[index]);
            host.PlaySound("pagetur2.wav");
        }
    }

    /// <summary>Opens the casebook at a disease next time (<c>UICasebook(ui, disease_selection)</c>).</summary>
    public static void Remember(string diseaseId) => _chosen = diseaseId;

    private void Choose(DiseaseDefinition disease)
    {
        _chosen = disease.Id;
        _percentUntil = Environment.TickCount64 + PercentMilliseconds;
    }

    /// <summary>The chosen disease's place in the list: the one chosen last, else the last one.</summary>
    private static int Selected(List<DiseaseDefinition> known)
    {
        var index = known.FindIndex(disease => disease.Id == _chosen);
        return index >= 0 ? index : known.Count - 1;
    }

    private static Button? ButtonAt(Point local) => Buttons.FirstOrDefault(button => button.Area.Contains(local));

    private static string Name(IWindowHost host, DiseaseDefinition disease) => host.Texts?.DiseaseName(disease.Id) ?? disease.Id;

    private static string Text(IWindowHost host, int section, int index, string fallback) => host.Texts?.String(section, index) ?? fallback;

    private static Rectangle Corners(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);

    private sealed record Button(string Key, Rectangle Area, int Sprite, bool Repeats = false);
}
