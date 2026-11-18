using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenWards.Sim;
using OpenWards.Sim.Calendar;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Disasters;

namespace OpenWards.Client.Ui.Windows;

/// <summary>
/// The watch (CorsixTH's <c>dialogs/watch.lua</c>): the countdowns running, until the hospital opens (with the button
/// that opens it at once), until an emergency's patients are lost and until the health inspector comes in an epidemic.
/// It opens by itself when a countdown starts and closes when none runs.
/// </summary>
internal sealed class WatchWindow : UiWindow
{
    public override string Title => UiText.Get("openwards.watch.caption", "Watch");

    /// <summary>At the left, a little above the bottom, as CorsixTH's <c>UIWatch</c>.</summary>
    public override Vector2? DefaultPosition => new(20, -100);

    private readonly WatchScreen _screen = new();

    /// <summary>The original's look (<see cref="WatchScreen"/>) where its graphics are there; else the table below.</summary>
    public override bool IsFullscreen(IWindowHost host) => WatchScreen.IsAvailable(host);

    public override Point FullscreenSize => WatchScreen.Size;

    public override Vector2? FullscreenPosition => new(20, -100);

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse) => _screen.Draw(batch, host, origin, mouse);

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous) => _screen.Mouse(host, local, mouse, previous);

    public override string? FullscreenTooltip(IWindowHost host, Point local) => WatchScreen.Tooltip(host, local);
    /// <summary>Whether a countdown runs for the player; the opening watch waits while the tutorial holds it.</summary>
    public static bool IsRunning(Simulation simulation, int player)
    {
        var hospital = simulation.Hospitals[player];
        return hospital is { IsOpen: false, IsOpeningWatchHeld: false } || hospital.Emergencies.Current is { IsAccepted: true } || hospital.Epidemics.Current is { IsWatchRunning: true };
    }

    public override bool IsDone(IWindowHost host) => !IsRunning(host.Simulation, host.Player);

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var simulation = host.Simulation;
        var hospital = simulation.Hospitals[host.Player];
        if (hospital is { IsOpen: false, IsOpeningWatchHeld: false })
        {
            var days = (hospital.HoursUntilOpen + GameDate.HoursPerDay - 1) / GameDate.HoursPerDay;
            yield return WindowRow.Of(
                UiText.Format("openwards.watch.opening_in", "Opening in %1% days", days),
                Segments(hospital.OpenTimer + 1),
                WindowCell.Button(UiText.Get("openwards.watch.open_now", "Open now"), () => host.Submit(new OpenHospitalCommand())).WithTooltip(OriginalTooltips.Get(host, 238, "Open the hospital now")));
        }

        if (hospital.Emergencies.Current is { IsAccepted: true } emergency)
        {
            yield return WindowRow.Of(
                UiText.Format("openwards.watch.emergency", "Emergency: %1% cured, %2% lost of %3%", emergency.Cured, emergency.Killed, emergency.Victims),
                Segments(emergency.WatchTimer + 1));
        }

        if (hospital.Epidemics.Current is { IsWatchRunning: true } epidemic)
        {
            yield return WindowRow.Of(
                UiText.Format("openwards.watch.epidemic", "Epidemic: %1% infected, the inspector is coming", Epidemics.CountInfectedPatients(simulation, epidemic)),
                Segments(epidemic.WatchTimer + 1));
        }
    }

    /// <summary>The watch's face as the segments left of 13.</summary>
    private static string Segments(int left) => new string('#', Math.Clamp(left, 0, 13)) + new string('.', 13 - Math.Clamp(left, 0, 13));
}
