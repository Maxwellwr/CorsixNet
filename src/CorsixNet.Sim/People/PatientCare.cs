// Ported from CorsixTH CorsixTH/Lua/entities/humanoids/patient.lua (Patient:setDisease, completeDiagnosticStep,
// modifyDiagnosisProgress, getTreatmentDiseaseId, agreesToPay), CorsixTH/Lua/rooms/gp.lua (GPRoom:dealtWithPatient,
// sendPatientToNextDiagnosisRoom), CorsixTH/Lua/hospital.lua (Hospital:receiveMoneyForTreatment, the default
// policies) and CorsixTH/Lua/world.lua (World:getNewPatientDisease).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Sim.People;

/// <summary>
/// Diseases, diagnosis and payment: which disease a new patient comes with, how far the GP gets in
/// diagnosing it, where the patient goes next, and what they pay.
/// </summary>
/// <remarks>
/// Diagnosis progress is kept in thousandths (1000 stands for CorsixTH's 1.0) and worked out with decimals,
/// so that the simulation stays free of floating point. Fatigue is not modelled yet, so it is always 0, as
/// for a rested member of staff in CorsixTH.
/// </remarks>
public static class PatientCare
{
    /// <summary>Diagnosis progress in thousandths that stands for a complete diagnosis.</summary>
    public const int FullDiagnosis = 1000;

    /// <summary>
    /// Policy: a patient whose diagnosis is below this when no diagnosis room is left goes home (CorsixTH's default
    /// <c>send_home</c>, 0.1).
    /// </summary>
    public const int SendHome = 100;

    /// <summary>Policy: at least this far diagnosed, a known disease is guessed (default <c>guess_cure</c>, 0.9).</summary>
    public const int GuessCure = 900;

    /// <summary>Policy: diagnosis stops at this (default <c>stop_procedure</c>, 1.0).</summary>
    public const int StopProcedure = 1000;

    /// <summary>Days a patient waits for a missing room or the player's decision before going home.</summary>
    public const int WaitingDays = 60;

    /// <summary>
    /// <c>World:getNewPatientDisease</c>: one of the level's diseases at random; if it may not come yet, one at
    /// random of those that may. Null when none may.
    /// </summary>
    public static DiseaseDefinition? NewPatientDisease(Simulation simulation, Hospital hospital)
    {
        var rules = simulation.Rules.Diseases;
        var available = rules.AvailableDiseases().ToList();
        if (available.Count == 0)
        {
            return null;
        }

        var month = simulation.Date.MonthOfGame;
        var disease = available[simulation.Random.Next(available.Count)];
        if (rules.IsUsableForNewPatient(disease, month, hospital.Visitors))
        {
            return disease;
        }

        var usable = available.Where(d => rules.IsUsableForNewPatient(d, month, hospital.Visitors)).ToList();
        return usable.Count == 0 ? null : usable[simulation.Random.Next(usable.Count)];
    }

    /// <summary>The second half of <c>Patient:setDisease</c>: a quarter of patients pay through one of three insurance companies.</summary>
    internal static int ChooseInsuranceCompany(SimRandom random)
    {
        return random.NextInclusive(1, 4) == 1 ? random.NextInclusive(1, Hospital.InsuranceCompanies) : 0;
    }

    /// <summary>
    /// <c>Patient:agreesToPay</c>: at or below the standard price a patient always agrees; above it, with chance
    /// e^(−4 × overprice). If they agree, they will pay the price as it is now.
    /// </summary>
    internal static bool AgreesToPay(Simulation simulation, Humanoid humanoid, PatientBehaviour patient, DiseaseDefinition disease)
    {
        var hospital = simulation.Hospitals[humanoid.HospitalPlayer];
        var rules = simulation.Rules.Diseases;
        var percent = hospital.CaseOf(disease, rules).PricePercent;
        var agrees = true;
        if (percent > DiseaseCase.StandardPrice)
        {
            var overprice = (percent - DiseaseCase.StandardPrice) / (decimal)DiseaseCase.StandardPrice;
            var chance = 1m / PatientArrivals.Exp(4 * overprice);
            agrees = simulation.Random.Next(1_000_000) / 1_000_000m <= chance;
        }

        if (agrees)
        {
            patient.PayAmount = hospital.TreatmentPrice(disease, rules);
        }

        return agrees;
    }

    /// <summary>
    /// <c>GPRoom:dealtWithPatient</c>: the GP is done. The patient pays for the consultation and gets further in
    /// the diagnosis; once diagnosed they go on to treatment, otherwise to another diagnosis room. What they do
    /// after leaving the room is decided here, while they are still in it.
    /// </summary>
    internal static void GpDealtWith(Simulation simulation, Room room, Humanoid humanoid, PatientBehaviour patient)
    {
        if (patient.Disease is not { } disease || patient.IsDiagnosed)
        {
            // CorsixTH lets such a patient meander about; they only come in debugging, so here they go home.
            patient.SetNextGoHome();
            return;
        }

        ReceiveMoneyForTreatment(simulation, humanoid, patient, room.RoomType);
        CompleteDiagnosticStep(simulation, room, patient, disease);
        if (patient.DiagnosisProgress >= StopProcedure || (patient.DiagnosisProgress >= FullDiagnosis && patient.AvailableDiagnosisRooms.Count == 0))
        {
            SetDiagnosed(simulation, humanoid, patient, disease);
            if (AgreesToPay(simulation, humanoid, patient, disease))
            {
                patient.SetNextTreatment();
            }
            else
            {
                patient.SetNextGoHome();
            }

            return;
        }

        // GPRoom:sendPatientToNextDiagnosisRoom.
        var rooms = patient.AvailableDiagnosisRooms;
        if (rooms.Count == 0)
        {
            patient.SetNextGoHome();
            return;
        }

        var index = simulation.Random.Next(rooms.Count);
        var next = DiseaseCatalog.Find(DiseaseCatalog.DiagnosisPrefix + rooms[index]);
        if (next is null || AgreesToPay(simulation, humanoid, patient, next))
        {
            patient.SetNextDiagnosis(index);
        }
        else
        {
            patient.SetNextGoHome();
        }
    }

    /// <summary>Marks the patient diagnosed; the hospital now knows the disease (<c>Patient:setDiagnosed</c>, <c>discoverDisease</c>).</summary>
    internal static void SetDiagnosed(Simulation simulation, Humanoid humanoid, PatientBehaviour patient, DiseaseDefinition disease)
    {
        patient.IsDiagnosed = true;
        simulation.Hospitals[humanoid.HospitalPlayer].CaseOf(disease, simulation.Rules.Diseases).Discovered = true;
    }

    /// <summary>Whether the hospital knows the patient's disease, so that it may be guessed.</summary>
    internal static bool IsDiscovered(Simulation simulation, Humanoid humanoid, DiseaseDefinition disease)
    {
        return simulation.Hospitals[humanoid.HospitalPlayer].CaseOf(disease, simulation.Rules.Diseases).Discovered;
    }

    /// <summary>
    /// <c>Hospital:receiveMoneyForTreatment</c>: the patient pays what they agreed to for the disease, or before the
    /// diagnosis for the room they are in, or else its current price.
    /// </summary>
    private static void ReceiveMoneyForTreatment(Simulation simulation, Humanoid humanoid, PatientBehaviour patient, string roomType)
    {
        var id = patient.IsDiagnosed ? patient.DiseaseId : DiseaseCatalog.DiagnosisPrefix + roomType;
        if (DiseaseCatalog.Find(id) is not { } paidFor)
        {
            return;
        }

        var hospital = simulation.Hospitals[humanoid.HospitalPlayer];
        var rules = simulation.Rules.Diseases;
        var amount = patient.PayAmount > 0 ? patient.PayAmount : hospital.TreatmentPrice(paidFor, rules);
        hospital.ReceiveForTreatment(simulation.Tick, hospital.CaseOf(paidFor, rules), amount, patient.InsuranceCompany);
        patient.PayAmount = 0;
    }

    /// <summary>
    /// <c>Patient:completeDiagnosticStep</c>: a base that the harder the disease is to diagnose the smaller it is,
    /// plus a bonus from the doctor's skill and attention to detail, both divided by a random 1 to 3; very skilled
    /// doctors may get up to five times the bonus.
    /// </summary>
    private static void CompleteDiagnosticStep(Simulation simulation, Room room, PatientBehaviour patient, DiseaseDefinition disease)
    {
        var difficulty = simulation.Rules.Diseases.MaxDiagDiff(disease.ExpertiseId) / 1000m;
        var diagnosisBase = 0.4m * (1 - difficulty);
        var bonus = 0.4m;
        var multiplier = 1m;

        if (room.StaffPresent && simulation.FindEntity(room.StaffId) is Humanoid { Profile: { } profile })
        {
            // multiplier = (skilled ? random(1, 5) : 1) * (1 - (fatigue - 0.5)), with fatigue 0.
            const decimal fatigueFactor = 1.5m;
            multiplier = profile.Skill >= 900 ? simulation.Random.NextInclusive(1, 5) * fatigueFactor : fatigueFactor;
            var divisor = simulation.Random.NextInclusive(1, 3);
            var attention = profile.AttentionToDetail / (decimal)StaffProfile.MaxSkill / divisor;
            var skill = profile.Skill / (decimal)StaffProfile.MaxSkill / divisor;
            bonus = (attention + 0.4m) * skill;
        }

        var progress = (patient.DiagnosisProgress / 1000m) + diagnosisBase + (bonus * multiplier);
        progress = Math.Clamp(progress, 0, StopProcedure / 1000m);
        patient.DiagnosisProgress = (int)decimal.Floor(progress * 1000);
    }
}
