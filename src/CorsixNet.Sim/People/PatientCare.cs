// Ported from CorsixTH CorsixTH/Lua/entities/humanoids/patient.lua (Patient:setDisease, completeDiagnosticStep,
// modifyDiagnosisProgress, getTreatmentDiseaseId, agreesToPay, treatDisease, isTreatmentEffective), CorsixTH/Lua/room.lua
// (Room:dealtWithPatient, getStaffServiceQuality), CorsixTH/Lua/rooms/gp.lua (GPRoom:dealtWithPatient,
// sendPatientToNextDiagnosisRoom), CorsixTH/Lua/hospital.lua (Hospital:receiveMoneyForTreatment, paySupplierForDrug,
// updateCuredCounts, humanoidDeath, the default policies), CorsixTH/Lua/entities/humanoids/staff.lua
// (Staff:getServiceQuality) and CorsixTH/Lua/world.lua (World:getNewPatientDisease).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Disasters;
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
/// so that the simulation stays free of floating point.
/// </remarks>
public static class PatientCare
{
    /// <summary>Diagnosis progress in thousandths that stands for a complete diagnosis.</summary>
    public const int FullDiagnosis = 1000;

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
    /// <c>Room:dealtWithPatient</c> (<c>GPRoom:dealtWithPatient</c> in the GP's office): what the patient pays and does
    /// next. After a diagnosis room an undiagnosed patient gets further in the diagnosis, pays and goes back to the GP;
    /// a diagnosed one goes on to the next room of the treatment, or after the last is treated.
    /// </summary>
    internal static void DealtWith(Simulation simulation, Room room, Humanoid humanoid, PatientBehaviour patient)
    {
        if (string.Equals(room.RoomType, PatientBehaviour.FirstRoom, StringComparison.Ordinal))
        {
            GpDealtWith(simulation, room, humanoid, patient);
            return;
        }

        if (patient.Disease is not { } disease)
        {
            patient.SetNextGoHome();
            return;
        }

        if (!patient.IsDiagnosed)
        {
            CompleteDiagnosticStep(simulation, room, humanoid, patient, disease);
            ReceiveMoneyForTreatment(simulation, humanoid, patient, room.RoomType);
            if (DiseaseCatalog.Find(DiseaseCatalog.GpDiagnosis) is { } consultation && !AgreesToPay(simulation, humanoid, patient, consultation))
            {
                NotCured(simulation, humanoid, patient, ReputationChange.OverPriced);
                patient.SetNextGoHome();
            }
            else
            {
                patient.SetNextGp();
            }

            return;
        }

        patient.CureRoomsVisited++;
        if (patient.CureRoomsVisited < disease.TreatmentRooms.Count)
        {
            patient.SetNextTreatment();
        }
        else
        {
            TreatDisease(simulation, room, humanoid, patient, disease);
        }
    }

    /// <summary>
    /// <c>Patient:treatDisease</c>: the patient pays for the cure; it works with a chance from the drug's effectiveness,
    /// how sure the diagnosis is and how good the staff is, and then the patient goes home cured, or else dies. The drug
    /// is paid for either way.
    /// </summary>
    internal static void TreatDisease(Simulation simulation, Room room, Humanoid humanoid, PatientBehaviour patient, DiseaseDefinition disease)
    {
        var hospital = simulation.Hospitals[humanoid.HospitalPlayer];
        var entry = hospital.CaseOf(disease, simulation.Rules.Diseases);
        ReceiveMoneyForTreatment(simulation, humanoid, patient, room.RoomType);
        if (IsTreatmentEffective(simulation, room, patient, entry))
        {
            // Patient:cure and goHome("cured") with Hospital:updateCuredCounts.
            hospital.ChangeReputation(simulation.Random, ReputationChange.Cured, entry);
            hospital.Cured++;
            hospital.CuredThisYear++;
            entry.Recoveries++;
            patient.Cure();
            patient.AddToTreatmentHistory(new TreatmentHistoryEntry(TreatmentHistoryKind.Cured, string.Empty));
            patient.SetNextGoHome();
        }
        else
        {
            // Patient:die with Hospital:humanoidDeath.
            RecordDeath(simulation, hospital, entry);
            patient.SetNextDie();
        }

        hospital.PayForDrug(simulation.Hours, entry, simulation.Random.NextInclusive(1, 5));
        if (patient.EmergencyIndex > 0)
        {
            Emergencies.PatientDone(simulation, hospital, patient.IsCured);
        }
    }

    /// <summary>
    /// <c>Patient:isTreatmentEffective</c>: the chance in percent is the cure's effectiveness times the diagnosis progress,
    /// moved by up to a tenth of what is left to 100 (at least 20) by the staff's service quality, which is
    /// <c>0.7 × skill + 0.2 × (1 − fatigue) + 0.1 × happiness</c>.
    /// </summary>
    private static bool IsTreatmentEffective(Simulation simulation, Room room, PatientBehaviour patient, DiseaseCase entry)
    {
        var cureChance = entry.CureEffectiveness * (patient.DiagnosisProgress / 1000m);
        var quality = 0.5m;
        if (simulation.FindEntity(room.StaffId) is Humanoid { Profile: { } profile } staff)
        {
            quality = (0.7m * profile.Skill / StaffProfile.MaxSkill) + (0.2m * (1 - staff.Fatigue)) + (0.1m * staff.Happiness);
        }

        const int minImpact = 20;
        const decimal scale = 0.2m;
        var serviceBase = Math.Max(100 - cureChance, minImpact);
        cureChance += serviceBase * (quality - 0.5m) * scale;
        return cureChance >= simulation.Random.NextInclusive(1, 100);
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
        CompleteDiagnosticStep(simulation, room, humanoid, patient, disease);
        if (patient.DiagnosisProgress >= Policies(simulation, humanoid).StopProcedure || (patient.DiagnosisProgress >= FullDiagnosis && patient.AvailableDiagnosisRooms.Count == 0))
        {
            SetDiagnosed(simulation, humanoid, patient, disease);
            if (AgreesToPay(simulation, humanoid, patient, disease))
            {
                patient.SetNextTreatment();
            }
            else
            {
                NotCured(simulation, humanoid, patient, ReputationChange.OverPriced);
                patient.SetNextGoHome();
            }

            return;
        }

        // GPRoom:sendPatientToNextDiagnosisRoom.
        var rooms = patient.AvailableDiagnosisRooms;
        if (rooms.Count == 0)
        {
            NotCured(simulation, humanoid, patient, ReputationChange.Kicked);
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
            NotCured(simulation, humanoid, patient, ReputationChange.OverPriced);
            patient.SetNextGoHome();
        }
    }

    /// <summary>
    /// <c>Hospital:updateNotCuredCounts</c> from <c>Patient:goHome</c>: the patient goes home without a cure, sent home or
    /// because of the price, which changes the reputation for their disease.
    /// </summary>
    /// <summary><c>Hospital:humanoidDeath</c>: the death counts, the disease's fatalities and the reputation.</summary>
    internal static void RecordDeath(Simulation simulation, Hospital hospital, DiseaseCase? entry)
    {
        if (entry is not null)
        {
            entry.Fatalities++;
        }

        hospital.Deaths++;
        hospital.DeathsThisYear++;
        hospital.ChangeReputation(simulation.Random, ReputationChange.Death, entry);
    }

    /// <summary>The policies of the patient's hospital.</summary>
    internal static HospitalPolicies Policies(Simulation simulation, Humanoid humanoid) => simulation.Hospitals[humanoid.HospitalPlayer].Policies;

    internal static void NotCured(Simulation simulation, Humanoid humanoid, PatientBehaviour patient, ReputationChange reason)
    {
        if (patient.Disease is not { } disease)
        {
            return;
        }

        var hospital = simulation.Hospitals[humanoid.HospitalPlayer];
        hospital.UpdateNotCuredCounts(simulation.Random, reason, hospital.CaseOf(disease, simulation.Rules.Diseases));

        // Though not killed, this lets the emergency's watch end early.
        if (patient.EmergencyIndex > 0)
        {
            Emergencies.PatientDone(simulation, hospital, cured: false);
        }
    }

    /// <summary>Marks the patient diagnosed; the hospital now knows the disease (<c>Patient:setDiagnosed</c>, <c>discoverDisease</c>).</summary>
    internal static void SetDiagnosed(Simulation simulation, Humanoid humanoid, PatientBehaviour patient, DiseaseDefinition disease)
    {
        patient.IsDiagnosed = true;
        patient.AddToTreatmentHistory(new TreatmentHistoryEntry(TreatmentHistoryKind.Diagnosis, disease.Id));
        var hospital = simulation.Hospitals[humanoid.HospitalPlayer];
        var entry = hospital.CaseOf(disease, simulation.Rules.Diseases);
        if (!entry.Discovered)
        {
            entry.Discovered = true;
            hospital.Research.OnDiseaseDiscovered(simulation, hospital, disease);
        }
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
        // Nothing at all in free build mode, not even the patient's view of the price.
        if (simulation.Rules.FreeBuild)
        {
            return;
        }

        var id = patient.IsDiagnosed ? patient.DiseaseId : DiseaseCatalog.DiagnosisPrefix + roomType;
        if (DiseaseCatalog.Find(id) is not { } paidFor)
        {
            return;
        }

        var hospital = simulation.Hospitals[humanoid.HospitalPlayer];
        var rules = simulation.Rules.Diseases;
        var amount = patient.PayAmount > 0 ? patient.PayAmount : hospital.TreatmentPrice(paidFor, rules);
        var entry = hospital.CaseOf(paidFor, rules);
        if (patient.InsuranceCompany == 0)
        {
            // A patient paying themselves may find the price too high or low.
            hospital.ComputePriceLevelImpact(simulation, humanoid, entry);
        }

        hospital.ReceiveForTreatment(simulation.Hours, entry, amount, patient.InsuranceCompany);
        simulation.ShowFloatingDollarSign(humanoid, amount, patient.InsuranceCompany != 0);
        patient.PayAmount = 0;
    }

    /// <summary>
    /// <c>Patient:completeDiagnosticStep</c>: a base that the harder the disease is to diagnose the smaller it is,
    /// plus a bonus from the doctor's skill and attention to detail, both divided by a random 1 to 3; very skilled
    /// doctors may get up to five times the bonus.
    /// </summary>
    private static void CompleteDiagnosticStep(Simulation simulation, Room room, Humanoid humanoid, PatientBehaviour patient, DiseaseDefinition disease)
    {
        var difficulty = simulation.Rules.Diseases.MaxDiagDiff(disease.ExpertiseId) / 1000m;
        var diagnosisBase = 0.4m * (1 - difficulty);
        var bonus = 0.4m;
        var multiplier = 1m;

        if (room.StaffPresent && simulation.FindEntity(room.StaffId) is Humanoid { Profile: { } profile } staff)
        {
            // multiplier = (skilled ? random(1, 5) : 1) * (1 - (fatigue - 0.5)).
            var fatigueFactor = 1 - (staff.Fatigue - 0.5m);
            multiplier = profile.Skill >= 900 ? simulation.Random.NextInclusive(1, 5) * fatigueFactor : fatigueFactor;
            var divisor = simulation.Random.NextInclusive(1, 3);
            var attention = profile.AttentionToDetail / (decimal)StaffProfile.MaxSkill / divisor;
            var skill = profile.Skill / (decimal)StaffProfile.MaxSkill / divisor;
            bonus = (attention + 0.4m) * skill;
        }

        var progress = (patient.DiagnosisProgress / 1000m) + diagnosisBase + (bonus * multiplier);
        // Patient:modifyDiagnosisProgress: no further than the stop_procedure policy.
        progress = Math.Clamp(progress, 0, Policies(simulation, humanoid).StopProcedure / 1000m);
        patient.DiagnosisProgress = (int)decimal.Floor(progress * 1000);
    }
}
