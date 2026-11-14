// The progress report as the original draws it, ported from CorsixTH
// CorsixTH/Lua/dialogs/fullscreen/progress_report.lua (UIProgressReport: the criteria icons and bars, the hospitals, the
// markers and warnings, the tooltips).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Client.Rendering;
using CorsixNet.Sim;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The fullscreen progress report over Rep01V: the year, the hospitals (the player's in red), the share of the town's
/// patients, a column for each goal of the report (a bar of how far it is met, red for a lose goal, under its icon),
/// the markers for the patients' happiness, thirst and warmth, and a warning when one of them is bad.
/// </summary>
internal sealed class ProgressReportScreen
{
    private const string Palette = "REP01V";
    private static readonly Rectangle Close = new(606, 447, 26, 26);
    private bool _closePressed;

    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Picture("REP01V") is not null && graphics.Sheet("REP02V", Palette) is not null
        && graphics.Font("FONT100V", Palette) is not null && graphics.Font("FONT101V", Palette) is not null;

    /// <summary>A goal's icon: Rep02V's 10 to 15 for most, the happy cursors for happiness, a figure for the months.</summary>
    public static (string Sheet, int Sprite) Icon(EndCriterion criterion) => criterion switch
    {
        EndCriterion.Reputation => ("REP02V", 10),
        EndCriterion.Balance => ("REP02V", 11),
        EndCriterion.PercentageCured => ("REP02V", 12),
        EndCriterion.NumCured => ("REP02V", 13),
        EndCriterion.PercentageKilled => ("REP02V", 14),
        EndCriterion.Value => ("REP02V", 15),
        EndCriterion.StaffHappiness => ("MPOINTER", 16),
        EndCriterion.PatientHappiness => ("MPOINTER", 18),
        _ => ("FONT04V", 46),
    };

    /// <summary>How tall a goal's bar is, from 1 to 50 pixels (<c>UIProgressReport:draw</c>).</summary>
    public static int BarHeight(ProgressEntry entry, decimal now)
    {
        var goal = entry.Goal;
        var height = entry.IsLoseGoal
            ? goal.Bound == goal.Value ? 50m : 1 + (49 * (1 - ((now - goal.Value) / (goal.Bound - goal.Value))))
            : goal.Value == 0 ? 50m : 1 + (49 * (now / goal.Value));
        return (int)Math.Clamp(Math.Floor(height), 0, 50);
    }

    /// <summary><c>UIPatient.normaliseWarmth</c>: 0.08 and below is 0, 0.5 and above is 1.</summary>
    public static decimal NormaliseWarmth(decimal? warmth) =>
        warmth is not { } value ? 0.5m : value < 0.08m ? 0 : value > 0.5m ? 1 : (value - 0.08m) / (0.5m - 0.08m);

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        var graphics = host.Graphics!;
        var simulation = host.Simulation;
        var hospital = simulation.Hospitals[host.Player];
        batch.Draw(graphics.Picture("REP01V")!, origin.ToVector2(), Color.White);
        var sheet = graphics.Sheet("REP02V", Palette)!;
        var normal = graphics.Font("FONT100V", Palette)!;
        var red = graphics.Font("FONT101V", Palette)!;
        var small = graphics.Font("FONT106V", Palette) ?? normal;
        var local = new Point(mouse.X, mouse.Y) - origin;
        if (mouse.LeftButton == ButtonState.Pressed && _closePressed && Close.Contains(local))
        {
            sheet.Draw(batch, 8, origin.X + Close.X, origin.Y + Close.Y, Color.White);
        }

        void At(TextRenderer font, string text, int x, int y) => font.Draw(batch, text, new Vector2(origin.X + x, origin.Y + y));

        // The hospitals, the player's (selected) in red.
        for (var i = 0; i < simulation.Hospitals.Count; i++)
        {
            At(i == host.Player ? red : normal, HospitalName(host, i).ToUpperInvariant(), 272, 73 + (i * 25));
        }

        // A column for each goal.
        var x = 263;
        foreach (var entry in simulation.Rules.EndConditions.ProgressReport(simulation, hospital))
        {
            var (sheetName, sprite) = Icon(entry.Goal.Criterion);
            var icons = sheetName == "REP02V" ? sheet : graphics.Sheet(sheetName, Palette);
            icons?.Draw(batch, sprite, origin.X + x, origin.Y + 240, Color.White);
            var now = EndConditions.Measure(simulation, hospital, entry.Goal.Criterion) ?? 0;
            var height = BarHeight(entry, now);
            var offset = entry.IsLoseGoal ? 2 : 0;
            for (var dy = 0; dy < height; dy++)
            {
                sheet.Draw(batch, 1 + offset, origin.X + x + 7, origin.Y + 237 - dy, Color.White);
            }

            sheet.Draw(batch, 2 + offset, origin.X + x + 7, origin.Y + 237 - height, Color.White);
            x += 30;
        }

        // drawMarkers: the patients' happiness, thirst and warmth between x 455 and 551.
        var (happiness, thirst, warmth) = PatientFeelings(simulation, host.Player);
        foreach (var (value, y) in new[] { (happiness, 193), (thirst, 223), (warmth, 254) })
        {
            sheet.Draw(batch, 5, origin.X + 455 + (int)Math.Floor(96 * value), origin.Y + y, Color.White);
        }

        var warning = warmth < 0.3m ? (6, "Your patients are too cold. Put more radiators in.")
            : warmth > 0.7m ? (5, "Your patients are too hot. Turn the heating down.")
            : thirst > 0.7m ? (4, "Your patients are thirsty. Put more drinks machines in.")
            : happiness < 0.6m ? (2, "Your patients are very unhappy.")
            : happiness < 0.8m ? (3, "Your patients are quite unhappy.")
            : ((int, string)?)null;
        if (warning is { } shown)
        {
            sheet.Draw(batch, 7, origin.X + 252, origin.Y + 295, Color.White);
            var lines = Maps.MapViewScreen.WrapToWidth(host.Texts?.String(60, shown.Item1) ?? shown.Item2, 285, text => normal.Measure(text).Width);
            for (var i = 0; i < lines.Count; i++)
            {
                At(normal, lines[i], 285, 285 + (i * normal.LineHeight));
            }
        }

        var header = string.Create(CultureInfo.InvariantCulture, $"{host.Texts?.String(60, 1) ?? "Progress report"} {simulation.Date.Year + 1999}");
        At(normal, header, 227 + ((400 - normal.Measure(header).Width) / 2), 40);
        At(small, (host.Texts?.String(60, 8) ?? "Win criteria").ToUpperInvariant(), 263, 172);
        At(small, string.Create(CultureInfo.InvariantCulture, $"{(host.Texts?.String(60, 7) ?? "Percentage of population").ToUpperInvariant()} {PopulationShare(simulation, host.Player)}%"), 450, 65);
    }

    public void Mouse(IWindowHost host, UiWindow window, Point local, MouseState mouse, MouseState previous)
    {
        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _closePressed = Close.Contains(local);
        }
        else if (mouse.LeftButton == ButtonState.Released && previous.LeftButton == ButtonState.Pressed)
        {
            if (_closePressed && Close.Contains(local))
            {
                host.PlaySound("no4.wav");
                host.Close(window);
            }

            _closePressed = false;
        }
    }

    public static string? Tooltip(IWindowHost host, Point local)
    {
        string Tip(int index, string fallback) => OriginalTooltips.Get(host, index, fallback);
        if (Close.Contains(local))
        {
            return Tip(229, "Close the progress report");
        }

        var simulation = host.Simulation;
        var hospital = simulation.Hospitals[host.Player];
        var x = 263;
        foreach (var entry in simulation.Rules.EndConditions.ProgressReport(simulation, hospital))
        {
            if (new Rectangle(x, 180, 30, 90).Contains(local))
            {
                var goal = entry.Goal;
                return ProgressReportWindow.Tooltip(host, goal.Criterion, entry.IsLoseGoal, goal.Value, EndConditions.Measure(simulation, hospital, goal.Criterion) ?? 0);
            }

            x += 30;
        }

        return new Rectangle(265, 71, 147, 20).Contains(local) ? Tip(223, "Your hospital's progress towards winning")
            : new Rectangle(433, 179, 145, 30).Contains(local) ? Tip(226, "Your patients' average happiness")
            : new Rectangle(433, 209, 145, 30).Contains(local) ? Tip(227, "How thirsty your patients are on average")
            : new Rectangle(433, 239, 145, 31).Contains(local) ? Tip(228, "How warm your patients are on average")
            : null;
    }

    /// <summary>
    /// The averages drawMarkers shows: happiness, one less the thirst (as CorsixTH has it) and the normalised warmth; a
    /// half each with nobody.
    /// </summary>
    private static (decimal Happiness, decimal Thirst, decimal Warmth) PatientFeelings(Simulation simulation, int player)
    {
        var patients = simulation.Entities.OfType<Humanoid>()
            .Where(h => h.Behaviour is PatientBehaviour && h.HospitalPlayer == player && InHospital(simulation.Map, player, h.Tile))
            .ToList();
        if (patients.Count == 0)
        {
            return (0.5m, 0.5m, 0.5m);
        }

        var happiness = patients.Average(h => h.Happiness);
        var thirst = 1 - patients.Average(h => ((PatientBehaviour)h.Behaviour!).Thirst);
        var warmth = NormaliseWarmth(patients.Average(h => h.Warmth));
        return (happiness, thirst, warmth);
    }

    /// <summary>The player's share of the patients who came this month, an equal share when nobody came.</summary>
    private static int PopulationShare(Simulation simulation, int player)
    {
        var total = simulation.Hospitals.Sum(h => h.Statistics.Count == 0 ? 0 : h.Statistics[^1].Visitors);
        var own = simulation.Hospitals[player].Statistics is { Count: > 0 } months ? months[^1].Visitors : 0;
        return total > 0 ? own * 100 / total : 100 / Math.Max(1, simulation.Hospitals.Count);
    }

    /// <summary>The hospital's name: the player's name for theirs, the original's competitor names (S[5]) for the others.</summary>
    private static string HospitalName(IWindowHost host, int index) =>
        index == host.Player ? host.PlayerName
        : host.Texts?.String(5, index switch { 1 => 1, 2 => 3, 3 => 2, _ => 4 }) ?? string.Create(CultureInfo.InvariantCulture, $"Hospital {index + 1}");

    private static bool InHospital(LevelMap map, int player, TilePosition tile) =>
        map.Contains(tile.X, tile.Y) && map[tile.X, tile.Y].Has(TileFlags.Hospital) && map.GetParcelOwner(map[tile.X, tile.Y].Parcel) == player + 1;
}
