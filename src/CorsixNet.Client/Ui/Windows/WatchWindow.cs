using System.Globalization;
using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Disasters;
using Microsoft.Xna.Framework;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The watch (CorsixTH's <c>dialogs/watch.lua</c>): the countdowns running, until the hospital opens (with the button
/// that opens it at once), until an emergency's patients are lost and until the health inspector comes in an epidemic.
/// It opens by itself when a countdown starts and closes when none runs.
/// </summary>
internal sealed class WatchWindow : UiWindow
{
    public override string Title => "Watch";

    /// <summary>At the left, a little above the bottom, as CorsixTH's <c>UIWatch</c>.</summary>
    public override Vector2? DefaultPosition => new(20, -100);

    /// <summary>Whether a countdown runs for the player.</summary>
    public static bool IsRunning(Simulation simulation, int player)
    {
        var hospital = simulation.Hospitals[player];
        return !hospital.IsOpen || hospital.Emergencies.Current is { IsAccepted: true } || hospital.Epidemics.Current is { IsWatchRunning: true };
    }

    public override bool IsDone(IWindowHost host) => !IsRunning(host.Simulation, host.Player);

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var simulation = host.Simulation;
        var hospital = simulation.Hospitals[host.Player];
        if (!hospital.IsOpen)
        {
            var days = (hospital.HoursUntilOpen + GameDate.HoursPerDay - 1) / GameDate.HoursPerDay;
            yield return WindowRow.Of(
                string.Create(CultureInfo.InvariantCulture, $"Opening in {days} days"),
                Segments(hospital.OpenTimer + 1),
                WindowCell.Button("Open now", () => host.Submit(new OpenHospitalCommand())));
        }

        if (hospital.Emergencies.Current is { IsAccepted: true } emergency)
        {
            yield return WindowRow.Of(
                string.Create(CultureInfo.InvariantCulture, $"Emergency: {emergency.Cured} cured, {emergency.Killed} lost of {emergency.Victims}"),
                Segments(emergency.WatchTimer + 1));
        }

        if (hospital.Epidemics.Current is { IsWatchRunning: true } epidemic)
        {
            yield return WindowRow.Of(
                string.Create(CultureInfo.InvariantCulture, $"Epidemic: {Epidemics.CountInfectedPatients(simulation, epidemic)} infected, the inspector is coming"),
                Segments(epidemic.WatchTimer + 1));
        }
    }

    /// <summary>The watch's face as the segments left of 13.</summary>
    private static string Segments(int left) => new string('#', Math.Clamp(left, 0, 13)) + new string('.', 13 - Math.Clamp(left, 0, 13));
}
