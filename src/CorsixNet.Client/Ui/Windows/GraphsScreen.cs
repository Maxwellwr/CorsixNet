// The graphs as the original draws them, ported from CorsixTH CorsixTH/Lua/dialogs/fullscreen/graphs.lua (UIGraphs: its
// buttons, getHospitalStatistics, computeVerticalValuePosition, the labels' placing, draw, toggleGraphScale).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Sim.Economy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The fullscreen graphs over Graph01V: a line for each of the hospital's monthly figures (money in and out, wages,
/// balance, visitors, cures, deaths, reputation) over the last twelve months, years or four-year steps, each scaled
/// between its own lowest and highest value and 0, with its newest value labelled at the right; the pens switch lines
/// off and on, the scale button changes the time scale.
/// </summary>
internal sealed class GraphsScreen
{
    private const string Palette = "GRAPH01V";
    private const int TopY = 85;
    private const int BottomY = 353;
    private const int RightX = 346;
    private const int Spacing = 25;

    private static readonly Rectangle Close = new(63, 384, 26, 26);
    private static readonly Rectangle Scale = new(371, 384, 65, 26);
    private static readonly int[] PenY = [34, 86, 138, 190, 243, 295, 347, 400];

    /// <summary>Which lines are shown and the time scale, shared by every opening (CorsixTH's runtime config).</summary>
    private static readonly bool[] Shown = Enumerable.Repeat(true, 8).ToArray();
    private static int _scale = 3;

    private string? _pressed;

    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Picture("GRAPH01V") is not null && graphics.Sheet("GRAPH02V", Palette) is not null
        && graphics.Font("FONT01V", Palette) is not null && graphics.Font("FONT00V", Palette) is not null;

    /// <summary><c>computeVerticalValuePosition</c>: where a value goes between the top and bottom of the graph.</summary>
    public static int Y(long value, long minimum, long maximum) =>
        maximum == minimum ? BottomY : BottomY - (int)Math.Floor((double)(value - minimum) / (maximum - minimum) * (BottomY - TopY));

    /// <summary>
    /// updateTextPositions, simply: the labels sorted by where their line ends, each pushed down below the one above it
    /// and the lowest ones up again to stay above the bottom, though never over the top or one another.
    /// </summary>
    public static List<int> PlaceLabels(IReadOnlyList<int> ideal, int height)
    {
        var placed = ideal.Select(y => y - (height / 2)).ToList();
        for (var i = 1; i < placed.Count; i++)
        {
            placed[i] = Math.Max(placed[i], placed[i - 1] + height);
        }

        for (var i = placed.Count - 1; i >= 0; i--)
        {
            var limit = i == placed.Count - 1 ? BottomY - height : placed[i + 1] - height;
            placed[i] = Math.Max(TopY, Math.Min(placed[i], limit));
        }

        // Kept from the top: none above another when there is no room for all.
        for (var i = 1; i < placed.Count; i++)
        {
            placed[i] = Math.Max(placed[i], placed[i - 1] + height);
        }

        return placed;
    }

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        var graphics = host.Graphics!;
        var hospital = host.Simulation.Hospitals[host.Player];
        batch.Draw(graphics.Picture("GRAPH01V")!, origin.ToVector2(), Color.White);
        var sheet = graphics.Sheet("GRAPH02V", Palette)!;
        var white = graphics.Font("FONT01V", Palette)!;
        var black = graphics.Font("FONT00V", Palette)!;
        var local = new Point(mouse.X, mouse.Y) - origin;
        var held = mouse.LeftButton == ButtonState.Pressed;
        if (held && _pressed == "close" && Close.Contains(local))
        {
            sheet.Draw(batch, 3, origin.X + Close.X, origin.Y + Close.Y, Color.White);
        }

        if (held && _pressed == "scale" && Scale.Contains(local))
        {
            sheet.Draw(batch, 2, origin.X + Scale.X, origin.Y + Scale.Y, Color.White);
        }

        var lines = GraphWindow.Lines;
        for (var i = 0; i < lines.Length; i++)
        {
            // A pen is pressed in while its line is off (setToggleState(not config[option])).
            if (!Shown[i])
            {
                sheet.Draw(batch, 1, origin.X + 590, origin.Y + PenY[i], Color.White);
            }

            var name = host.Texts?.String(7, 4 + i) ?? lines[i].Name;
            var size = white.Measure(name);
            white.Draw(batch, name, new Vector2(origin.X + 502 + ((80 - size.Width) / 2), origin.Y + PenY[i] + 7 + ((27 - size.Height) / 2)));
        }

        var values = GraphWindow.Values(hospital.Statistics, _scale);
        if (values.Count == 0)
        {
            return;
        }

        // The lines, newest at the right.
        var labels = new List<(int Line, int Ideal, long Value)>();
        for (var line = 0; line < lines.Length; line++)
        {
            var value = lines[line].Value;
            var minimum = Math.Min(0, values.Min(value));
            var maximum = Math.Max(0, values.Max(value));
            if (Shown[line])
            {
                for (var i = 1; i < values.Count; i++)
                {
                    var from = new Vector2(origin.X + RightX - ((i - 1) * Spacing), origin.Y + Y(value(values[i - 1]), minimum, maximum));
                    var to = new Vector2(origin.X + RightX - (i * Spacing), origin.Y + Y(value(values[i]), minimum, maximum));
                    DrawLine(batch, graphics.Pixel, from, to, lines[line].Colour);
                }

                labels.Add((line, Y(value(values[0]), minimum, maximum), value(values[0])));
            }
        }

        labels.Sort((a, b) => a.Ideal.CompareTo(b.Ideal));
        var height = black.LineHeight;
        var placed = PlaceLabels(labels.Select(l => l.Ideal).ToList(), height);
        for (var i = 0; i < labels.Count; i++)
        {
            var (line, _, value) = labels[i];
            var name = (host.Texts?.String(7, 4 + line) ?? lines[line].Name) + ":";
            black.Draw(batch, name, new Vector2(origin.X + RightX + 3, origin.Y + placed[i]));
            black.Draw(batch, value.ToString(CultureInfo.InvariantCulture), new Vector2(origin.X + RightX + 60, origin.Y + placed[i]));
        }

        // The months (or years) under the graph, with their little marks.
        var statistics = hospital.Statistics;
        var step = _scale switch { 1 => 48, 2 => 12, _ => 1 };
        var x = RightX - (Spacing / 2);
        var month = statistics.Count - ((statistics.Count - 1) / 12 * 12);
        var year = (statistics.Count - 1) / step * (step / 12);
        for (var i = 0; i < values.Count; i++)
        {
            var label = step >= 12
                ? year.ToString(CultureInfo.InvariantCulture)
                : host.Texts?.String(6, month) ?? CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month);
            black.Draw(batch, label, new Vector2(origin.X + x + ((25 - black.Measure(label).Width) / 2), origin.Y + BottomY + 10));
            batch.Draw(graphics.Pixel, new Rectangle(origin.X + RightX - (i * Spacing), origin.Y + BottomY + 4, 1, 4), Color.Black);
            x -= Spacing;
            year -= step / 12;
            month -= step;
            if (month < 1)
            {
                month += 12;
            }
        }
    }

    public void Mouse(IWindowHost host, UiWindow window, Point local, MouseState mouse, MouseState previous)
    {
        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = Close.Contains(local) ? "close" : Scale.Contains(local) ? "scale" : PenAt(local) is { } pen ? "pen" + pen : null;
            return;
        }

        if (mouse.LeftButton != ButtonState.Released || previous.LeftButton != ButtonState.Pressed)
        {
            return;
        }

        var pressed = _pressed;
        _pressed = null;
        if (pressed == "close" && Close.Contains(local))
        {
            host.PlaySound("no4.wav");
            host.Close(window);
        }
        else if (pressed == "scale" && Scale.Contains(local))
        {
            // toggleGraphScale: months, then four years, then years.
            _scale = _scale == 1 ? 3 : _scale - 1;
            host.PlaySound(WindowLayer.DefaultButtonSound);
        }
        else if (pressed is { } name && name.StartsWith("pen", StringComparison.Ordinal) && PenAt(local) is { } pen && name == "pen" + pen)
        {
            Shown[pen] = !Shown[pen];
            host.PlaySound(WindowLayer.DefaultButtonSound);
        }
    }

    public static string? Tooltip(IWindowHost host, Point local)
    {
        string Tip(int index, string fallback) => OriginalTooltips.Get(host, index, fallback);
        return Close.Contains(local) ? Tip(176, "Close the graphs")
            : Scale.Contains(local) ? Tip(177, "Change the graph's time scale")
            : PenAt(local) is { } pen ? pen == 7 ? "Toggle reputation" : Tip(178 + pen, "Toggle " + GraphWindow.Lines[pen].Name.ToLowerInvariant())
            : null;
    }

    private static int? PenAt(Point local)
    {
        for (var i = 0; i < PenY.Length; i++)
        {
            if (new Rectangle(590, PenY[i], 42, 42).Contains(local))
            {
                return i;
            }
        }

        return null;
    }

    private static void DrawLine(SpriteBatch batch, Texture2D pixel, Vector2 from, Vector2 to, Color colour)
    {
        var delta = to - from;
        batch.Draw(pixel, from, null, colour, MathF.Atan2(delta.Y, delta.X), new Vector2(0, 0.5f), new Vector2(delta.Length(), 2), SpriteEffects.None, 0);
    }
}
