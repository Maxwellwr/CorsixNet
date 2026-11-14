using System.Globalization;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The hospital policy window (CorsixTH's <c>dialogs/fullscreen/hospital_policy.lua</c>, F9): the diagnosis thresholds,
/// when staff go to the staff room, whether staff may leave their rooms and whether raises are granted. The sliders are
/// buttons that move them by a step; the simulation keeps them in range as the window's sliders do.
/// </summary>
internal sealed class PolicyWindow : UiWindow
{
    public override string Title => "Hospital policy";

    public override string? Hotkey => "F9";

    private readonly PolicyScreen _screen = new();

    /// <summary>The original's look (<see cref="PolicyScreen"/>) where its graphics are there; else the table below.</summary>
    public override bool IsFullscreen(IWindowHost host) => PolicyScreen.IsAvailable(host);

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse) => _screen.Draw(batch, host, origin, mouse);

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous) => _screen.Mouse(host, this, local, mouse, previous);

    public override string? FullscreenTooltip(IWindowHost host, Point local) => PolicyScreen.Tooltip(host, local);
    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var policies = host.Simulation.Hospitals[host.Player].Policies;
        yield return WindowCell.Line("Diagnosis", highlight: true);
        yield return Slider(host, "Send home below", PolicyKind.SendHome, policies.SendHome, Percent(policies.SendHome), OriginalTooltips.Get(host, 233, "Patients less certainly diagnosed than this are sent home, those more certain than the guess get the cure"));
        yield return Slider(host, "Guess cure from", PolicyKind.GuessCure, policies.GuessCure, Percent(policies.GuessCure), OriginalTooltips.Get(host, 233, "Patients less certainly diagnosed than this are sent home, those more certain than the guess get the cure"));
        yield return Slider(host, "Stop procedure at", PolicyKind.StopProcedure, policies.StopProcedure, Percent(policies.StopProcedure), OriginalTooltips.Get(host, 234, "Diagnosis goes on until the doctors are this certain"));
        yield return WindowRow.Gap;
        yield return WindowCell.Line("Staff", highlight: true);
        yield return Slider(host, "Staff room when tired", PolicyKind.GoToStaffRoom, policies.GoToStaffRoom, Percent(policies.GoToStaffRoom), OriginalTooltips.Get(host, 235, "How tired staff are before they rest in the staff room"));
        yield return WindowRow.Of(
            "Staff leave their rooms",
            Switch(host, "Allowed", PolicyKind.StaffAllowedToMove, policies.StaffAllowedToMove, on: true).WithTooltip(OriginalTooltips.Get(host, 231, "Staff may leave their rooms to work where they are needed")),
            Switch(host, "Not allowed", PolicyKind.StaffAllowedToMove, policies.StaffAllowedToMove, on: false).WithTooltip(OriginalTooltips.Get(host, 232, "Staff stay in the rooms you put them in")));
        yield return WindowRow.Of(
            "Raises staff ask for",
            Switch(host, "Grant", PolicyKind.GrantWageIncrease, policies.GrantWageIncrease, on: true).WithTooltip("Raises staff ask for are given at once (the options menu's wage requests)"),
            Switch(host, "Ask me", PolicyKind.GrantWageIncrease, policies.GrantWageIncrease, on: false).WithTooltip("You are asked about each raise staff ask for"));
    }

    private static string Percent(int thousandths) => string.Create(CultureInfo.InvariantCulture, $"{thousandths / 10} %");

    private static WindowRow Slider(IWindowHost host, string label, PolicyKind policy, int value, string shown, string tooltip) => WindowRow.Of(
        new WindowCell(label) { Tooltip = tooltip },
        WindowCell.Number(shown),
        Step(host, "-10", policy, value - 100).WithTooltip(tooltip),
        Step(host, "-1", policy, value - 10).WithTooltip(tooltip),
        Step(host, "+1", policy, value + 10).WithTooltip(tooltip),
        Step(host, "+10", policy, value + 100).WithTooltip(tooltip));

    private static WindowCell Step(IWindowHost host, string text, PolicyKind policy, int value) =>
        WindowCell.Button(text, () =>
        {
            host.Submit(new SetPolicyCommand(policy, value));
        });

    private static WindowCell Switch(IWindowHost host, string text, PolicyKind policy, bool current, bool on) =>
        WindowCell.Button(text, () => host.Submit(new SetPolicyCommand(policy, on ? 1 : 0)), enabled: current != on, highlight: current == on);
}
