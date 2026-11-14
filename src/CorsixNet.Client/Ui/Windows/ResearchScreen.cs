// The research screen as the original draws it, ported from CorsixTH
// CorsixTH/Lua/dialogs/fullscreen/research_policy.lua (UIResearch: its buttons, tooltips, adjustResearch, onTick, draw).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Client.Input;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Research;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The fullscreen research policy over Res01V: for each of the five areas its share of the research points with arrows
/// to change it (1, 5 with Shift, 20 with Ctrl, 100 with both; held, they repeat), "done" for a finished area, a bar for
/// how far its current work has got, and the total; the water drips and the rat runs (Res02V).
/// </summary>
internal sealed class ResearchScreen
{
    private const string Palette = "RES01V";

    /// <summary>One of CorsixTH's ticks, which the water and the rat count (the game's 18 a second).</summary>
    private const long TickMilliseconds = 1000 / 18;
    private const long RepeatDelay = 300;
    private const long RepeatEvery = 80;

    private static readonly Rectangle Close = new(607, 447, 40, 40);

    private readonly long _opened = Environment.TickCount64;
    private (ResearchCategory Category, int Direction)? _pressed;
    private bool _closePressed;
    private long _nextRepeat;

    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Picture("RES01V") is not null && graphics.Sheet("RES02V", Palette) is not null
        && graphics.Font("FONT43V", Palette) is not null;

    /// <summary>The arrows of an area (1 to 5 from the top): less at x 372, more at 450, 40 pixels square.</summary>
    public static Rectangle Arrow(int row, int direction) => new(direction < 0 ? 372 : 450, 21 + (row * 41), 40, 40);

    /// <summary>The water's and the rat's sprites after <paramref name="ticks"/> (each moves on every fourth tick).</summary>
    public static (int Water, int Rat) Animation(long ticks) => (5 + (int)(ticks / 4 % 8), 13 + (int)(ticks / 4 % 8));

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        var graphics = host.Graphics!;
        var simulation = host.Simulation;
        var research = simulation.Hospitals[host.Player].Research;
        batch.Draw(graphics.Picture("RES01V")!, origin.ToVector2(), Color.White);
        var sheet = graphics.Sheet("RES02V", Palette)!;
        var font = graphics.Font("FONT43V", Palette)!;
        var (water, rat) = Animation((Environment.TickCount64 - _opened) / TickMilliseconds);
        sheet.Draw(batch, water, origin.X + 2, origin.Y + 312, Color.White);
        sheet.Draw(batch, rat, origin.X + 480, origin.Y + 365, Color.White);
        var local = new Point(mouse.X, mouse.Y) - origin;
        var held = mouse.LeftButton == ButtonState.Pressed;
        if (held && _closePressed && Close.Contains(local))
        {
            sheet.Draw(batch, 4, origin.X + Close.X, origin.Y + Close.Y, Color.White);
        }

        void Centred(string text, int x, int y, int width) =>
            font.Draw(batch, text, new Vector2(origin.X + x + ((width - font.Measure(text).Width) / 2), origin.Y + y));

        var categories = Enum.GetValues<ResearchCategory>();
        for (var i = 1; i <= categories.Length; i++)
        {
            var category = categories[i - 1];
            var area = research.Area(category);
            var y = 28 + (i * 41);
            font.Draw(batch, host.Texts?.String(17, i) ?? category.ToString(), new Vector2(origin.X + 170, origin.Y + y));
            if (area.Current.IsNone)
            {
                // updateCategories: a finished area's arrows are covered with a dark panel.
                batch.Draw(graphics.Pixel, new Rectangle(origin.X + 372, origin.Y + 21 + (i * 41), 120, 30), new Color(24, 24, 20));
                Centred(host.Texts?.String(3, 21) ?? "Done", 270, y, 300);
                continue;
            }

            Centred(area.Share.ToString(CultureInfo.InvariantCulture), 270, y, 300);
            foreach (var direction in new[] { -1, 1 })
            {
                if (held && _pressed == (category, direction) && Arrow(i, direction).Contains(local))
                {
                    var arrow = Arrow(i, direction);
                    sheet.Draw(batch, direction < 0 ? 1 : 2, origin.X + arrow.X, origin.Y + arrow.Y, Color.White);
                }
            }

            if (Progress(simulation, research, area.Current) is { } progress)
            {
                // The progress bar: sprite 3 every 10 pixels over 290 for the whole.
                var length = 290 * progress;
                for (var dx = 0; dx + 10 < length; dx += 10)
                {
                    sheet.Draw(batch, 3, origin.X + 172 + dx, origin.Y + y + 26, Color.White);
                }
            }
        }

        Centred(research.Total.ToString(CultureInfo.InvariantCulture), 270, 288, 300);
    }

    public void Mouse(IWindowHost host, UiWindow window, Point local, MouseState mouse, MouseState previous)
    {
        var now = Environment.TickCount64;
        var under = ArrowAt(host, local);
        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _closePressed = Close.Contains(local);
            _pressed = under;
            if (under is { } first)
            {
                Adjust(host, first.Category, first.Direction);
                _nextRepeat = now + RepeatDelay;
            }

            return;
        }

        if (mouse.LeftButton == ButtonState.Pressed && under is { } held && held == _pressed && now >= _nextRepeat)
        {
            Adjust(host, held.Category, held.Direction);
            _nextRepeat = now + RepeatEvery;
        }

        if (mouse.LeftButton == ButtonState.Released && previous.LeftButton == ButtonState.Pressed)
        {
            if (_closePressed && Close.Contains(local))
            {
                host.PlaySound("no4.wav");
                host.Close(window);
            }

            _closePressed = false;
            _pressed = null;
        }
    }

    public static string? Tooltip(IWindowHost host, Point local)
    {
        string Tip(int index, string fallback) => OriginalTooltips.Get(host, index, fallback);
        if (Close.Contains(local))
        {
            return Tip(163, "Close the research screen");
        }

        var categories = Enum.GetValues<ResearchCategory>();
        var simulation = host.Simulation;
        var research = simulation.Hospitals[host.Player].Research;
        for (var i = 1; i <= categories.Length; i++)
        {
            if (!research.Area(categories[i - 1]).Current.IsNone)
            {
                if (Arrow(i, -1).Contains(local))
                {
                    return Tip(163 + i, "Decrease the percentage");
                }

                if (Arrow(i, 1).Contains(local))
                {
                    return Tip(168 + i, "Increase the percentage");
                }
            }

            // The progress tooltips over each area (research_policy.research_progress), in our words.
            if (new Rectangle(165, 60 + ((i - 1) * 41), 315, 41).Contains(local))
            {
                var current = research.Area(categories[i - 1]).Current;
                return Progress(simulation, research, current) is { } progress
                    ? string.Create(CultureInfo.InvariantCulture, $"Progress towards the next discovery: {(int)(progress * 100)} %")
                    : "Nothing being researched here";
            }
        }

        return null;
    }

    /// <summary>adjustResearch: 1, 5 with Shift, 20 with Ctrl, 100 with both; Wrong2.wav when nothing can change.</summary>
    private static void Adjust(IWindowHost host, ResearchCategory category, int direction)
    {
        var keyboard = InputScript.Keyboard();
        var ctrl = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        var shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        var amount = ctrl && shift ? 100 : ctrl ? 20 : shift ? 5 : 1;
        var research = host.Simulation.Hospitals[host.Player].Research;
        var area = research.Area(category);
        var can = direction < 0 ? area.Share > 0 : research.Total < 100 && !area.Current.IsNone;
        host.PlaySound(can ? WindowLayer.DefaultButtonSound : "wrong2.wav");
        if (can)
        {
            host.Submit(new AdjustResearchCommand(category, direction * amount));
        }
    }

    private static (ResearchCategory Category, int Direction)? ArrowAt(IWindowHost host, Point local)
    {
        var categories = Enum.GetValues<ResearchCategory>();
        var research = host.Simulation.Hospitals[host.Player].Research;
        for (var i = 1; i <= categories.Length; i++)
        {
            foreach (var direction in new[] { -1, 1 })
            {
                if (Arrow(i, direction).Contains(local) && !research.Area(categories[i - 1]).Current.IsNone)
                {
                    return (categories[i - 1], direction);
                }
            }
        }

        return null;
    }

    /// <summary>How far the work towards a target has got, from 0 to 1, or null when there is no real target.</summary>
    private static float? Progress(Simulation simulation, ResearchDepartment research, ResearchTarget target)
    {
        decimal? points = target.Kind switch
        {
            ResearchTargetKind.Object => research.Objects.TryGetValue(target.Id, out var obj) ? obj.Points : null,
            ResearchTargetKind.Drug => research.Drugs.TryGetValue(target.Id, out var drug) ? drug : null,
            _ => null,
        };
        return points is { } done && research.Required(simulation, target) is { } required && required > 0
            ? (float)Math.Min(1m, done / required)
            : null;
    }
}
