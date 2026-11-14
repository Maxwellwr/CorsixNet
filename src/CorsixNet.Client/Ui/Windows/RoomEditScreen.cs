// The room edit panel as the original draws it, ported from CorsixTH CorsixTH/Lua/dialogs/place_objects.lua
// (UIPlaceObjects: its panels, buttons, list, resize, draw, hovering) under dialogs/edit_room.lua (purchaseItems,
// pickupItems, confirm, cancel).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Client.Maps;
using CorsixNet.Client.Rendering;
using CorsixNet.Sim.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The room edit panel of Req05V's panels while furnishing, 186 wide and 29 taller for each line: the room's name, what
/// to do, cancel, buy, pick up and confirm (each greyed out while it cannot be used), and the objects still to place
/// with how many, the one on the cursor framed in red; a click on one puts it on the cursor, again turns it.
/// </summary>
internal sealed class RoomEditScreen(BuildRoomTool tool, Action<BuildButton> press)
{
    public const int Width = 186;

    private static readonly Rectangle Cancel = new(9, 108, 41, 42);
    private static readonly Rectangle Buy = new(51, 108, 41, 42);
    private static readonly Rectangle PickUp = new(93, 108, 41, 42);
    private static readonly Rectangle Confirm = new(135, 108, 43, 42);

    private string? _pressed;
    private int _hovered = -1;

    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Sheet("REQ05V", OriginalGraphics.MainPalette) is not null
        && graphics.Font("FONT01V", OriginalGraphics.MainPalette) is not null && graphics.Font("FONT02V", OriginalGraphics.MainPalette) is not null;

    /// <summary>The window's height for a number of lines (resize: 167 + 29 a line).</summary>
    public static int Height(int lines) => 167 + (lines * 29);

    /// <summary>The objects to place: those the room still needs, then the extras bought and not placed yet.</summary>
    public List<(ObjectDefinition Object, int Count)> Lines(IWindowHost host)
    {
        var simulation = host.Simulation;
        var lines = tool.Missing(simulation)
            .Select(m => (Object: simulation.Content.FindObject(m.ObjectType), m.Count))
            .Where(m => m.Object is not null)
            .Select(m => (m.Object!, m.Count))
            .ToList();
        foreach (var extra in tool.RoomType?.AdditionalObjects ?? [])
        {
            if (tool.ExtrasToPlace(extra) is > 0 and var count && lines.All(l => l.Item1.Id != extra) && simulation.Content.FindObject(extra) is { } definition)
            {
                lines.Add((definition, count));
            }
        }

        return lines;
    }

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        var graphics = host.Graphics!;
        var sheet = graphics.Sheet("REQ05V", OriginalGraphics.MainPalette)!;
        var white = graphics.Font("FONT01V", OriginalGraphics.MainPalette)!;
        var blue = graphics.Font("FONT02V", OriginalGraphics.MainPalette)!;
        var simulation = host.Simulation;
        var local = new Point(mouse.X, mouse.Y) - origin;
        var held = mouse.LeftButton == ButtonState.Pressed;
        void Panel(int sprite, int x, int y) => sheet.Draw(batch, sprite, origin.X + x, origin.Y + y, Color.White);

        Panel(112, 0, 0);
        for (var y = 48; y <= 83; y += 7)
        {
            Panel(113, 0, y);
        }

        Panel(114, 0, 90);
        var lines = Lines(host);
        var canConfirm = tool.CanConfirm(simulation);
        Panel(held && _pressed == "cancel" && Cancel.Contains(local) ? 116 : 115, 0, 100);
        Panel(held && _pressed == "buy" && Buy.Contains(local) ? 118 : 117, 50, 100);
        Panel(tool.IsPickingUp || (held && _pressed == "pickup" && PickUp.Contains(local)) ? 120 : 119, 92, 100);
        Panel(!canConfirm ? 129 : (held && _pressed == "confirm" && Confirm.Contains(local)) || Tutorial.Lit("confirm") ? 122 : 121, 134, 100);
        if (lines.Count > 0)
        {
            Panel(123, 0, 146);
            for (var i = 1; i < lines.Count; i++)
            {
                Panel(held && _pressed == "line" + (i - 1) && LineArea(i, lines.Count).Contains(local) ? 125 : 124, 0, 121 + (i * 29));
            }

            Panel(held && _pressed == "line" + (lines.Count - 1) && LineArea(lines.Count, lines.Count).Contains(local) ? 125 : 156, 0, 117 + (lines.Count * 29));
        }

        void Centred(TextRenderer font, string text, int x, int y, int width) => font.Draw(batch, text, new Vector2(origin.X + x + ((width - font.Measure(text).Width) / 2), origin.Y + y));
        Centred(white, tool.RoomType?.Name ?? string.Empty, 17, 21, 153);
        var what = tool.IsPickingUp ? host.Texts?.String(3, 16) ?? "Pick up an object to move it"
            : lines.Count > 0 ? host.Texts?.String(3, 14) ?? "Place the objects"
            : host.Texts?.String(3, 15) ?? "Confirm, or buy more objects";
        var row = 0;
        foreach (var text in MapViewScreen.WrapToWidth(what, 147, t => white.Measure(t).Width))
        {
            white.Draw(batch, text, new Vector2(origin.X + 20, origin.Y + 46 + (row++ * white.LineHeight)));
        }

        for (var i = 1; i <= lines.Count; i++)
        {
            var (definition, count) = lines[i - 1];
            if (definition.Id == tool.PlacingId)
            {
                // The line of the object on the cursor, framed in red.
                var frame = new Rectangle(origin.X + 20, origin.Y + 134 + (i * 29), 119, 12);
                var red = new Color(221, 83, 0);
                batch.Draw(graphics.Pixel, new Rectangle(frame.X, frame.Y, frame.Width, 1), red);
                batch.Draw(graphics.Pixel, new Rectangle(frame.X, frame.Bottom, frame.Width + 1, 1), red);
                batch.Draw(graphics.Pixel, new Rectangle(frame.X, frame.Y, 1, frame.Height), red);
                batch.Draw(graphics.Pixel, new Rectangle(frame.Right, frame.Y, 1, frame.Height), red);
            }

            var font = i - 1 == _hovered ? blue : white;
            Centred(font, definition.Name, 15, 136 + (i * 29), 130);
            Centred(font, count.ToString(CultureInfo.InvariantCulture), 151, 136 + (i * 29), 19);
        }
    }

    public void Mouse(IWindowHost host, UiWindow window, Point local, MouseState mouse, MouseState previous)
    {
        var lines = Lines(host);
        var hovered = LineAt(local, lines.Count);
        if (hovered != _hovered && hovered >= 0)
        {
            host.PlaySound("Hlight5.wav");
        }

        _hovered = hovered;
        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = Cancel.Contains(local) ? "cancel" : Buy.Contains(local) ? "buy" : PickUp.Contains(local) ? "pickup"
                : Confirm.Contains(local) ? "confirm" : hovered >= 0 ? "line" + hovered : null;
            return;
        }

        if (mouse.LeftButton != ButtonState.Released || previous.LeftButton != ButtonState.Pressed)
        {
            return;
        }

        var pressed = _pressed;
        _pressed = null;
        switch (pressed)
        {
            case "cancel" when Cancel.Contains(local):
                host.PlaySound("no4.wav");
                press(BuildButton.Back);
                break;
            case "buy" when Buy.Contains(local):
                host.PlaySound(WindowLayer.DefaultButtonSound);
                host.Open(new RoomBuyWindow(tool));
                break;
            case "pickup" when PickUp.Contains(local):
                host.PlaySound(WindowLayer.DefaultButtonSound);
                press(BuildButton.PickUp);
                break;
            case "confirm" when Confirm.Contains(local):
                if (tool.CanConfirm(host.Simulation))
                {
                    host.PlaySound("YesX.wav");
                    press(BuildButton.Confirm);
                }

                break;
            case { } line when line.StartsWith("line", StringComparison.Ordinal) && hovered >= 0 && line == "line" + hovered:
                host.PlaySound(WindowLayer.DefaultButtonSound);
                tool.Choose(lines[hovered].Object);
                break;
        }
    }

    public static string? Tooltip(IWindowHost host, Point local)
    {
        string Tip(int index, string fallback) => OriginalTooltips.Get(host, index, fallback);
        return Cancel.Contains(local) ? Tip(132, "Take the room down")
            : Buy.Contains(local) ? Tip(133, "Buy more objects for the room")
            : PickUp.Contains(local) ? Tip(134, "Pick up an object to move it")
            : Confirm.Contains(local) ? Tip(135, "Confirm the room")
            : null;
    }

    /// <summary>A line's button: the last line's sits 4 pixels lower in its taller panel.</summary>
    private static Rectangle LineArea(int line, int lines) =>
        line < lines ? new Rectangle(15, 121 + (line * 29) + 8, 130, 23) : new Rectangle(15, 117 + (line * 29) + 12, 130, 23);

    private static int LineAt(Point local, int lines)
    {
        for (var i = 1; i <= lines; i++)
        {
            if (LineArea(i, lines).Contains(local))
            {
                return i - 1;
            }
        }

        return -1;
    }
}

/// <summary>
/// The room's buy window (UIEditRoom:purchaseItems opens UIFurnishCorridor with the room's extras): how many of each extra
/// to buy; confirm puts them on the cursor one after another, each paid for as it is placed.
/// </summary>
internal sealed class RoomBuyWindow(BuildRoomTool tool) : UiWindow
{
    private readonly FurnishCorridorScreen _screen = new(host =>
    {
        var simulation = host.Simulation;
        var research = simulation.Hospitals[host.Player].Research;
        return (tool.RoomType?.AdditionalObjects ?? []).Select(simulation.Content.FindObject).OfType<ObjectDefinition>()
            .Where(o => research.CanBuy(simulation.Rules, o.Id)).ToList();
    });

    public override string Title => "Buy objects";

    public override bool IsDone(IWindowHost host) => !tool.IsFurnishing;

    public override bool IsFullscreen(IWindowHost host) => FurnishCorridorScreen.IsAvailable(host);

    public override Point FullscreenSize => FurnishCorridorScreen.Size;

    public override IEnumerable<WindowRow> Rows(IWindowHost host) => [];

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse) => _screen.Draw(batch, host, origin, mouse);

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous) =>
        _screen.Mouse(host, this, local, mouse, previous, list =>
        {
            foreach (var definition in list)
            {
                tool.PlaceExtra(definition);
            }
        });

    public override string? FullscreenTooltip(IWindowHost host, Point local) => _screen.Tooltip(host, local);
}
