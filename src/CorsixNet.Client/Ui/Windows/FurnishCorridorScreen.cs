// The furnish corridor window as the original draws it, ported from CorsixTH CorsixTH/Lua/dialogs/furnish_corridor.lua
// (UIFurnishCorridor: its panels, list, buttons, tooltips, purchaseItem, confirm, draw, the preview animation).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Client.Input;
using CorsixNet.Client.Rendering;
using CorsixNet.Sim.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The furnish corridor window of Req10V's panels, 360 by 274: the corridor's objects in CorsixTH's order with how many
/// to buy of each (a click on the name or the plus adds one, a right click or the minus takes one away; 5 with Shift, 10
/// with Ctrl, up to 99), the hovered object's price and preview, the total, cancel and confirm. Confirm puts the
/// objects on the cursor one after the other.
/// </summary>
internal sealed class FurnishCorridorScreen(Func<IWindowHost, List<ObjectDefinition>>? source = null)
{
    public static readonly Point Size = new(360, 274);

    /// <summary>The corridor's objects in CorsixTH's order (corridor_object).</summary>
    public static readonly string[] Order = ["reception_desk", "bench", "drinks_machine", "extinguisher", "radiator", "bin", "plant"];

    /// <summary>The objects' preview animations (build_preview_animation).</summary>
    public static readonly IReadOnlyDictionary<string, int> Previews = new Dictionary<string, int>
    {
        ["reception_desk"] = 5060,
        ["bench"] = 902,
        ["drinks_machine"] = 906,
        ["extinguisher"] = 912,
        ["radiator"] = 914,
        ["bin"] = 5096,
        ["plant"] = 934,
    };

    private static readonly Rectangle Cancel = new(9, 237, 129, 28);
    private static readonly Rectangle Confirm = new(154, 238, 197, 28);

    private readonly Dictionary<string, int> _quantities = [];
    private int _hovered = -1;
    private string? _pressed;
    private long _nextRepeat;

    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Sheet("REQ10V", OriginalGraphics.MainPalette) is not null
        && graphics.Font("FONT01V", OriginalGraphics.MainPalette) is not null && graphics.Font("FONT02V", OriginalGraphics.MainPalette) is not null;

    /// <summary>The objects offered: the corridor's, or those <paramref name="source"/> gives (a room's extras).</summary>
    private List<ObjectDefinition> List(IWindowHost host) => source?.Invoke(host) ?? Objects(host);

    /// <summary>The corridor's objects the game has, in order.</summary>
    public static List<ObjectDefinition> Objects(IWindowHost host) =>
        Order.Select(host.Simulation.Content.FindObject).OfType<ObjectDefinition>().Where(o => o.CorridorObject).ToList();

    /// <summary>The total of what is chosen, at the hospital's prices.</summary>
    public long Total(IWindowHost host) =>
        List(host).Sum(o => (long)_quantities.GetValueOrDefault(o.Id) * Price(host, o));

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        var graphics = host.Graphics!;
        var sheet = graphics.Sheet("REQ10V", OriginalGraphics.MainPalette)!;
        var white = graphics.Font("FONT01V", OriginalGraphics.MainPalette)!;
        var blue = graphics.Font("FONT02V", OriginalGraphics.MainPalette)!;
        var local = new Point(mouse.X, mouse.Y) - origin;
        var held = mouse.LeftButton == ButtonState.Pressed;
        void Panel(int sprite, int x, int y) => sheet.Draw(batch, sprite, origin.X + x, origin.Y + y, Color.White);
        bool Down(string key, Rectangle area) => held && _pressed == key && area.Contains(local);

        Panel(228, 0, 0);
        for (var y = 33; y <= 103; y += 10)
        {
            Panel(229, 0, y);
        }

        Panel(230, 0, 113);
        Panel(231, 0, 148);
        Panel(232, 0, 173);
        Panel(233, 0, 215);
        Panel(234, 0, 248);
        Panel(234, 0, 252);
        Panel(Down("cancel", Cancel) ? 243 : 242, Cancel.X, Cancel.Y);
        Panel(235, 146, 0);
        Panel(236, 146, 223);
        // The tutorial lights the reception desk, then confirm once one is chosen.
        var deskChosen = _quantities.GetValueOrDefault("reception_desk") > 0;
        Panel(Down("confirm", Confirm) || (deskChosen && Tutorial.Lit("buy:reception_desk")) ? 238 : 237, Confirm.X, Confirm.Y);
        var objects = List(host);
        for (var i = 0; i < 10; i++)
        {
            var y = 34 + (i * 19);
            Panel(239, 146, y);
            if (i < objects.Count)
            {
                Panel(Down("item" + i, Item(i)) || (!deskChosen && Tutorial.Lit("buy:" + objects[i].Id)) ? 241 : 240, 158, y);
                Panel(Down("less" + i, Less(i)) ? 245 : 244, 285, y + 1);
                Panel(Down("more" + i, More(i)) ? 247 : 246, 329, y + 1);
            }
        }

        void Text(TextRenderer font, string text, int x, int y) => font.Draw(batch, text, new Vector2(origin.X + x, origin.Y + y));
        Text(white, host.Texts?.String(16, 4) ?? "Choose items", 163, 18);
        var price = _hovered >= 0 && _hovered < objects.Count ? Price(host, objects[_hovered]) : 0;
        Text(white, string.Create(CultureInfo.InvariantCulture, $"{(host.Texts?.String(16, 6) ?? "Price:").TrimEnd()} {price}"), 24, 173);
        Text(white, string.Create(CultureInfo.InvariantCulture, $"{(host.Texts?.String(16, 7) ?? "Total:").TrimEnd()} {Total(host)}"), 24, 202);
        for (var i = 0; i < objects.Count; i++)
        {
            var font = i == _hovered ? blue : white;
            Text(font, objects[i].Name, 163, 20 + ((i + 1) * 19));
            var quantity = _quantities.GetValueOrDefault(objects[i].Id).ToString(CultureInfo.InvariantCulture);
            Text(font, quantity, 306 + ((19 - font.Measure(quantity).Width) / 2), 20 + ((i + 1) * 19));
        }

        if (_hovered >= 0 && _hovered < objects.Count && Previews.TryGetValue(objects[_hovered].Id, out var preview))
        {
            host.DrawAnimation(batch, preview, origin + new Point(72, 57));
        }
    }

    public void Mouse(IWindowHost host, UiWindow window, Point local, MouseState mouse, MouseState previous, Action<IReadOnlyList<ObjectDefinition>> buy)
    {
        var objects = List(host);
        var now = Environment.TickCount64;
        var hovered = local.X >= 158 && local.X < 346 && local.Y >= 34 && local.Y < 224 ? ((local.Y - 15) / 19) - 1 : -1;
        if (hovered != _hovered && hovered >= 0 && hovered < objects.Count)
        {
            host.PlaySound("HLight5.wav");
        }

        _hovered = hovered;
        var under = ButtonAt(local, objects.Count);
        if (mouse.RightButton == ButtonState.Pressed && previous.RightButton == ButtonState.Released && under is { } right && right.StartsWith("item", StringComparison.Ordinal))
        {
            Change(host, objects[right[^1] - '0'], -1);
            return;
        }

        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = under;
            if (under is { } key && (key.StartsWith("less", StringComparison.Ordinal) || key.StartsWith("more", StringComparison.Ordinal)))
            {
                // makeRepeatButton: at once, then again while held.
                Change(host, objects[key[^1] - '0'], key.StartsWith("more", StringComparison.Ordinal) ? 1 : -1);
                _nextRepeat = now + 300;
            }

            return;
        }

        if (mouse.LeftButton == ButtonState.Pressed && _pressed is { } holding && holding == under && now >= _nextRepeat
            && (holding.StartsWith("less", StringComparison.Ordinal) || holding.StartsWith("more", StringComparison.Ordinal)))
        {
            Change(host, objects[holding[^1] - '0'], holding.StartsWith("more", StringComparison.Ordinal) ? 1 : -1);
            _nextRepeat = now + 80;
            return;
        }

        if (mouse.LeftButton != ButtonState.Released || previous.LeftButton != ButtonState.Pressed)
        {
            return;
        }

        var pressed = _pressed;
        _pressed = null;
        if (pressed is null || pressed != under)
        {
            return;
        }

        if (pressed == "cancel")
        {
            host.PlaySound("no4.wav");
            host.Close(window);
        }
        else if (pressed == "confirm")
        {
            // confirm: the objects chosen go on the cursor one after the other, each bought as it is placed.
            host.PlaySound(WindowLayer.DefaultButtonSound);
            var list = objects.SelectMany(o => Enumerable.Repeat(o, _quantities.GetValueOrDefault(o.Id))).ToList();
            host.Close(window);
            if (list.Count > 0)
            {
                buy(list);
            }
        }
        else if (pressed.StartsWith("item", StringComparison.Ordinal))
        {
            Change(host, objects[pressed[^1] - '0'], 1);
        }
    }

    public string? Tooltip(IWindowHost host, Point local)
    {
        string Tip(int index, string fallback) => OriginalTooltips.Get(host, index, fallback);
        var objects = List(host);
        var under = ButtonAt(local, objects.Count);
        return under switch
        {
            "cancel" => Tip(41, "Cancel"),
            "confirm" => Tip(40, "Buy these objects"),
            { } key when key.StartsWith("less", StringComparison.Ordinal) => Tip(42, "Buy one less"),
            { } key when key.StartsWith("more", StringComparison.Ordinal) => Tip(43, "Buy one more"),
            { } key when key.StartsWith("item", StringComparison.Ordinal) => objects[key[^1] - '0'].Name,
            _ => new Rectangle(20, 168, 107, 19).Contains(local) ? Tip(38, "The price of the object")
                : new Rectangle(20, 196, 107, 19).Contains(local) ? Tip(39, "The total of what you are buying")
                : null,
        };
    }

    /// <summary>purchaseItem: one more or less, 5 with Shift, 10 with Ctrl, from 0 to 99, within the balance.</summary>
    private void Change(IWindowHost host, ObjectDefinition item, int by)
    {
        var keyboard = InputScript.Keyboard();
        var step = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl) ? 10
            : keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift) ? 5 : 1;
        var current = _quantities.GetValueOrDefault(item.Id);
        var wanted = Math.Clamp(current + (by * step), 0, 99);
        var bought = wanted - current;
        var balance = host.Simulation.Hospitals[host.Player].Balance;
        if (by > 0 && (current >= 99 || Total(host) + (bought * Price(host, item)) > balance))
        {
            // The adviser's cannot_buy or cannot_afford_2, and Wrong2.wav.
            host.Adviser.Say(host.Texts?.Named(current >= 99 ? "adviser.warnings.cannot_buy" : "adviser.warnings.cannot_afford_2")?.Replace("%s", item.Name, StringComparison.Ordinal)
                ?? "You don't have enough money in the bank to make that purchase!", Environment.TickCount64);
            host.PlaySound("wrong2.wav");
            return;
        }

        _quantities[item.Id] = wanted;
        host.PlaySound(by > 0 ? "AddItemJ.wav" : "DelItemJ.wav");
    }

    private static int Price(IWindowHost host, ObjectDefinition item) =>
        host.Simulation.Hospitals[host.Player].Research.ObjectCost(host.Simulation.Rules, item.Id);

    private static Rectangle Item(int i) => new(158, 34 + (i * 19), 125, 19);

    private static Rectangle Less(int i) => new(285, 35 + (i * 19), 17, 17);

    private static Rectangle More(int i) => new(329, 35 + (i * 19), 17, 17);

    private static string? ButtonAt(Point local, int count)
    {
        if (Cancel.Contains(local))
        {
            return "cancel";
        }

        if (Confirm.Contains(local))
        {
            return "confirm";
        }

        for (var i = 0; i < Math.Min(10, count); i++)
        {
            if (Less(i).Contains(local))
            {
                return "less" + i;
            }

            if (More(i).Contains(local))
            {
                return "more" + i;
            }

            if (Item(i).Contains(local))
            {
                return "item" + i;
            }
        }

        return null;
    }
}
