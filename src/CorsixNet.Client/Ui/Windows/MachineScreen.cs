// The machine window as the original draws it, ported from CorsixTH CorsixTH/Lua/dialogs/machine_dialog.lua (UIMachine:
// its panels, buttons, tooltips, draw, callHandyman, replaceMachine, cycling through the machines).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Rooms;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// A machine's window of Req03V's panels, 188 by 206: its name (in brackets while a repair is called), how often it has
/// been used, the bar of the uses it has left, and buttons to call a handyman and to replace it. A right click on the
/// name goes on to the next machine and moves the view there.
/// </summary>
internal sealed class MachineScreen
{
    public static readonly Point Size = new(188, 206);

    private static readonly Rectangle Repair = new(20, 127, 63, 60);
    private static readonly Rectangle Replace = new(92, 127, 45, 60);
    private static readonly Rectangle Close = new(146, 18, 24, 24);
    private static readonly Rectangle NameBox = new(18, 19, 121, 23);

    private string? _pressed;

    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Sheet("REQ03V", OriginalGraphics.MainPalette) is not null
        && graphics.Font("FONT01V", OriginalGraphics.MainPalette) is not null;

    /// <summary>The status bar's width: what is left of its strength, 40 pixels for the whole.</summary>
    public static int StatusWidth(int timesUsed, int strength) =>
        strength <= 0 ? 0 : (int)Math.Floor((Math.Clamp(1 - ((double)timesUsed / strength), 0, 1) * 40) + 0.5);

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse, RoomObject machine)
    {
        var graphics = host.Graphics!;
        var sheet = graphics.Sheet("REQ03V", OriginalGraphics.MainPalette)!;
        var font = graphics.Font("FONT01V", OriginalGraphics.MainPalette)!;
        var state = machine.Machine!;
        var local = new Point(mouse.X, mouse.Y) - origin;
        var held = mouse.LeftButton == ButtonState.Pressed;
        void Panel(int sprite, int x, int y) => sheet.Draw(batch, sprite, origin.X + x, origin.Y + y, Color.White);
        void Button(string key, Rectangle area, int sprite) => Panel(held && _pressed == key && area.Contains(local) ? sprite + 1 : sprite, area.X, area.Y);

        Panel(333, 0, 0);
        Panel(334, 0, 74);
        for (var y = 131; y <= 180; y += 7)
        {
            Panel(335, 0, y);
        }

        Panel(336, 0, 182);
        Button("repair", Repair, 339);
        Button("replace", Replace, 341);
        Button("close", Close, 337);

        var name = host.Simulation.Content.FindObject(machine.ObjectType)?.Name ?? machine.ObjectType;
        font.Draw(batch, state.IsRepairCalled ? $"({name})" : name, new Vector2(origin.X + 27, origin.Y + 27));
        font.Draw(batch, state.TotalUsage.ToString(CultureInfo.InvariantCulture), new Vector2(origin.X + 60, origin.Y + 59));
        for (var dx = 0; dx < StatusWidth(state.TimesUsed, state.Strength); dx++)
        {
            Panel(352, 53 + dx, 99);
        }
    }

    public void Mouse(IWindowHost host, MachineWindow window, Point local, MouseState mouse, MouseState previous, RoomObject machine)
    {
        if (mouse.RightButton == ButtonState.Pressed && previous.RightButton == ButtonState.Released && NameBox.Contains(local))
        {
            // onMouseDown's right button on the name: the next machine in a working room, camclick.wav.
            var simulation = host.Simulation;
            var machines = simulation.Entities.OfType<RoomObject>()
                .Where(o => o.Machine is not null && !o.IsDestroyed && simulation.FindEntity(o.RoomId) is Room { IsActive: true } room && RoomBuilder.IsOwnedBy(simulation.Map, room, host.Player))
                .OrderBy(o => o.Id)
                .ToList();
            var next = machines.FirstOrDefault(o => o.Id > machine.Id) ?? machines.FirstOrDefault();
            if (next is not null && next.Id != machine.Id)
            {
                host.CenterOn(next.Tile);
                host.Close(window);
                host.Open(new MachineWindow(next.Id));
                host.PlaySound("camclick.wav");
            }

            return;
        }

        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = Repair.Contains(local) ? "repair" : Replace.Contains(local) ? "replace" : Close.Contains(local) ? "close" : null;
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
            case "repair" when Repair.Contains(local):
                // callHandyman: only once it has been used.
                host.PlaySound(WindowLayer.DefaultButtonSound);
                if (machine.Machine is { TimesUsed: not 0, IsRepairCalled: false })
                {
                    host.Submit(new CallHandymanCommand(machine.Id));
                }

                break;
            case "replace" when Replace.Contains(local):
                host.PlaySound(WindowLayer.DefaultButtonSound);
                window.AskToReplace(host, machine);
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
        return Repair.Contains(local) ? Tip(117, "Call a handyman to repair the machine")
            : Replace.Contains(local) ? Tip(118, "Replace the machine")
            : Close.Contains(local) ? Tip(114, "Close this window")
            : NameBox.Contains(local) ? Tip(113, "The machine's name; right click for the next machine")
            : new Rectangle(18, 49, 121, 28).Contains(local) ? Tip(115, "How many times the machine has been used")
            : new Rectangle(24, 88, 104, 27).Contains(local) ? Tip(116, "How worn the machine is")
            : null;
    }
}
