// The queue window as the original draws it, ported from CorsixTH CorsixTH/Lua/dialogs/queue_dialog.lua (UIQueue: its
// panels, buttons, tooltips, draw, drawPatients, getHoveredPatient, dragging patients, the queue's size).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Client.Input;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Rooms;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// A queue's window of Req06V's panels, 604 by 122, centred: how many are queueing, expected and have gone in, the
/// queue's size with buttons (by 1, 5 with Shift, 10 with Ctrl), and the queueing patients standing in a row facing the
/// door; one dragged to the door goes to the front, to the exit sign to the back, between others to that place, and
/// onto another room of the same kind on the map, to that room. A right click opens the patient's window.
/// </summary>
internal sealed class QueueScreen(int ownerId)
{
    public static readonly Point Size = new(604, 122);

    private const int RowX = 239;
    private const int RowY = 75;
    private const int RowWidth = 276;
    private const int Gap = 10;

    private static readonly Rectangle Smaller = new(97, 89, 17, 17);
    private static readonly Rectangle Larger = new(144, 89, 17, 17);
    private static readonly Rectangle Close = new(562, 17, 24, 24);

    private string? _pressed;
    private int? _hovered;
    private (int Id, Point At)? _dragged;

    /// <summary>Whether a patient is being dragged, so the window takes the mouse wherever it goes.</summary>
    public bool IsDragging => _dragged is not null;

    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Sheet("REQ06V", OriginalGraphics.MainPalette) is not null
        && graphics.Font("FONT01V", OriginalGraphics.MainPalette) is not null;

    /// <summary>isInsideQueueBoundingBox.</summary>
    public static bool InQueueBox(Point local) => local.X >= 219 && local.X <= 534 && local.Y >= 15 && local.Y <= 105;

    /// <summary>The space between queueing patients (drawPatients), less when one is lifted out with a gap round them.</summary>
    public static int Spacing(int count, bool hovered) => count <= 1 ? 0 : (RowWidth - (hovered ? 2 * Gap : 0)) / (count - 1);

    /// <summary>Where a patient dropped at <paramref name="x"/> in the row goes (onMouseUp: floor((x - 220) / dx)).</summary>
    public static int DropPosition(int x, int count) => Math.Clamp((x - 220) / Math.Max(1, count <= 1 ? 1 : RowWidth / (count - 1)), 0, Math.Max(0, count - 1));

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        var graphics = host.Graphics!;
        var simulation = host.Simulation;
        if (QueueWindow.QueueOf(simulation, ownerId) is not { } queue)
        {
            return;
        }

        var sheet = graphics.Sheet("REQ06V", OriginalGraphics.MainPalette)!;
        var font = graphics.Font("FONT01V", OriginalGraphics.MainPalette)!;
        var local = new Point(mouse.X, mouse.Y) - origin;
        var held = mouse.LeftButton == ButtonState.Pressed;
        void Panel(int sprite, int x, int y) => sheet.Draw(batch, sprite, origin.X + x, origin.Y + y, Color.White);

        Panel(364, 0, 0);
        for (var x = 21; x <= 83; x += 4)
        {
            Panel(365, x, 0);
        }

        Panel(366, 85, 0);
        for (var x = 223; x <= 531; x += 7)
        {
            Panel(367, x, 0);
        }

        Panel(368, 529, 0);
        Panel(held && _pressed == "smaller" && Smaller.Contains(local) ? 370 : 369, Smaller.X, Smaller.Y);
        Panel(held && _pressed == "larger" && Larger.Contains(local) ? 372 : 371, Larger.X, Larger.Y);
        Panel(held && _pressed == "close" && Close.Contains(local) ? 374 : 373, Close.X, Close.Y);

        void At(string text, int x, int y) => font.Draw(batch, text, new Vector2(origin.X + x, origin.Y + y));
        At(host.Texts?.String(49, 1) ?? "Number in queue", 22, 22);
        At(queue.Count.ToString(CultureInfo.InvariantCulture), 140, 22);
        At(host.Texts?.String(49, 2) ?? "Number expected", 22, 45);
        At(Expected(simulation, queue).ToString(CultureInfo.InvariantCulture), 140, 45);
        At(host.Texts?.String(49, 3) ?? "Number entered", 22, 68);
        At(queue.VisitorCount.ToString(CultureInfo.InvariantCulture), 140, 68);
        At(host.Texts?.String(49, 4) ?? "Max queue size", 22, 93);
        At(queue.MaxSize.ToString(CultureInfo.InvariantCulture), 119, 93);

        // drawPatients: in a row from the door, the one under the cursor lifted with a gap on each side.
        var members = Members(simulation, queue);
        var dx = Spacing(members.Count, _hovered is not null);
        var x0 = RowX;
        foreach (var member in members)
        {
            if (member.Id == _hovered)
            {
                x0 += Gap;
                host.DrawIdle(batch, member, origin + new Point(x0, RowY - 10));
                x0 += Gap + dx;
            }
            else
            {
                host.DrawIdle(batch, member, origin + new Point(x0, RowY));
                x0 += dx;
            }
        }

        if (_dragged is { } dragged && simulation.FindEntity(dragged.Id) is Humanoid carried)
        {
            host.DrawIdle(batch, carried, new Point(mouse.X, mouse.Y));
        }
    }

    public void Mouse(IWindowHost host, UiWindow window, Point local, MouseState mouse, MouseState previous)
    {
        var simulation = host.Simulation;
        if (QueueWindow.QueueOf(simulation, ownerId) is not { } queue)
        {
            return;
        }

        var members = Members(simulation, queue);
        _hovered = _dragged is null && InQueueBox(local) ? HoveredPatient(members, local.X - 219) : null;
        if (mouse.RightButton == ButtonState.Pressed && previous.RightButton == ButtonState.Released && _hovered is { } chosen)
        {
            host.Open(new PatientWindow(chosen));
            return;
        }

        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = Smaller.Contains(local) ? "smaller" : Larger.Contains(local) ? "larger" : Close.Contains(local) ? "close" : null;
            if (_pressed is null && _hovered is { } lifted)
            {
                _dragged = (lifted, local);
            }

            return;
        }

        if (mouse.LeftButton != ButtonState.Released || previous.LeftButton != ButtonState.Pressed)
        {
            return;
        }

        if (_dragged is { } dragged)
        {
            _dragged = null;
            Drop(host, queue, members, dragged.Id, local, new Point(mouse.X, mouse.Y));
            return;
        }

        var pressed = _pressed;
        _pressed = null;
        var keyboard = InputScript.Keyboard();
        var amount = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl) ? 10
            : keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift) ? 5 : 1;
        switch (pressed)
        {
            case "smaller" when Smaller.Contains(local):
                host.PlaySound(WindowLayer.DefaultButtonSound);
                host.Submit(new ChangeQueueSizeCommand(ownerId, -amount));
                break;
            case "larger" when Larger.Contains(local):
                host.PlaySound(WindowLayer.DefaultButtonSound);
                host.Submit(new ChangeQueueSizeCommand(ownerId, amount));
                break;
            case "close" when Close.Contains(local):
                host.PlaySound("no4.wav");
                host.Close(window);
                break;
        }
    }

    public static string? Tooltip(IWindowHost host, Point local)
    {
        string Tip(int index, string fallback) => OriginalTooltips.Get(host, index, fallback);
        return Smaller.Contains(local) ? Tip(75, "Make the queue shorter")
            : Larger.Contains(local) ? Tip(76, "Make the queue longer")
            : Close.Contains(local) ? Tip(79, "Close this window")
            : new Rectangle(15, 15, 148, 21).Contains(local) ? Tip(71, "How many are in the queue")
            : new Rectangle(15, 39, 148, 21).Contains(local) ? Tip(72, "How many are on their way to the queue")
            : new Rectangle(15, 62, 148, 21).Contains(local) ? Tip(73, "How many have gone in")
            : new Rectangle(15, 87, 148, 21).Contains(local) ? Tip(74, "The most who may queue")
            : new Rectangle(168, 25, 45, 80).Contains(local) ? Tip(77, "Drag a patient here to move them to the front")
            : new Rectangle(543, 51, 43, 54).Contains(local) ? Tip(78, "Drag a patient here to move them to the back")
            : InQueueBox(local) ? Tip(80, "Drag a patient to move them, right click for their window")
            : null;
    }

    /// <summary>onMouseUp with a patient dragged: to the front, the back, a place in the row, or another room like this one.</summary>
    private void Drop(IWindowHost host, HumanoidQueue queue, List<Humanoid> members, int id, Point local, Point screen)
    {
        if (!queue.Contains(id))
        {
            return;
        }

        if (local.X > 170 && local.X < 210 && local.Y > 25 && local.Y < 105)
        {
            host.Submit(new MoveInQueueCommand(id, 0));
        }
        else if (local.X > 542 && local.X < 585 && local.Y > 50 && local.Y < 105)
        {
            host.Submit(new MoveInQueueCommand(id, queue.Count - 1));
        }
        else if (InQueueBox(local))
        {
            host.Submit(new MoveInQueueCommand(id, DropPosition(local.X, members.Count)));
        }
        else if (local.X < 0 || local.Y < 0 || local.X >= Size.X || local.Y >= Size.Y)
        {
            // Onto the map: another finished room of the same kind takes them.
            var simulation = host.Simulation;
            if (host.TileAtScreen(screen) is { } tile && simulation.FindEntity(simulation.Map[tile.X, tile.Y].RoomId) is Room target
                && simulation.FindEntity(ownerId) is Room source && target.Id != source.Id && target.RoomType == source.RoomType
                && target.IsActive && RoomBuilder.IsOwnedBy(simulation.Map, target, host.Player))
            {
                host.Submit(new SendPatientToRoomCommand(id, target.Id));
            }
        }
    }

    /// <summary>getHoveredPatient: the one nearest the cursor along the row, within 25 pixels.</summary>
    private int? HoveredPatient(List<Humanoid> members, int x)
    {
        x -= 15;
        var dx = Spacing(members.Count, false);
        var offset = 0;
        (int Id, int Diff)? closest = null;
        for (var i = 0; i < members.Count; i++)
        {
            var diff = Math.Abs((i * dx) + offset - x);
            if (members[i].Id == _hovered)
            {
                offset = Gap * 2;
                diff += Gap;
            }

            if (closest is null || diff < closest.Value.Diff)
            {
                closest = (members[i].Id, diff);
            }
        }

        return closest is { Diff: <= 25 } near ? near.Id : null;
    }

    /// <summary>queue:expectedSize: patients on their way to this room or desk who are not in its queue yet.</summary>
    private int Expected(Simulation simulation, HumanoidQueue queue) =>
        simulation.Entities.OfType<Humanoid>().Count(h => h.Behaviour is PatientBehaviour patient && patient.TargetId == ownerId && !queue.Contains(h.Id));

    private static List<Humanoid> Members(Simulation simulation, HumanoidQueue queue) =>
        queue.Members.Select(simulation.FindEntity).OfType<Humanoid>().ToList();
}
