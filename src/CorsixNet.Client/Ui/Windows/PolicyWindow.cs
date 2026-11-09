using System.Globalization;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;

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

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var policies = host.Simulation.Hospitals[host.Player].Policies;
        yield return WindowCell.Line("Diagnosis", highlight: true);
        yield return Slider(host, "Send home below", PolicyKind.SendHome, policies.SendHome, Percent(policies.SendHome));
        yield return Slider(host, "Guess cure from", PolicyKind.GuessCure, policies.GuessCure, Percent(policies.GuessCure));
        yield return Slider(host, "Stop procedure at", PolicyKind.StopProcedure, policies.StopProcedure, Percent(policies.StopProcedure));
        yield return WindowRow.Gap;
        yield return WindowCell.Line("Staff", highlight: true);
        yield return Slider(host, "Staff room when tired", PolicyKind.GoToStaffRoom, policies.GoToStaffRoom, Percent(policies.GoToStaffRoom));
        yield return WindowRow.Of(
            "Staff leave their rooms",
            Switch(host, "Allowed", PolicyKind.StaffAllowedToMove, policies.StaffAllowedToMove, on: true),
            Switch(host, "Not allowed", PolicyKind.StaffAllowedToMove, policies.StaffAllowedToMove, on: false));
        yield return WindowRow.Of(
            "Raises staff ask for",
            Switch(host, "Grant", PolicyKind.GrantWageIncrease, policies.GrantWageIncrease, on: true),
            Switch(host, "Ask me", PolicyKind.GrantWageIncrease, policies.GrantWageIncrease, on: false));
    }

    private static string Percent(int thousandths) => string.Create(CultureInfo.InvariantCulture, $"{thousandths / 10} %");

    private static WindowRow Slider(IWindowHost host, string label, PolicyKind policy, int value, string shown) => WindowRow.Of(
        label,
        WindowCell.Number(shown),
        Step(host, "-10", policy, value - 100),
        Step(host, "-1", policy, value - 10),
        Step(host, "+1", policy, value + 10),
        Step(host, "+10", policy, value + 100));

    private static WindowCell Step(IWindowHost host, string text, PolicyKind policy, int value) =>
        WindowCell.Button(text, () =>
        {
            host.Submit(new SetPolicyCommand(policy, value));
            host.PlaySound("selectx.wav");
        });

    private static WindowCell Switch(IWindowHost host, string text, PolicyKind policy, bool current, bool on) =>
        WindowCell.Button(text, () => host.Submit(new SetPolicyCommand(policy, on ? 1 : 0)), enabled: current != on, highlight: current == on);
}
