// The graphs, ported from CorsixTH CorsixTH/Lua/dialogs/fullscreen/graphs.lua (UIGraphs: the lines' colours, the
// three scales, normalising each line between its minimum and maximum with 0 always in range).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Client.Maps;
using CorsixNet.Client.Rendering;
using CorsixNet.Sim.Economy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The graphs (F8): money in and out, wages, balance, visitors, cures, deaths and reputation over the last twelve
/// months, years or four-year steps, each line scaled to its own range as CorsixTH's; the buttons switch lines on and
/// off and change the scale.
/// </summary>
internal sealed class GraphWindow : UiWindow
{
    /// <summary>Points on the graph (CorsixTH's <c>VERT_COUNT</c>).</summary>
    public const int Points = 12;

    private const int Spacing = 25;
    private const int GraphHeight = 268;

    private static readonly (string Name, Color Colour, Func<MonthStatistics, long> Value)[] Lines =
    [
        ("Money in", new Color(182, 32, 16), s => s.MoneyIn),
        ("Money out", new Color(215, 81, 8), s => s.MoneyOut),
        ("Wages", new Color(194, 162, 0), s => s.Wages),
        ("Balance", new Color(28, 138, 36), s => s.Balance),
        ("Visitors", new Color(0, 101, 198), s => s.Visitors),
        ("Cures", new Color(36, 40, 154), s => s.Cures),
        ("Deaths", new Color(130, 0, 178), s => s.Deaths),
        ("Reputation", new Color(215, 12, 101), s => s.Reputation),
    ];

    private readonly bool[] _shown = Enumerable.Repeat(true, Lines.Length).ToArray();
    private int _scale = 3;
    private IReadOnlyList<MonthStatistics> _values = [];

    public override string Title => "Graphs";

    public override string? Hotkey => "F8";

    public override Point? CanvasSize => new((Points - 1) * Spacing + 120, GraphHeight + 20);

    /// <summary>
    /// <c>UIGraphs:getHospitalStatistics</c>: from the newest month back, every month, every year or every four
    /// years, at most twelve.
    /// </summary>
    public static IReadOnlyList<MonthStatistics> Values(IReadOnlyList<MonthStatistics> statistics, int scale)
    {
        var step = scale switch { 1 => 4 * 12, 2 => 12, _ => 1 };
        var values = new List<MonthStatistics>();
        for (var i = statistics.Count - 1; values.Count < Points && i >= 0; i -= step)
        {
            values.Add(statistics[i]);
        }

        return values;
    }

    /// <summary><c>computeVerticalValuePosition</c>: the height of a value between the line's minimum and maximum, 0 to 1.</summary>
    public static double Height(long value, long minimum, long maximum) => maximum == minimum ? 0 : (double)(value - minimum) / (maximum - minimum);

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        _values = Values(host.Simulation.Hospitals[host.Player].Statistics, _scale);
        yield return new WindowRow(Lines.Take(4).Select((line, i) => Toggle(host, i)).ToList());
        yield return new WindowRow(Lines.Skip(4).Select((line, i) => Toggle(host, i + 4)).ToList());
        yield return WindowRow.Of(
            WindowCell.Button("Months", () => _scale = 3, _scale != 3, _scale == 3).WithTooltip(OriginalTooltips.Get(host, 177, "Change the graph's time scale")),
            WindowCell.Button("Years", () => _scale = 2, _scale != 2, _scale == 2).WithTooltip(OriginalTooltips.Get(host, 177, "Change the graph's time scale")),
            WindowCell.Button("4 years", () => _scale = 1, _scale != 1, _scale == 1).WithTooltip(OriginalTooltips.Get(host, 177, "Change the graph's time scale")),
            WindowCell.Button("Figures", () => host.Open(new GraphFiguresWindow())).WithTooltip("Show the figures behind the graphs"));
        if (_values.Count < 2)
        {
            yield return WindowCell.Line("The graphs start after the first month.", dim: true);
        }
    }

    public override void DrawCanvas(SpriteBatch batch, Rectangle area, Texture2D pixel, TextRenderer text)
    {
        var right = area.X + ((Points - 1) * Spacing);
        var bottom = area.Y + GraphHeight;
        for (var i = 0; i < Points; i++)
        {
            batch.Draw(pixel, new Rectangle(right - (i * Spacing), area.Y, 1, GraphHeight), new Color(60, 60, 60));
        }

        if (_values.Count < 2)
        {
            return;
        }

        for (var line = 0; line < Lines.Length; line++)
        {
            if (!_shown[line])
            {
                continue;
            }

            var (name, colour, value) = Lines[line];
            var minimum = Math.Min(0, _values.Min(value));
            var maximum = Math.Max(0, _values.Max(value));
            Vector2? previous = null;
            for (var i = 0; i < _values.Count; i++)
            {
                var point = new Vector2(right - (i * Spacing), bottom - (float)(Height(value(_values[i]), minimum, maximum) * GraphHeight));
                if (previous is { } from)
                {
                    DrawLine(batch, pixel, from, point, colour);
                }

                previous = point;
            }

            // The newest value by its line's end, as the original labels each line.
            var newest = value(_values[0]);
            var label = line is 0 or 1 or 2 or 3 ? MoneyText.Format(newest) : newest.ToString(CultureInfo.InvariantCulture);
            var labelY = bottom - (float)(Height(newest, minimum, maximum) * GraphHeight) - 6;
            text.Draw(batch, $"{label} {name}", new Vector2(right + 6, labelY), tint: colour);
        }
    }

    /// <summary>A line's button; its tooltip is <c>tooltip.graphs</c>' (CorsixTH's English corrects the reputation's).</summary>
    private WindowCell Toggle(IWindowHost host, int line) =>
        WindowCell.Button(Lines[line].Name, () => _shown[line] = !_shown[line], highlight: _shown[line])
            .WithTooltip(line == 7 ? "Toggle reputation" : OriginalTooltips.Get(host, 178 + line, "Toggle " + Lines[line].Name.ToLowerInvariant()));

    private static void DrawLine(SpriteBatch batch, Texture2D pixel, Vector2 from, Vector2 to, Color colour)
    {
        var delta = to - from;
        var angle = MathF.Atan2(delta.Y, delta.X);
        batch.Draw(pixel, from, null, colour, angle, new Vector2(0, 0.5f), new Vector2(delta.Length(), 2), SpriteEffects.None, 0);
    }
}
