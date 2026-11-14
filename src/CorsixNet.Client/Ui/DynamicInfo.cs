// The bottom panel's dynamic info for what is under the cursor, after CorsixTH CorsixTH/Lua/entities/humanoids/patient.lua
// (Patient:updateDynamicInfo), staff.lua (Staff:updateDynamicInfo), machine.lua and object.lua (updateDynamicInfo),
// with the texts of languages/original_strings.lua's dynamic_info (S[59]).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Client.Ui.Windows;
using CorsixNet.Content;
using CorsixNet.Sim;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;

namespace CorsixNet.Client.Ui;

/// <summary>
/// The dynamic info as the bottom panel draws it (CorsixTH's <c>dynamic_info</c>): up to three lines, and on the last a
/// progress bar from 0 to 1 with dividers (the tiredness at which staff go for a rest).
/// </summary>
internal sealed record DynamicInfoLines(IReadOnlyList<string> Lines, decimal? Progress = null, IReadOnlyList<decimal>? Dividers = null)
{
    /// <summary>The lines joined into one, the bar left out.</summary>
    public override string ToString() => string.Join("   ", Lines.Where(line => !string.IsNullOrWhiteSpace(line)));
}

/// <summary>
/// What CorsixTH's bottom panel shows about the humanoid or object under the cursor (<c>getDynamicInfo</c>): its lines
/// joined into one, the diagnosis or tiredness bar as a percentage.
/// </summary>
internal static class DynamicInfo
{
    /// <summary>The dynamic info of a humanoid, or null for one CorsixTH shows none for.</summary>
    public static string? Of(Simulation simulation, Humanoid humanoid, OriginalTexts? texts)
    {
        string S(int index, string fallback) => texts?.String(59, index) ?? fallback;

        switch (humanoid.Behaviour)
        {
            case PatientBehaviour patient:
                return Join(PatientAction(simulation, patient, S), Contagious(simulation, humanoid, patient, S), Diagnosis(patient, texts, S));
            case VipBehaviour:
                return S(21, "VIP");
            case InspectorBehaviour:
                return S(22, "Health Inspector");
        }

        if (humanoid.Profile is { } profile)
        {
            var name = texts?.Staff.FullName(profile) ?? UiText.Format("corsixnet.staff.unnamed", "%1% %2%", profile.Class, humanoid.Id);
            return Join(name, StaffLabels.Doing(simulation, humanoid), $"{S(29, "Tiredness")} {StaffLabels.Percent(humanoid.Fatigue)}");
        }

        return null;
    }

    /// <summary>
    /// The dynamic info of a humanoid for the bottom panel: a patient's action, contagion and diagnosis with the bar of
    /// diagnosis made (up to the stop procedure policy); a member of staff's name, what they do and their tiredness with
    /// its bar, divided at the staff room policy (none for a receptionist); null where CorsixTH shows none.
    /// </summary>
    public static DynamicInfoLines? Panel(Simulation simulation, Humanoid humanoid, OriginalTexts? texts)
    {
        string S(int index, string fallback) => texts?.String(59, index) ?? fallback;
        var hospital = humanoid.HospitalPlayer >= 0 && humanoid.HospitalPlayer < simulation.Hospitals.Count ? simulation.Hospitals[humanoid.HospitalPlayer] : null;
        switch (humanoid.Behaviour)
        {
            case PatientBehaviour patient:
                var undiagnosed = !patient.IsDiagnosed && patient.Phase is not (PatientPhase.GoingHome or PatientPhase.Dying);
                var stop = Math.Max(1, hospital?.Policies.StopProcedure ?? HospitalPolicies.DefaultStopProcedure);
                return new DynamicInfoLines(
                    [PatientAction(simulation, patient, S), Contagious(simulation, humanoid, patient, S), undiagnosed ? S(15, "Diagnosis progress") : Diagnosis(patient, texts, S)],
                    undiagnosed ? Math.Min(1m, (decimal)patient.DiagnosisProgress / stop) : null);
            case VipBehaviour:
                return new DynamicInfoLines([S(21, "VIP")]);
            case InspectorBehaviour:
                return new DynamicInfoLines([S(22, "Health Inspector")]);
        }

        if (humanoid.Profile is not { } profile)
        {
            return null;
        }

        var name = texts?.Staff.FullName(profile) ?? UiText.Format("corsixnet.staff.unnamed", "%1% %2%", profile.Class, humanoid.Id);
        var receptionist = profile.Class == Sim.Staff.StaffClass.Receptionist;
        return new DynamicInfoLines(
            [name, StaffLabels.Doing(simulation, humanoid), receptionist ? string.Empty : S(29, "Tiredness")],
            receptionist ? null : Math.Clamp(humanoid.Fatigue, 0m, 1m),
            receptionist || hospital is null ? null : [hospital.Policies.GoToStaffRoom / (decimal)HospitalPolicies.MaxFraction]);
    }

    /// <summary>A machine's dynamic info: its name, strength and times used (<c>Machine:updateDynamicInfo</c>).</summary>
    public static string OfMachine(Simulation simulation, RoomObject machine, OriginalTexts? texts)
    {
        string S(int index, string fallback) => texts?.String(59, index) ?? fallback;
        var name = UiText.ObjectName(simulation.Content, machine.ObjectType);
        var state = machine.Machine!;
        return Join(name, Format(S(31, "Strength %d"), state.Strength), Format(S(32, "Times used %d"), state.TimesUsed));
    }

    private static string PatientAction(Simulation simulation, PatientBehaviour patient, Func<int, string, string> s)
    {
        var room = simulation.FindEntity(patient.TargetId) is Room target ? UiText.RoomName(simulation.Content, target.RoomType) : null;
        return patient.Phase switch
        {
            PatientPhase.Dying => s(5, "Dying"),
            PatientPhase.GoingHome when patient.IsCured => s(9, "Cured"),
            PatientPhase.GoingHome when patient.HomeMood == PatientHomeMood.SadMoney => s(20, "Prices too high"),
            PatientPhase.GoingHome => s(11, "Sent home"),
            PatientPhase.Waiting => s(6, "Awaiting decision"),
            PatientPhase.InDoorQueue when room is not null => Format(s(7, "Queueing for %s"), room),
            PatientPhase.WalkingToRoom when room is not null => Format(s(8, "On my way to %s"), room),
            _ => string.Empty,
        };
    }

    private static string Contagious(Simulation simulation, Humanoid humanoid, PatientBehaviour patient, Func<int, string, string> s) =>
        patient.IsInfected && humanoid.HospitalPlayer >= 0 && humanoid.HospitalPlayer < simulation.Hospitals.Count
        && simulation.Hospitals[humanoid.HospitalPlayer].Epidemics.Current is { IsCoverUp: true }
            ? patient.IsVaccinated ? UiText.Get("dynamic_info.patient.actions.epidemic_vaccinated", "I am no longer contagious") : s(26, "I am contagious")
            : string.Empty;

    private static string Diagnosis(PatientBehaviour patient, OriginalTexts? texts, Func<int, string, string> s)
    {
        if (patient.Phase is PatientPhase.GoingHome or PatientPhase.Dying)
        {
            return string.Empty;
        }

        if (patient.IsDiagnosed && patient.Disease is { } disease)
        {
            var name = texts?.DiseaseName(disease.Id) ?? disease.Id;
            return patient.DiagnosisProgress < 1000 ? Format(s(14, "Guessed diagnosis: %s"), name)
                : patient.EmergencyIndex != 0 ? Format(s(23, "Emergency: %s"), name)
                : Format(s(13, "Diagnosed: %s"), name);
        }

        return $"{s(15, "Diagnosis progress")} {Math.Min(100, patient.DiagnosisProgress / 10)} %";
    }

    private static string Format(string template, object value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        var at = template.IndexOf("%s", StringComparison.Ordinal) is >= 0 and var i ? i : template.IndexOf("%d", StringComparison.Ordinal);
        return at < 0 ? $"{template} {text}" : template[..at] + text + template[(at + 2)..];
    }

    private static string Join(params string?[] lines) => string.Join("   ", lines.Where(line => !string.IsNullOrWhiteSpace(line)));
}
