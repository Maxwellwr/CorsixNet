using System.Globalization;
using CorsixNet.Client.Maps;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Disasters;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The faxes waiting for an answer (CorsixTH's <c>dialogs/fullscreen/fax.lua</c> with the messages of
/// <c>dialogs/bottom_panel.lua</c>), C here: a VIP asking to visit, an emergency, an epidemic to declare or cover up,
/// and patients asking what to do; each with the fax's choices. It opens by itself when a new fax arrives, as the
/// original's message icon calls for a look.
/// </summary>
internal sealed class FaxWindow : UiWindow
{
    /// <summary>The code typed on the fax's keypad (<c>UIFax.code</c>).</summary>
    private string _code = string.Empty;

    public override string Title => "Faxes";

    /// <summary>The code typed so far.</summary>
    internal string Code => _code;

    public override string? Hotkey => "C";

    /// <summary>The faxes waiting, as keys to tell new ones apart: what they are about and for whom.</summary>
    public static IEnumerable<string> Pending(Simulation simulation, int player)
    {
        var hospital = simulation.Hospitals[player];
        if (hospital.Vips.IsInvitationPending)
        {
            yield return $"vip:{hospital.Vips.InvitationExpires}";
        }

        if (hospital.Vips.Report is { } report)
        {
            yield return $"vipreport:{report.Expires}";
        }

        if (hospital.Emergencies.Report is { } result)
        {
            yield return $"emergencyreport:{result.Expires}";
        }

        if (hospital.Emergencies.Current is { IsAccepted: false } offer)
        {
            yield return $"emergency:{offer.OfferExpires}";
        }

        if (hospital.Epidemics.Current is { FaxExpires: > 0 } epidemic)
        {
            yield return $"epidemic:{epidemic.DiseaseId}:{epidemic.FaxExpires}";
        }

        foreach (var asking in Asking(simulation, player))
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"patient:{asking.Id}");
        }
    }

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var simulation = host.Simulation;
        var hospital = simulation.Hospitals[host.Player];
        var any = false;
        if (hospital.Vips.IsInvitationPending)
        {
            any = true;
            var name = host.Texts?.VipName(hospital.Vips.InvitedName) ?? string.Create(CultureInfo.InvariantCulture, $"VIP {hospital.Vips.InvitedName}");
            yield return WindowRow.Of(
                $"{name} would like to visit your hospital.",
                WindowCell.Button("Invite", () => host.Submit(new AnswerVipCommand(true))).WithTooltip("Invite the VIP to visit your hospital"),
                WindowCell.Button("Refuse", () => host.Submit(new AnswerVipCommand(false))).WithTooltip("Turn the VIP down"));
        }

        if (hospital.Vips.Report is { } report)
        {
            any = true;
            foreach (var line in VipReportLines(host, report))
            {
                yield return WindowCell.Line(line);
            }

            // The fax's one choice, close (fax.vip_visit_result.close_text).
            yield return WindowCell.Button(host.Texts?.String(44, 189) ?? "Close", () => host.Submit(new CloseReportCommand(FaxReport.Vip))) with { Tooltip = "Close this fax" };
        }

        if (hospital.Emergencies.Report is { } result)
        {
            any = true;
            foreach (var line in EmergencyReportLines(host, result))
            {
                yield return WindowCell.Line(line);
            }

            // fax.emergency_result.close_text.
            yield return WindowCell.Button(host.Texts?.String(44, 38) ?? "Close", () => host.Submit(new CloseReportCommand(FaxReport.Emergency))) with { Tooltip = "Close this fax" };
        }

        if (hospital.Emergencies.Current is { IsAccepted: false } offer)
        {
            any = true;
            yield return WindowRow.Of(
                string.Create(CultureInfo.InvariantCulture, $"Emergency: {offer.Victims} patients with {DiseaseName(host, offer.DiseaseId)}, cure {offer.Percentage} % for {MoneyText.Format(offer.Bonus)} each."),
                WindowCell.Button("Accept", () => host.Submit(new AnswerEmergencyCommand(true))).WithTooltip("Take the emergency in"),
                WindowCell.Button("Refuse", () => host.Submit(new AnswerEmergencyCommand(false))).WithTooltip("Send the emergency elsewhere"));
        }

        if (hospital.Epidemics.Current is { FaxExpires: > 0 } epidemic)
        {
            any = true;
            yield return WindowRow.Of(
                $"Epidemic of {DiseaseName(host, epidemic.DiseaseId)}: declare it and pay {MoneyText.Format(epidemic.DeclareFine)}, or try to cover it up.",
                WindowCell.Button("Declare", () => host.Submit(new AnswerEpidemicCommand(false))).WithTooltip("Declare the epidemic and pay the fine"),
                WindowCell.Button("Cover up", () => host.Submit(new AnswerEpidemicCommand(true))).WithTooltip("Try to cure every infected patient before the health inspector comes"));
        }

        foreach (var humanoid in Asking(simulation, host.Player))
        {
            any = true;
            var patient = (PatientBehaviour)humanoid.Behaviour!;
            var noDiagnosis = patient.Question == PatientQuestion.NoDiagnosisRoom;
            var text = noDiagnosis
                ? string.Create(CultureInfo.InvariantCulture, $"Patient {humanoid.Id}: no diagnosis room left, diagnosed {Math.Min(100, patient.DiagnosisProgress / 10)} %.")
                : $"Patient {humanoid.Id}: no room to treat {DiseaseName(host, patient.DiseaseId)}.";
            var cells = new List<WindowCell>
            {
                text,
                WindowCell.Button("Wait", () => host.Submit(new AnswerPatientCommand(humanoid.Id, PatientAnswer.Wait))).WithTooltip("Ask the patient to wait in the hospital"),
                WindowCell.Button("Send home", () => host.Submit(new AnswerPatientCommand(humanoid.Id, PatientAnswer.SendHome))).WithTooltip("Send the patient home"),
            };
            if (noDiagnosis)
            {
                cells.Add(WindowCell.Button("Guess cure", () => host.Submit(new AnswerPatientCommand(humanoid.Id, PatientAnswer.GuessCure))).WithTooltip("Treat the patient for the likeliest disease"));
            }
            else if (PatientBehaviour.CanGoToResearch(simulation, humanoid))
            {
                cells.Add(WindowCell.Button("Research", () => host.Submit(new AnswerPatientCommand(humanoid.Id, PatientAnswer.Research))).WithTooltip("Send the patient to the research department"));
            }

            cells.Add(WindowCell.Button("Patient", () => host.Open(new PatientWindow(humanoid.Id))).WithTooltip("Look at the patient"));
            yield return new WindowRow(cells);
        }

        if (!any)
        {
            yield return WindowCell.Line("No faxes waiting.", dim: true);
        }

        // The keypad (UIFax:appendNumber, cancel, correct, validate): codes typed here can unlock cheats.
        yield return WindowRow.Gap;
        yield return WindowRow.Of("Code", _code.Length > 0 ? _code : "-");
        yield return new WindowRow("12345".Select(digit => Key(digit.ToString())).ToList());
        yield return new WindowRow("67890".Select(digit => Key(digit.ToString())).ToList());
        yield return WindowRow.Of(
            WindowCell.Button("Cancel", () => _code = string.Empty, _code.Length > 0).WithTooltip("Clear the code"),
            WindowCell.Button("Correct", () => _code = _code[..^1], _code.Length > 0).WithTooltip("Delete the last digit"),
            WindowCell.Button("Validate", () => Validate(host), _code.Length > 0).WithTooltip("Send the code"));
    }

    /// <summary>
    /// <c>PlayerHospital:makeVipEndFax</c>: who remarked, their remark for the rating, and for a pleased VIP the
    /// reputation boost and the grant, for a displeased one the loss; in free build mode one of three remarks of its
    /// own. The original's strings (section 44) are used when loaded; the English stand-ins are ours, except free build's.
    /// </summary>
    internal static IEnumerable<string> VipReportLines(IWindowHost host, VipReport report)
    {
        var name = host.Texts?.VipName(report.Name) ?? string.Create(CultureInfo.InvariantCulture, $"VIP {report.Name}");
        yield return new Advice(44, 192, "%s has made a report on your hospital:").Text(host.Texts, name);
        if (host.Simulation.Rules.FreeBuild)
        {
            // CorsixTH picks one at random; this one stays the same for the visit.
            yield return FreeBuildRemarks[(int)(report.Expires % FreeBuildRemarks.Length)];
            yield break;
        }

        yield return new Advice(44, 195 + report.Rating, Remark(report.Rating)).Text(host.Texts);
        if (report.Rating <= VipBehaviour.PleasedRating)
        {
            yield return new Advice(44, 194, "Your reputation has gone up.").Text(host.Texts);
            yield return new Advice(44, 193, "You have been given a grant of %d.").Text(host.Texts, report.CashReward);
        }
        else if (report.Rating >= 11)
        {
            yield return new Advice(44, 195, "Your reputation has gone down.").Text(host.Texts);
        }
    }

    /// <summary>
    /// <c>PlayerHospital:makeEmergencyEndFax</c>: how many of the emergency's patients were saved, and, outside free
    /// build mode, the bonus there was and what was earned. The original's strings (section 44) are used when loaded;
    /// the English stand-ins are ours.
    /// </summary>
    internal static IEnumerable<string> EmergencyReportLines(IWindowHost host, EmergencyReport report)
    {
        yield return new Advice(44, 41, "You saved %d of the %d people in the emergency.").Text(host.Texts, report.Rescued, report.Total);
        if (!host.Simulation.Rules.FreeBuild)
        {
            yield return new Advice(44, 40, "The most you could have earned was %d. You earned %d.").Text(host.Texts, report.MaxBonus, report.Earned);
        }
    }

    /// <summary><c>fax.vip_visit_result.remarks.free_build</c> (english.lua).</summary>
    private static readonly string[] FreeBuildRemarks =
    [
        "It is a very nice hospital you have there! Not very hard to get it working without money limitations though, eh?",
        "I'm no economist, but I think I could run this hospital too if you know what I mean...",
        "A very well run hospital. Watch out for the recession though! Right... you don't have to worry about that.",
    ];

    /// <summary>Our stand-ins for the original's fifteen remarks, best to worst.</summary>
    private static string Remark(int rating) => rating switch
    {
        <= 3 => "A superb hospital. I was most impressed.",
        <= 7 => "A good hospital, well run.",
        <= 10 => "An average hospital. It could do better.",
        _ => "A poor hospital. Things must improve.",
    };

    private WindowCell Key(string digit) => WindowCell.Button(digit, () => _code += digit, _code.Length < 10).WithTooltip($"Type {digit}");

    /// <summary>
    /// <c>UIFax:validate</c>: 24328, the original game's code, opens the cheats; 112 is a friendly hello; the other codes
    /// are checked against the toggle cheats' (<c>Cheats:processCheatCode</c>). A code that does nothing buzzes.
    /// </summary>
    private void Validate(IWindowHost host)
    {
        var code = _code;
        _code = string.Empty;
        switch (FaxCodes.Check(code))
        {
            case FaxCode.Cheats:
                host.Adviser.Say("Congratulations, you have unlocked cheats!", Environment.TickCount64);
                host.Open(new CheatsWindow());
                break;
            case FaxCode.Hello:
                break;
            case FaxCode.Toggle when FaxCodes.ToggleCheat(code) is { } toggle:
                var wasOn = host.Simulation.Hospitals[host.Player].ActiveCheats.HasFlag(toggle.Flag);
                host.Submit(new CheatCommand(toggle.Cheat));
                host.Adviser.Say(wasOn ? toggle.Off : toggle.On, Environment.TickCount64);
                break;
            default:
                host.PlaySound("fax_no.wav");
                return;
        }

        host.PlaySound("fax_yes.wav");
    }

    private static IEnumerable<Humanoid> Asking(Simulation simulation, int player) =>
        simulation.Entities.OfType<Humanoid>().Where(humanoid => humanoid is { Behaviour: PatientBehaviour { Question: not PatientQuestion.None }, IsDespawning: false } && humanoid.HospitalPlayer == player);

    private static string DiseaseName(IWindowHost host, string diseaseId) => host.Texts?.DiseaseName(diseaseId) ?? diseaseId;
}

/// <summary>What a code typed on the fax does.</summary>
internal enum FaxCode
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>Opens the cheats (24328).</summary>
    Cheats,

    /// <summary>The friendly 112: an announcement in CorsixTH.</summary>
    Hello,

    /// <summary>Turns one of the toggle cheats on or off.</summary>
    Toggle,
}

/// <summary>
/// The fax's codes (CorsixTH's <c>UIFax:validate</c> and the <c>toggle_cheats</c> list of <c>cheats.lua</c>): the
/// toggle cheats' codes are hidden behind the same sum CorsixTH checks, not written out.
/// </summary>
internal static class FaxCodes
{
    /// <summary>The toggle cheats: the window between which the code's value must fall, and the adviser's words.</summary>
    private static readonly (double Lower, double Upper, CheatKind Cheat, ToggleCheats Flag, string On, string Off)[] Toggles =
    [
        (27868.3, 27868.4, CheatKind.ToggleSpawnRate, ToggleCheats.SpawnRate, "Roujin's challenge activated! Good luck in the coming months...", "Roujin's challenge deactivated. Everything will be back to normal soon."),
        (185.5, 185.6, CheatKind.ToggleNoRest, ToggleCheats.NoRest, "Oh no! It appears your staff have consumed too much caffeine and no longer feel a need to rest.", "Phew! Looks like that buzz finally wore off. Your staff will now rest properly."),
        (200.5, 200.6, CheatKind.ToggleQueueJump, ToggleCheats.QueueJump, "Your patients know how to queue. They let nearly dead patients go to the front of the line.", "People have turned selfish and are no longer letting nearly dead patients jump the queues."),
        (301.5, 301.6, CheatKind.ToggleSuperDoctor, ToggleCheats.SuperDoctor, "A superb medical school has recommended you to their graduates! Check the staff for hire.", "The medical school chief no longer recommends you to their graduates."),
    ];

    /// <summary>What the code does.</summary>
    public static FaxCode Check(string code) => code switch
    {
        "24328" => FaxCode.Cheats,
        "112" => FaxCode.Hello,
        _ => ToggleCheat(code) is not null ? FaxCode.Toggle : FaxCode.None,
    };

    /// <summary>The toggle cheat a code turns on or off, or null.</summary>
    public static (CheatKind Cheat, ToggleCheats Flag, string On, string Off)? ToggleCheat(string code)
    {
        var number = double.TryParse(code, System.Globalization.NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed / 1e5 : 0;
        var value = Math.Abs(((Math.Pow(number, 5.00001) - Math.Pow(number, 5)) * 1e5) - Math.Pow(number, 5));
        foreach (var (lower, upper, cheat, flag, on, off) in Toggles)
        {
            if (lower < value && upper > value)
            {
                return (cheat, flag, on, off);
            }
        }

        return null;
    }
}
