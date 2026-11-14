using System.Globalization;
using CorsixNet.Client.Maps;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Disasters;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

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

    public override string Title => UiText.Get("corsixnet.toolbar.faxes", "Faxes");

    /// <summary>The code typed so far.</summary>
    internal string Code => _code;

    public override string? Hotkey => "C";

    private readonly FaxScreen _screen = new();

    /// <summary>The original's look (<see cref="FaxScreen"/>) where its graphics are there; else the table below.</summary>
    public override bool IsFullscreen(IWindowHost host) => FaxScreen.IsAvailable(host);

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse) =>
        _screen.Draw(batch, host, origin, mouse, Messages(host).FirstOrDefault());

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous)
    {
        var message = Messages(host).FirstOrDefault();
        switch (_screen.Clicked(local, mouse, previous, message))
        {
            case "close":
                host.PlaySound("no4.wav");
                host.Close(this);
                break;
            case "cancel":
                _code = string.Empty;
                break;
            case "correct":
                _code = _code.Length > 0 ? _code[..^1] : _code;
                break;
            case "validate":
                if (_code.Length > 0)
                {
                    Validate(host);
                }

                break;
            case { } key when key.StartsWith("key", StringComparison.Ordinal):
                // appendNumber, with the key's tone (Fax_0 to Fax_9.wav).
                var typed = key[3..];
                if (char.IsDigit(typed[0]))
                {
                    host.PlaySound("Fax_" + typed + ".wav");
                }

                if (_code.Length < 10)
                {
                    _code += typed;
                }

                break;
            case { } choice when choice.StartsWith("choice", StringComparison.Ordinal) && message is not null:
                // UIFax:choice: the answer, and the fax goes; with no other fax waiting the window closes.
                host.PlaySound(WindowLayer.DefaultButtonSound);
                message.Choices[int.Parse(choice[6..], CultureInfo.InvariantCulture)].Choose();
                if (Pending(host.Simulation, host.Player).Count() <= 1)
                {
                    host.Close(this);
                }

                break;
        }
    }

    /// <summary>
    /// The faxes waiting, one message each, in the order the table lists them: the VIP's invitation, the reports, the
    /// emergency, the epidemic and the patients asking what to do; each with its choices.
    /// </summary>
    private static string EmergencyOfferText(IWindowHost host, Sim.Disasters.Emergency offer) =>
        UiText.Format("corsixnet.fax.emergency_offer", "Emergency: %1% patients with %2%, cure %3% %% for %4% each.", offer.Victims, DiseaseName(host, offer.DiseaseId), offer.Percentage, MoneyText.Format(offer.Bonus));

    private static string EpidemicOfferText(IWindowHost host, Sim.Disasters.Epidemic epidemic) =>
        UiText.Format("corsixnet.fax.epidemic_offer", "Epidemic of %1%: declare it and pay %2%, or try to cover it up.", DiseaseName(host, epidemic.DiseaseId), MoneyText.Format(epidemic.DeclareFine));

    internal List<FaxMessage> Messages(IWindowHost host)
    {
        var simulation = host.Simulation;
        var hospital = simulation.Hospitals[host.Player];
        var messages = new List<FaxMessage>();
        if (host.IsWinLetterPending)
        {
            // World:winGame's letter: on to the next level (or back to the menu after the last), or stay a while longer.
            var next = host.HasNextLevel;
            messages.Add(new FaxMessage(
                [.. WinLetterLines(host)],
                [(next ? host.Texts?.Named("fax.choices.accept_new_level") ?? "Move on to the next level" : host.Texts?.Named("fax.choices.return_to_main_menu") ?? "Return to the main menu", next, () => host.StartLevel(next: true)),
                 (host.Texts?.Named("fax.choices.decline_new_level") ?? "Continue playing a while longer", true, host.DeclineWinLetter)]));
        }
        if (hospital.Vips.IsInvitationPending)
        {
            var name = host.Texts?.VipName(hospital.Vips.InvitedName) ?? UiText.Format("corsixnet.fax.vip_number", "VIP %1%", hospital.Vips.InvitedName);
            messages.Add(new FaxMessage(
                [host.Texts?.String(44, 187)?.Replace("%s", name, StringComparison.Ordinal) ?? UiText.Format("corsixnet.fax.vip_visit", "%1% would like to visit your hospital.", name)],
                [(host.Texts?.String(44, 184) ?? "Invite the VIP", true, () => host.Submit(new AnswerVipCommand(true))), (host.Texts?.String(44, 185) ?? UiText.Get("corsixnet.fax.refuse_tooltip", "Turn the VIP down"), true, () => host.Submit(new AnswerVipCommand(false)))]));
        }

        if (hospital.Vips.Report is { } report)
        {
            messages.Add(new FaxMessage([.. VipReportLines(host, report)], [(host.Texts?.String(44, 189) ?? "Close", true, () => host.Submit(new CloseReportCommand(FaxReport.Vip)))]));
        }

        if (hospital.Emergencies.Report is { } result)
        {
            messages.Add(new FaxMessage([.. EmergencyReportLines(host, result)], [(host.Texts?.String(44, 38) ?? "Close", true, () => host.Submit(new CloseReportCommand(FaxReport.Emergency)))]));
        }
        foreach (var discovered in hospital.DiseaseReports)
        {
            messages.Add(new FaxMessage([.. DiseaseReportLines(host, discovered)], [(host.Texts?.String(44, 55) ?? "A new disease has been found", true, () => host.Submit(new CloseReportCommand(FaxReport.Disease)))]));
        }

        if (hospital.Epidemics.Report is { } epidemicResult)
        {
            messages.Add(new FaxMessage([.. EpidemicReportLines(host, epidemicResult)], [(host.Texts?.String(44, 173) ?? "Close", true, () => host.Submit(new CloseReportCommand(FaxReport.Epidemic)))]));
        }

        if (hospital.Emergencies.Current is { IsAccepted: false } offer)
        {
            messages.Add(new FaxMessage(
                [.. EmergencyStartLines(host, offer)],
                [(host.Texts?.String(44, 15) ?? "Accept the emergency", true, () => host.Submit(new AnswerEmergencyCommand(true))), (host.Texts?.String(44, 16) ?? UiText.Get("corsixnet.fax.refuse_emergency_tooltip", "Send the emergency elsewhere"), true, () => host.Submit(new AnswerEmergencyCommand(false)))]));
        }

        if (hospital.Epidemics.Current is { FaxExpires: > 0 } epidemic)
        {
            messages.Add(new FaxMessage(
                [.. EpidemicStartLines(host, epidemic)],
                [(host.Texts?.String(44, 165) ?? "Declare the epidemic", true, () => host.Submit(new AnswerEpidemicCommand(false))), (host.Texts?.String(44, 166) ?? "Try to cover it up", true, () => host.Submit(new AnswerEpidemicCommand(true)))]));
        }

        foreach (var humanoid in Asking(simulation, host.Player))
        {
            var patient = (PatientBehaviour)humanoid.Behaviour!;
            var id = humanoid.Id;
            void Answer(PatientAnswer answer) => host.Submit(new AnswerPatientCommand(id, answer));
            string S(int index, string fallback) => host.Texts?.String(44, index) ?? fallback;
            if (patient.Question == PatientQuestion.NoDiagnosisRoom)
            {
                // makeNoDiagnosisRoomFax: send home, take a chance (once the disease is known) or wait.
                var known = patient.Disease is { } disease && simulation.Hospitals[host.Player].PeekCase(disease, simulation.Rules.Diseases).Discovered;
                messages.Add(new FaxMessage(
                    [.. NoDiagnosisRoomLines(host, patient, known)],
                    [(S(212, "Send them home"), true, () => Answer(PatientAnswer.SendHome)), (S(213, "Take a chance on the likeliest cure"), known, () => Answer(PatientAnswer.GuessCure)), (S(214, "Ask them to wait"), true, () => Answer(PatientAnswer.Wait))]));
            }
            else
            {
                // makeNoTreatmentRoomFax: send home, wait, or research (with a research room and a researcher).
                messages.Add(new FaxMessage(
                    [.. NoTreatmentRoomLines(host, patient)],
                    [(S(43, "Send them home"), true, () => Answer(PatientAnswer.SendHome)), (S(44, "Ask them to wait"), true, () => Answer(PatientAnswer.Wait)), (S(45, "Send them to research"), PatientBehaviour.CanGoToResearch(simulation, humanoid), () => Answer(PatientAnswer.Research))]));
            }
        }

        return messages;
    }
    /// <summary>The faxes waiting, as keys to tell new ones apart: what they are about and for whom.</summary>
    public static IEnumerable<string> Pending(Simulation simulation, int player, bool winLetter = false)
    {
        var hospital = simulation.Hospitals[player];
        if (winLetter)
        {
            yield return "winletter:";
        }

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
        foreach (var discovered in hospital.DiseaseReports)
        {
            yield return $"diseasereport:{discovered.DiseaseId}:{discovered.Expires}";
        }

        if (hospital.Epidemics.Report is { } epidemicResult)
        {
            yield return $"epidemicreport:{epidemicResult.Expires}";
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
            var name = host.Texts?.VipName(hospital.Vips.InvitedName) ?? UiText.Format("corsixnet.fax.vip_number", "VIP %1%", hospital.Vips.InvitedName);
            yield return WindowRow.Of(
                UiText.Format("corsixnet.fax.vip_visit", "%1% would like to visit your hospital.", name),
                WindowCell.Button(UiText.Get("corsixnet.fax.invite", "Invite"), () => host.Submit(new AnswerVipCommand(true))).WithTooltip(UiText.Get("corsixnet.fax.invite_tooltip", "Invite the VIP to visit your hospital")),
                WindowCell.Button(UiText.Get("corsixnet.fax.refuse", "Refuse"), () => host.Submit(new AnswerVipCommand(false))).WithTooltip(UiText.Get("corsixnet.fax.refuse_tooltip", "Turn the VIP down")));
        }

        if (hospital.Vips.Report is { } report)
        {
            any = true;
            foreach (var line in VipReportLines(host, report))
            {
                yield return WindowCell.Line(line);
            }

            // The fax's one choice, close (fax.vip_visit_result.close_text).
            yield return WindowCell.Button(host.Texts?.String(44, 189) ?? "Close", () => host.Submit(new CloseReportCommand(FaxReport.Vip))) with { Tooltip = UiText.Get("corsixnet.fax.close_tooltip", "Close this fax") };
        }

        if (hospital.Emergencies.Report is { } result)
        {
            any = true;
            foreach (var line in EmergencyReportLines(host, result))
            {
                yield return WindowCell.Line(line);
            }

            // fax.emergency_result.close_text.
            yield return WindowCell.Button(host.Texts?.String(44, 38) ?? "Close", () => host.Submit(new CloseReportCommand(FaxReport.Emergency))) with { Tooltip = UiText.Get("corsixnet.fax.close_tooltip", "Close this fax") };
        }
        foreach (var discovered in hospital.DiseaseReports)
        {
            any = true;
            foreach (var line in DiseaseReportLines(host, discovered))
            {
                yield return WindowCell.Line(line);
            }

            // fax.disease_discovered.close_text.
            yield return WindowCell.Button(host.Texts?.String(44, 55) ?? "A new disease has been found", () => host.Submit(new CloseReportCommand(FaxReport.Disease))) with { Tooltip = UiText.Get("corsixnet.fax.close_tooltip", "Close this fax") };
        }

        if (hospital.Epidemics.Report is { } epidemicResult)
        {
            any = true;
            foreach (var line in EpidemicReportLines(host, epidemicResult))
            {
                yield return WindowCell.Line(line);
            }

            // fax.epidemic_result.close_text.
            yield return WindowCell.Button(host.Texts?.String(44, 173) ?? "Close", () => host.Submit(new CloseReportCommand(FaxReport.Epidemic))) with { Tooltip = UiText.Get("corsixnet.fax.close_tooltip", "Close this fax") };
        }

        if (hospital.Emergencies.Current is { IsAccepted: false } offer)
        {
            any = true;
            yield return WindowRow.Of(
                EmergencyOfferText(host, offer),
                WindowCell.Button(UiText.Get("corsixnet.fax.accept", "Accept"), () => host.Submit(new AnswerEmergencyCommand(true))).WithTooltip(UiText.Get("corsixnet.fax.accept_tooltip", "Take the emergency in")),
                WindowCell.Button(UiText.Get("corsixnet.fax.refuse", "Refuse"), () => host.Submit(new AnswerEmergencyCommand(false))).WithTooltip(UiText.Get("corsixnet.fax.refuse_emergency_tooltip", "Send the emergency elsewhere")));
        }

        if (hospital.Epidemics.Current is { FaxExpires: > 0 } epidemic)
        {
            any = true;
            yield return WindowRow.Of(
                EpidemicOfferText(host, epidemic),
                WindowCell.Button(UiText.Get("corsixnet.fax.declare", "Declare"), () => host.Submit(new AnswerEpidemicCommand(false))).WithTooltip(UiText.Get("corsixnet.fax.declare_tooltip", "Declare the epidemic and pay the fine")),
                WindowCell.Button(UiText.Get("corsixnet.fax.cover_up", "Cover up"), () => host.Submit(new AnswerEpidemicCommand(true))).WithTooltip(UiText.Get("corsixnet.fax.cover_up_tooltip", "Try to cure every infected patient before the health inspector comes")));
        }

        foreach (var humanoid in Asking(simulation, host.Player))
        {
            any = true;
            var patient = (PatientBehaviour)humanoid.Behaviour!;
            var noDiagnosis = patient.Question == PatientQuestion.NoDiagnosisRoom;
            var text = noDiagnosis
                ? UiText.Format("corsixnet.fax.patient_no_diagnosis", "Patient %1%: no diagnosis room left, diagnosed %2% %%.", humanoid.Id, Math.Min(100, patient.DiagnosisProgress / 10))
                : UiText.Format("corsixnet.fax.patient_no_treatment", "Patient %1%: no room to treat %2%.", humanoid.Id, DiseaseName(host, patient.DiseaseId));
            var cells = new List<WindowCell>
            {
                text,
                WindowCell.Button(UiText.Get("corsixnet.fax.wait", "Wait"), () => host.Submit(new AnswerPatientCommand(humanoid.Id, PatientAnswer.Wait))).WithTooltip(UiText.Get("corsixnet.fax.wait_tooltip", "Ask the patient to wait in the hospital")),
                WindowCell.Button(UiText.Get("corsixnet.patient.send_home", "Send home"), () => host.Submit(new AnswerPatientCommand(humanoid.Id, PatientAnswer.SendHome))).WithTooltip(UiText.Get("tooltip.queue_window.patient_dropdown.send_home", "Send the patient home")),
            };
            if (noDiagnosis)
            {
                cells.Add(WindowCell.Button(UiText.Get("corsixnet.patient.guess_cure", "Guess cure"), () => host.Submit(new AnswerPatientCommand(humanoid.Id, PatientAnswer.GuessCure))).WithTooltip(UiText.Get("corsixnet.fax.guess_cure_tooltip", "Treat the patient for the likeliest disease")));
            }
            else if (PatientBehaviour.CanGoToResearch(simulation, humanoid))
            {
                cells.Add(WindowCell.Button(UiText.Get("corsixnet.menu.research", "Research"), () => host.Submit(new AnswerPatientCommand(humanoid.Id, PatientAnswer.Research))).WithTooltip(UiText.Get("corsixnet.fax.research_tooltip", "Send the patient to the research department")));
            }

            cells.Add(WindowCell.Button(UiText.Get("corsixnet.patient.caption", "Patient"), () => host.Open(new PatientWindow(humanoid.Id))).WithTooltip(UiText.Get("corsixnet.fax.patient_tooltip", "Look at the patient")));
            yield return new WindowRow(cells);
        }

        if (!any)
        {
            yield return WindowCell.Line(UiText.Get("corsixnet.fax.none", "No faxes waiting."), dim: true);
        }

        // The keypad (UIFax:appendNumber, cancel, correct, validate): codes typed here can unlock cheats.
        yield return WindowRow.Gap;
        yield return WindowRow.Of(UiText.Get("corsixnet.fax.code", "Code"), _code.Length > 0 ? _code : "-");
        yield return new WindowRow("12345".Select(digit => Key(digit.ToString())).ToList());
        yield return new WindowRow("67890".Select(digit => Key(digit.ToString())).ToList());
        yield return WindowRow.Of(
            WindowCell.Button(UiText.Get("graphics_window.cancel", "Cancel"), () => _code = string.Empty, _code.Length > 0).WithTooltip(UiText.Get("corsixnet.fax.clear_tooltip", "Clear the code")),
            WindowCell.Button(UiText.Get("corsixnet.fax.correct", "Correct"), () => _code = _code[..^1], _code.Length > 0).WithTooltip(UiText.Get("corsixnet.fax.correct_tooltip", "Delete the last digit")),
            WindowCell.Button(UiText.Get("corsixnet.fax.validate", "Validate"), () => Validate(host), _code.Length > 0).WithTooltip(UiText.Get("corsixnet.fax.validate_tooltip", "Send the code")));
    }

    /// <summary>
    /// <c>PlayerHospital:makeVipEndFax</c>: who remarked, their remark for the rating, and for a pleased VIP the
    /// reputation boost and the grant, for a displeased one the loss; in free build mode one of three remarks of its
    /// own. The original's strings (section 44) are used when loaded; the English stand-ins are ours, except free build's.
    /// </summary>
    internal static IEnumerable<string> VipReportLines(IWindowHost host, VipReport report)
    {
        var name = host.Texts?.VipName(report.Name) ?? UiText.Format("corsixnet.fax.vip_number", "VIP %1%", report.Name);
        yield return new Advice(44, 192, "%s has made a report on your hospital:").Text(host.Texts, name);
        if (host.Simulation.Rules.FreeBuild)
        {
            // CorsixTH picks one at random; this one stays the same for the visit.
            var remark = (int)(report.Expires % FreeBuildRemarks.Length);
            yield return UiText.Get("fax.vip_visit_result.remarks.free_build." + (remark + 1), FreeBuildRemarks[remark]);
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

    /// <summary>
    /// World:getCampaignWinningText for an original level: one of the four letters (1 to 4 offering the next level, 9 to
    /// 12 after the last one) of S[39], to the player, with the salary offered and the next town.
    /// </summary>
    internal static IEnumerable<string> WinLetterLines(IWindowHost host)
    {
        var simulation = host.Simulation;
        var hospital = simulation.Hospitals[host.Player];
        var letters = Letters(host.Texts);
        var index = (simulation.Date.MonthOfGame % 4) + 1 + (host.HasNextLevel ? 0 : 8);
        if (TranslatedLetter(host.Texts, index) is { Count: >= 3 } translated)
        {
            while (letters.Count <= index)
            {
                letters.Add([]);
            }

            letters[index] = translated;
        }
        if (index >= letters.Count || letters[index].Count < 3)
        {
            yield return UiText.Get("corsixnet.progress.won", "Congratulations! You have met every goal of this level.");
            if (hospital.SalaryOffer > 0)
            {
                yield return string.Create(CultureInfo.InvariantCulture, $"{UiText.Get("corsixnet.progress.new_salary", "Your new salary")}: {MoneyText.Format(hospital.SalaryOffer)}");
            }

            yield break;
        }

        var arguments = new object[][] { [host.PlayerName], [hospital.SalaryOffer], [host.NextLevelTitle ?? string.Empty] };
        for (var i = 0; i < letters[index].Count; i++)
        {
            var text = letters[index][i];
            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return i < arguments.Length ? new Advice(0, 0, text).Text(null, arguments[i]) : text;
            }
        }
    }

    /// <summary>The chosen translation's letter (<c>letter[index]</c>), its lines without the "//" ending them, or null.</summary>
    private static List<string>? TranslatedLetter(CorsixNet.Content.OriginalTexts? texts, int index)
    {
        var lines = new List<string>();
        for (var i = 1; texts?.Named(string.Create(CultureInfo.InvariantCulture, $"letter.{index}.{i}")) is { } line; i++)
        {
            lines.Add(line.Replace("//", string.Empty, StringComparison.Ordinal));
        }

        return lines.Count > 0 ? lines : null;
    }

    /// <summary>
    /// original_strings.lua's letters: S[39] read in order, "." starting the next letter and ".." ending them, a line
    /// ending at each "//".
    /// </summary>
    internal static List<List<string>> Letters(CorsixNet.Content.OriginalTexts? texts)
    {
        var letters = new List<List<string>> { new() { string.Empty } };
        for (var i = 1; texts is not null && i < 1000; i++)
        {
            if (texts.String(39, i) is not { } s)
            {
                break;
            }

            if (s == ".")
            {
                letters.Add([string.Empty]);
            }
            else if (s == "..")
            {
                letters.RemoveAt(letters.Count - 1);
                break;
            }
            else
            {
                var letter = letters[^1];
                letter[^1] += s.Replace("//", string.Empty, StringComparison.Ordinal);
                if (s.Contains("//", StringComparison.Ordinal))
                {
                    letter.Add(string.Empty);
                }
            }
        }

        return letters;
    }

    /// <summary>
    /// makeEmergencyStartFax's lines: where it is, how many have the disease, whether it can be cured (the drug's
    /// effectiveness, or the room to build and the staff to hire for the last treatment room), and the bonus.
    /// </summary>
    internal static IEnumerable<string> EmergencyStartLines(IWindowHost host, Sim.Disasters.Emergency offer)
    {
        string S(int index, string fallback, params object[] arguments) => new Advice(44, index, fallback).Text(host.Texts, arguments);
        var simulation = host.Simulation;
        var name = DiseaseName(host, offer.DiseaseId);

        // _S.fax.emergency.locations[math.random(1, 9)]: here the same place for the same emergency.
        var location = S(27 + (int)((ulong)offer.OfferExpires % 9), "a nearby town");
        yield return S(18, "There has been an emergency at %s.", location);
        yield return offer.Victims == 1
            ? host.Texts?.Named("fax.emergency.num_disease_singular")?.Replace("%s", name, StringComparison.Ordinal) ?? $"There is 1 person with {name} needing immediate attention."
            : S(19, "There are %d people with %s needing immediate attention.", offer.Victims, name);
        var info = S(21, "You can cure this.");
        if (Sim.Diseases.DiseaseCatalog.Find(offer.DiseaseId) is { } disease && disease.TreatmentRooms.Count > 0)
        {
            var hospital = simulation.Hospitals[host.Player];
            if (disease.TreatmentRooms is ["pharmacy"])
            {
                info = S(20, "You can cure %s with %d%% efficiency.", name, hospital.PeekCase(disease, simulation.Rules.Diseases).CureEffectiveness).Replace("%%", "%", StringComparison.Ordinal);
            }

            var roomType = disease.TreatmentRooms[^1];
            var roomMissing = !simulation.Entities.OfType<Room>().Any(room => room.RoomType == roomType && room.IsActive && RoomVisits.Owner(simulation, room) == host.Player);
            var staff = RoomStaff(roomType);
            var staffAvailable = staff is not { } kind || StaffOfCategory(simulation, host.Player, kind.Kind) > 0;
            var roomName = simulation.Content.FindRoom(roomType)?.Name ?? roomType;
            var staffName = staff is { } needed ? StaffTitle(host, needed.Kind) : string.Empty;
            if (roomMissing)
            {
                info = staffAvailable ? S(23, "You need to build a %s", roomName) + "." : S(22, "You need to build a %s and employ a %s", roomName, staffName) + ".";
            }
            else if (!staffAvailable)
            {
                info = S(24, "You need to employ a %s", staffName) + ".";
            }
        }

        yield return info;
        yield return simulation.Rules.FreeBuild
            ? host.Texts?.Named("fax.emergency.free_build") ?? "If you are successful your reputation will increase, but if you fail your reputation will be seriously dented."
            : S(26, "The bonus is %d for each patient you cure.", offer.Bonus * offer.Victims);
    }

    /// <summary>The epidemic's fax (Epidemic:sendFax): the disease, the fine to declare it, and what covering it up means.</summary>
    internal static IEnumerable<string> EpidemicStartLines(IWindowHost host, Sim.Disasters.Epidemic epidemic)
    {
        string S(int index, string fallback, params object[] arguments) => new Advice(44, index, fallback).Text(host.Texts, arguments);
        yield return S(168, "Your hospital has an epidemic of %s.", DiseaseName(host, epidemic.DiseaseId));
        yield return S(169, "Declaring it costs a fine of %d.", epidemic.DeclareFine);
        yield return S(170, "If you cover it up, you must cure everyone infected before the health inspector arrives.");
        yield return S(171, "If anyone infected is found, you will be fined and your reputation will suffer.");
    }

    /// <summary>
    /// makeNoTreatmentRoomFax's lines: the disease, then what is missing to cure it (the room to build, and the staff to
    /// hire for it; the staff alone; or that it cannot be cured here) and the question.
    /// </summary>
    internal static IEnumerable<string> NoTreatmentRoomLines(IWindowHost host, PatientBehaviour patient)
    {
        string S(int index, string fallback, params object[] arguments) => new Advice(44, index, fallback).Text(host.Texts, arguments);
        yield return S(51, "Disease: %s", DiseaseName(host, patient.DiseaseId));
        yield return " ";
        var missing = S(50, "You cannot cure this disease in your hospital.");
        if (patient.Disease is { } disease && DiseaseRequirements(host.Simulation, host.Player, disease) is { } needs)
        {
            if (needs.Rooms.Count == 1)
            {
                var room = host.Simulation.Content.FindRoom(needs.Rooms[0])?.Name ?? needs.Rooms[0];
                var staff = RoomStaff(needs.Rooms[0]);
                missing = staff is { } kind && needs.Staff.Any(s => s.Kind == kind.Kind)
                    ? S(47, "If you build a %s and employ a %s you can cure it.", room, StaffTitle(host, kind.Kind))
                    : S(48, "You need to build a %s to cure it.", room);
            }
            else if (needs.Rooms.Count == 0 && needs.Staff.Count > 0)
            {
                missing = S(49, "You need to employ a %s to cure it.", StaffTitle(host, needs.Staff[0].Kind));
            }
        }

        yield return missing;
        yield return S(52, "What do you want to do?");
    }

    /// <summary>makeNoDiagnosisRoomFax's lines: the situation, how far the diagnosis got (once the disease is known), the question.</summary>
    internal static IEnumerable<string> NoDiagnosisRoomLines(IWindowHost host, PatientBehaviour patient, bool known)
    {
        string S(int index, string fallback, params object[] arguments) => new Advice(44, index, fallback).Text(host.Texts, arguments);
        yield return S(216, "This patient has been through every diagnosis room you have, without a diagnosis.");
        yield return " ";
        if (known)
        {
            yield return S(218, "The diagnosis is %d%% sure the patient has %s.", (int)Math.Round(Math.Min(1000, patient.DiagnosisProgress) / 10m), DiseaseName(host, patient.DiseaseId)).Replace("%%", "%", StringComparison.Ordinal);
        }

        yield return S(217, "What do you want to do?");
    }

    /// <summary>
    /// Hospital:checkDiseaseRequirements: the disease's treatment rooms the hospital has none of, and the staff its rooms
    /// need beyond those employed (the most any room needs of a kind), or null when nothing is missing.
    /// </summary>
    internal static (List<string> Rooms, List<(string Kind, int Count)> Staff)? DiseaseRequirements(Simulation simulation, int player, Sim.Diseases.DiseaseDefinition disease)
    {
        var rooms = new List<string>();
        var staff = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var roomType in disease.TreatmentRooms)
        {
            if (!simulation.Entities.OfType<Room>().Any(room => room.RoomType == roomType && room.IsActive && RoomVisits.Owner(simulation, room) == player))
            {
                rooms.Add(roomType);
            }

            if (RoomStaff(roomType) is { } needed)
            {
                var available = StaffOfCategory(simulation, player, needed.Kind);
                if (available < needed.Count)
                {
                    staff[needed.Kind] = Math.Max(staff.GetValueOrDefault(needed.Kind), needed.Count - available);
                }
            }
        }

        return rooms.Count == 0 && staff.Count == 0 ? null : (rooms, staff.Select(pair => (pair.Key, pair.Value)).ToList());
    }

    /// <summary>A room type's required_staff: the kind (Doctor, Nurse, Surgeon, Psychiatrist, Researcher) and how many.</summary>
    private static (string Kind, int Count)? RoomStaff(string roomType) => roomType switch
    {
        "operating_theatre" => ("Surgeon", 2),
        "psych" => ("Psychiatrist", 1),
        "research" or "dna_fixer" => ("Researcher", 1),
        _ => RoomVisits.RequiredStaff(roomType) switch
        {
            BehaviourKind.Doctor => ("Doctor", 1),
            BehaviourKind.Nurse => ("Nurse", 1),
            _ => null,
        },
    };

    /// <summary>countStaffOfCategory: doctors, nurses, or doctors with the ability.</summary>
    private static int StaffOfCategory(Simulation simulation, int player, string kind) =>
        StaffLabels.Employed(simulation, player).Count(h => kind switch
        {
            "Nurse" => h.Profile!.Class == Sim.Staff.StaffClass.Nurse,
            "Doctor" => h.Profile!.Class == Sim.Staff.StaffClass.Doctor,
            "Surgeon" => h.Profile!.IsSurgeon,
            "Psychiatrist" => h.Profile!.IsPsychiatrist,
            "Researcher" => h.Profile!.IsResearcher,
            _ => false,
        });

    /// <summary>StaffProfile.translateStaffClass: staff_title (S[34]).</summary>
    private static string StaffTitle(IWindowHost host, string kind) => kind switch
    {
        "Nurse" => host.Texts?.String(34, 3) ?? "Nurse",
        "Surgeon" => host.Texts?.String(34, 6) ?? "Surgeon",
        "Psychiatrist" => host.Texts?.String(34, 7) ?? "Psychiatrist",
        "Researcher" => host.Texts?.String(34, 9) ?? "Researcher",
        _ => host.Texts?.String(34, 5) ?? "Doctor",
    };
    /// <summary>
    /// adviseDiscoverDisease's fax: the staff have discovered the disease, then its cause, symptoms and cure.
    /// </summary>
    internal static IEnumerable<string> DiseaseReportLines(IWindowHost host, DiseaseReport report)
    {
        yield return new Advice(44, 61, "Your staff have discovered a new disease. It is %s.").Text(host.Texts, DiseaseName(host, report.DiseaseId));
        if (host.Texts?.Disease(report.DiseaseId) is { } text)
        {
            foreach (var line in new[] { text.Cause, text.Symptoms, text.Cure })
            {
                if (!string.IsNullOrEmpty(line))
                {
                    yield return line;
                }
            }
        }
    }

    /// <summary>
    /// Epidemic:determineFaxAndFines's result fax: it worked and the compensation, or it failed and the fine, the fine with
    /// reputation lost, or the evacuation.
    /// </summary>
    internal static IEnumerable<string> EpidemicReportLines(IWindowHost host, EpidemicReport report)
    {
        var name = DiseaseName(host, report.DiseaseId);
        if (report.Outcome == EpidemicOutcome.CoveredUp)
        {
            yield return new Advice(44, 177, "The epidemic of %s has been covered up.").Text(host.Texts, name);
            yield return new Advice(44, 178, "The health inspector found nothing to report.").Text(host.Texts);
            yield return new Advice(44, 179, "You have been paid compensation of %d.").Text(host.Texts, report.Amount);
            yield break;
        }

        yield return new Advice(44, 175, "The epidemic of %s was not contained.").Text(host.Texts, name);
        yield return new Advice(44, 176, "The health inspector has reported your hospital.").Text(host.Texts);
        yield return report.Outcome switch
        {
            EpidemicOutcome.Fined when ReputationLost(host, report) => new Advice(44, 181, "You have been fined %d and your reputation has suffered.").Text(host.Texts, report.Amount),
            EpidemicOutcome.Evacuated => new Advice(44, 182, "Your hospital is being evacuated.").Text(host.Texts),
            _ => new Advice(44, 180, "You have been fined %d.").Text(host.Texts, report.Amount),
        };
    }

    /// <summary>
    /// Whether <c>reputation_loss_minimum</c> or more were still infected, as the fine (a fixed sum each,
    /// <c>calculateInfectedFine</c>) shows.
    /// </summary>
    private static bool ReputationLost(IWindowHost host, EpidemicReport report) =>
        host.Simulation.Rules.Epidemics is { Fine: > 0 } rules && report.Amount / rules.Fine >= rules.ReputationLossMinimum;
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
        <= 3 => UiText.Get("corsixnet.fax.remark_superb", "A superb hospital. I was most impressed."),
        <= 7 => UiText.Get("corsixnet.fax.remark_good", "A good hospital, well run."),
        <= 10 => UiText.Get("corsixnet.fax.remark_average", "An average hospital. It could do better."),
        _ => UiText.Get("corsixnet.fax.remark_poor", "A poor hospital. Things must improve."),
    };

    private WindowCell Key(string digit) => WindowCell.Button(digit, () => _code += digit, _code.Length < 10).WithTooltip(UiText.Format("corsixnet.fax.type_digit", "Type %1%", digit));

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
                host.Adviser.Say(UiText.Get("adviser.cheats.th_cheat", "Congratulations, you have unlocked cheats!"), Environment.TickCount64);
                host.Open(new CheatsWindow());
                host.UnlockCheatsMenu();
                break;
            case FaxCode.Hello:
                break;
            case FaxCode.Toggle when FaxCodes.ToggleCheat(code) is { } toggle:
                var wasOn = host.Simulation.Hospitals[host.Player].ActiveCheats.HasFlag(toggle.Flag);
                host.Submit(new CheatCommand(toggle.Cheat));
                var cheatName = toggle.Cheat switch
                {
                    CheatKind.ToggleSpawnRate => "roujin",
                    CheatKind.ToggleNoRest => "norest",
                    CheatKind.ToggleQueueJump => "queuejump",
                    _ => "superdoctor",
                };
                host.Adviser.Say(wasOn ? UiText.Get($"adviser.cheats.{cheatName}_off_cheat", toggle.Off) : UiText.Get($"adviser.cheats.{cheatName}_on_cheat", toggle.On), Environment.TickCount64);
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
