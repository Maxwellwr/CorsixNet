// The watch as the original draws it, ported from CorsixTH CorsixTH/Lua/dialogs/watch.lua (UIWatch: its panels, the
// segments as open_timer runs down, the end button, the hover sprites, the tooltips).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Disasters;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>Which countdown the watch shows (UIWatch's count_type).</summary>
internal enum WatchKind
{
    Opening,
    Emergency,
    Epidemic,
}

/// <summary>
/// The watch of Watch01V's sprites, 39 by 79, at (20, -100): the face fills red as its thirteen segments run out; above
/// it, for the opening the button that opens the hospital at once, for an epidemic the vaccination button (none for an
/// emergency).
/// </summary>
internal sealed class WatchScreen
{
    public static readonly Point Size = new(39, 79);

    private static readonly Rectangle EndButton = new(8, 0, 27, 28);

    private string? _pressed;

    public static bool IsAvailable(IWindowHost host) => host.Graphics?.Sheet("WATCH01V", OriginalGraphics.MainPalette, "DATA") is not null;

    /// <summary>The countdown shown, the opening first, and its open_timer (12 down to -1), or null if none runs.</summary>
    public static (WatchKind Kind, int Timer)? Countdown(Simulation simulation, int player)
    {
        var hospital = simulation.Hospitals[player];
        if (hospital is { IsOpen: false, IsOpeningWatchHeld: false })
        {
            return (WatchKind.Opening, hospital.OpenTimer);
        }

        if (hospital.Emergencies.Current is { IsAccepted: true } emergency)
        {
            return (WatchKind.Emergency, emergency.WatchTimer);
        }

        return hospital.Epidemics.Current is { IsWatchRunning: true } epidemic ? (WatchKind.Epidemic, epidemic.WatchTimer) : null;
    }

    /// <summary>
    /// onWorldTick's panels for a timer: the white face (1) until the timer reaches 5, then the red segment sprite (2 at
    /// 11, 13 - timer below), none at 0.
    /// </summary>
    public static (bool Face, int? Segment) Face(int timer) =>
        (timer > 5, timer switch
        {
            >= 12 => null,
            11 => 2,
            <= 0 => null,
            _ => 13 - timer,
        });

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        if (Countdown(host.Simulation, host.Player) is not { } countdown)
        {
            return;
        }

        var sheet = host.Graphics!.Sheet("WATCH01V", OriginalGraphics.MainPalette, "DATA")!;
        var local = new Point(mouse.X, mouse.Y) - origin;
        void Panel(int sprite, int x, int y) => sheet.Draw(batch, sprite, origin.X + x, origin.Y + y, Color.White);

        var end = countdown.Kind == WatchKind.Epidemic ? 14 : 16;
        var hover = EndButton.Contains(local);
        if (countdown.Kind != WatchKind.Emergency)
        {
            Panel(mouse.LeftButton == ButtonState.Pressed && _pressed == "end" && hover ? end + 1 : end, 4, 0);
        }

        Panel(13, 0, 28);
        if (hover && countdown.Kind == WatchKind.Opening)
        {
            Panel(17, 4, 0);
        }
        else if (hover && countdown.Kind == WatchKind.Epidemic)
        {
            Panel(15, 4, 0);
        }

        var (face, segment) = Face(countdown.Timer);
        if (face)
        {
            Panel(1, 2, 47);
        }

        if (segment is { } red)
        {
            Panel(red, 2, 47);
        }
    }

    public void Mouse(IWindowHost host, Point local, MouseState mouse, MouseState previous)
    {
        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = EndButton.Contains(local) ? "end" : null;
            return;
        }

        if (mouse.LeftButton != ButtonState.Released || previous.LeftButton != ButtonState.Pressed)
        {
            return;
        }

        var pressed = _pressed;
        _pressed = null;
        if (pressed == "end" && EndButton.Contains(local) && Countdown(host.Simulation, host.Player) is { Kind: WatchKind.Opening })
        {
            // onCountdownEnd for the opening: the hospital opens now, with its fanfare.
            host.PlaySound(WindowLayer.DefaultButtonSound);
            host.Submit(new OpenHospitalCommand());
        }
    }

    public static string? Tooltip(IWindowHost host, Point local)
    {
        if (Countdown(host.Simulation, host.Player) is not { } countdown || !new Rectangle(0, 0, Size.X, Size.Y).Contains(local))
        {
            return null;
        }

        string Tip(int index, string fallback) => OriginalTooltips.Get(host, index, fallback);
        return countdown.Kind switch
        {
            WatchKind.Opening => Tip(238, "Open the hospital now"),
            WatchKind.Emergency => Tip(239, "The time left for the emergency"),
            _ => Tip(240, "The time left to cover up the epidemic"),
        };
    }
}
