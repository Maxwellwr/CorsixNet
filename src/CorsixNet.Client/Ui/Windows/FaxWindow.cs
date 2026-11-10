using System.Globalization;
using CorsixNet.Client.Maps;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
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
    public override string Title => "Faxes";

    public override string? Hotkey => "C";

    /// <summary>The faxes waiting, as keys to tell new ones apart: what they are about and for whom.</summary>
    public static IEnumerable<string> Pending(Simulation simulation, int player)
    {
        var hospital = simulation.Hospitals[player];
        if (hospital.Vips.IsInvitationPending)
        {
            yield return $"vip:{hospital.Vips.InvitationExpires}";
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
                WindowCell.Button("Invite", () => host.Submit(new AnswerVipCommand(true))),
                WindowCell.Button("Refuse", () => host.Submit(new AnswerVipCommand(false))));
        }

        if (hospital.Emergencies.Current is { IsAccepted: false } offer)
        {
            any = true;
            yield return WindowRow.Of(
                string.Create(CultureInfo.InvariantCulture, $"Emergency: {offer.Victims} patients with {DiseaseName(host, offer.DiseaseId)}, cure {offer.Percentage} % for {MoneyText.Format(offer.Bonus)} each."),
                WindowCell.Button("Accept", () => host.Submit(new AnswerEmergencyCommand(true))),
                WindowCell.Button("Refuse", () => host.Submit(new AnswerEmergencyCommand(false))));
        }

        if (hospital.Epidemics.Current is { FaxExpires: > 0 } epidemic)
        {
            any = true;
            yield return WindowRow.Of(
                $"Epidemic of {DiseaseName(host, epidemic.DiseaseId)}: declare it and pay {MoneyText.Format(epidemic.DeclareFine)}, or try to cover it up.",
                WindowCell.Button("Declare", () => host.Submit(new AnswerEpidemicCommand(false))),
                WindowCell.Button("Cover up", () => host.Submit(new AnswerEpidemicCommand(true))));
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
                WindowCell.Button("Wait", () => host.Submit(new AnswerPatientCommand(humanoid.Id, PatientAnswer.Wait))),
                WindowCell.Button("Send home", () => host.Submit(new AnswerPatientCommand(humanoid.Id, PatientAnswer.SendHome))),
            };
            if (noDiagnosis)
            {
                cells.Add(WindowCell.Button("Guess cure", () => host.Submit(new AnswerPatientCommand(humanoid.Id, PatientAnswer.GuessCure))));
            }
            else if (PatientBehaviour.CanGoToResearch(simulation, humanoid))
            {
                cells.Add(WindowCell.Button("Research", () => host.Submit(new AnswerPatientCommand(humanoid.Id, PatientAnswer.Research))));
            }

            cells.Add(WindowCell.Button("Patient", () => host.Open(new PatientWindow(humanoid.Id))));
            yield return new WindowRow(cells);
        }

        if (!any)
        {
            yield return WindowCell.Line("No faxes waiting.", dim: true);
        }
    }

    private static IEnumerable<Humanoid> Asking(Simulation simulation, int player) =>
        simulation.Entities.OfType<Humanoid>().Where(humanoid => humanoid is { Behaviour: PatientBehaviour { Question: not PatientQuestion.None }, IsDespawning: false } && humanoid.HospitalPlayer == player);

    private static string DiseaseName(IWindowHost host, string diseaseId) => host.Texts?.DiseaseName(diseaseId) ?? diseaseId;
}
