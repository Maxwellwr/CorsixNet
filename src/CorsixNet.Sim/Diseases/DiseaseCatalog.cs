// Disease data ported from CorsixTH CorsixTH/Lua/diseases/*.lua and CorsixTH/Lua/diagnosis/*.lua, in the
// order of their file names.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

namespace CorsixNet.Sim.Diseases;

/// <summary>Every disease and diagnosis pseudo disease of the game.</summary>
public static class DiseaseCatalog
{
    /// <summary>The pseudo disease a patient pays for at the GP's office.</summary>
    public const string GpDiagnosis = "diag_gp";

    /// <summary>Prefix of a diagnosis room's pseudo disease, such as <c>diag_x_ray</c>.</summary>
    public const string DiagnosisPrefix = "diag_";

    private static readonly Dictionary<string, DiseaseDefinition> Lookup;

    static DiseaseCatalog()
    {
        Lookup = All.ToDictionary(disease => disease.Id, StringComparer.Ordinal);
    }

    /// <summary>The real diseases first, then the pseudo diseases, each in file name order.</summary>
    public static IReadOnlyList<DiseaseDefinition> All { get; } =
    [
        new("alien_dna", 8, 2000)
        {
            EmergencyNumber = 16,
            VisualsId = 6,
            Look = PatientLook.Alien,
            OnlyEmergency = true,
            TreatmentRooms = ["dna_fixer"],
        },
        new("baldness", 10, 950)
        {
            Look = PatientLook.Baldness,
            EmergencyNumber = 18,
            VisualsId = 8,
            DiagnosisRooms = ["x_ray", "blood_machine", "scanner"],
            TreatmentRooms = ["hair_restoration"],
        },
        new("bloaty_head", 2, 850)
        {
            EmergencyNumber = 18,
            VisualsId = 0,
            Look = PatientLook.BloatyHead,
            DiagnosisRooms = ["general_diag", "x_ray", "cardiogram", "scanner"],
            TreatmentRooms = ["inflation"],
        },
        new("broken_heart", 20, 1900)
        {
            EmergencyNumber = 6,
            NonVisualsId = 4,
            IsContagious = true,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["ward", "operating_theatre"],
        },
        new("broken_wind", 17, 1300)
        {
            EmergencyNumber = 14,
            NonVisualsId = 1,
            IsContagious = true,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["pharmacy"],
        },
        new("chronic_nosehair", 25, 800)
        {
            EmergencyNumber = 18,
            NonVisualsId = 9,
            IsContagious = true,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["pharmacy"],
        },
        new("corrugated_ankles", 24, 800)
        {
            EmergencyNumber = 18,
            NonVisualsId = 8,
            IsContagious = true,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["pharmacy"],
        },
        new("discrete_itching", 11, 700)
        {
            EmergencyNumber = 15,
            VisualsId = 9,
            IsContagious = true,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["pharmacy"],
        },
        new("fake_blood", 27, 800)
        {
            EmergencyNumber = 18,
            NonVisualsId = 11,
            IsContagious = true,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["psych"],
        },
        new("fractured_bones", 9, 450)
        {
            Look = PatientLook.FracturedBones,
            EmergencyNumber = 16,
            VisualsId = 7,
            DiagnosisRooms = ["scanner"],
            TreatmentRooms = ["fracture_clinic"],
        },
        new("gastric_ejections", 28, 650)
        {
            EmergencyNumber = 15,
            NonVisualsId = 12,
            IsContagious = true,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["pharmacy"],
        },
        new("golf_stones", 34, 1600)
        {
            EmergencyNumber = 6,
            NonVisualsId = 18,
            IsContagious = true,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["ward", "operating_theatre"],
        },
        new("gut_rot", 33, 350)
        {
            EmergencyNumber = 14,
            NonVisualsId = 17,
            MoreLooUse = true,
            IsContagious = true,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["pharmacy"],
        },
        new("hairyitis", 3, 1150)
        {
            Look = PatientLook.Hairyitis,
            EmergencyNumber = 12,
            VisualsId = 1,
            DiagnosisRooms = ["x_ray", "scanner"],
            TreatmentRooms = ["electrolysis"],
        },
        new("heaped_piles", 32, 400)
        {
            EmergencyNumber = 14,
            NonVisualsId = 16,
            IsContagious = true,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["pharmacy"],
        },
        new("infectious_laughter", 23, 1500)
        {
            EmergencyNumber = 18,
            NonVisualsId = 7,
            IsContagious = true,
            DiagnosisRooms = ["cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["psych"],
        },
        new("invisibility", 5, 1400)
        {
            Look = PatientLook.Invisible,
            EmergencyNumber = 18,
            VisualsId = 3,
            DiagnosisRooms = ["x_ray", "scanner"],
            TreatmentRooms = ["pharmacy"],
        },
        new("iron_lungs", 30, 1700)
        {
            EmergencyNumber = 5,
            NonVisualsId = 14,
            IsContagious = true,
            DiagnosisRooms = ["x_ray", "cardiogram", "blood_machine"],
            TreatmentRooms = ["ward", "operating_theatre"],
        },
        new("jellyitis", 12, 1000)
        {
            EmergencyNumber = 12,
            VisualsId = 10,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["jelly_vat"],
        },
        new("kidney_beans", 19, 1050)
        {
            EmergencyNumber = 5,
            NonVisualsId = 3,
            IsContagious = true,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["ward", "operating_theatre"],
        },
        new("king_complex", 4, 1600)
        {
            EmergencyNumber = 18,
            VisualsId = 2,
            DiagnosisRooms = ["x_ray", "scanner"],
            TreatmentRooms = ["psych"],
        },
        new("pregnant", 14, 200)
        {
            Look = PatientLook.Pregnant,
            EmergencyNumber = 8,
            VisualsId = 12,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["ward", "operating_theatre"],
        },
        new("ruptured_nodules", 21, 1600)
        {
            EmergencyNumber = 6,
            NonVisualsId = 5,
            IsContagious = true,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["ward", "operating_theatre"],
        },
        new("serious_radiation", 6, 1800)
        {
            EmergencyNumber = 18,
            VisualsId = 4,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["decontamination"],
        },
        new("slack_tongue", 7, 900)
        {
            Look = PatientLook.SlackTongue,
            EmergencyNumber = 18,
            VisualsId = 5,
            DiagnosisRooms = ["x_ray", "scanner"],
            TreatmentRooms = ["slack_tongue"],
        },
        new("sleeping_illness", 13, 750)
        {
            EmergencyNumber = 18,
            VisualsId = 11,
            IsContagious = true,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["pharmacy"],
        },
        new("spare_ribs", 18, 1100)
        {
            EmergencyNumber = 4,
            NonVisualsId = 2,
            IsContagious = true,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["ward", "operating_theatre"],
        },
        new("sweaty_palms", 31, 600)
        {
            EmergencyNumber = 14,
            NonVisualsId = 15,
            IsContagious = true,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["psych"],
        },
        new("the_squits", 29, 400)
        {
            EmergencyNumber = 18,
            NonVisualsId = 13,
            MoreLooUse = true,
            IsContagious = true,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["pharmacy"],
        },
        new("third_degree_sideburns", 26, 550)
        {
            EmergencyNumber = 13,
            NonVisualsId = 10,
            IsContagious = true,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["psych"],
        },
        new("transparency", 15, 800)
        {
            Look = PatientLook.Transparent,
            EmergencyNumber = 12,
            VisualsId = 13,
            DiagnosisRooms = ["scanner"],
            TreatmentRooms = ["pharmacy"],
        },
        new("tv_personalities", 22, 800)
        {
            EmergencyNumber = 14,
            NonVisualsId = 6,
            IsContagious = true,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["psych"],
        },
        new("uncommon_cold", 16, 300)
        {
            EmergencyNumber = 18,
            NonVisualsId = 0,
            IsContagious = true,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["pharmacy"],
        },
        new("unexpected_swelling", 35, 500)
        {
            EmergencyNumber = 5,
            NonVisualsId = 19,
            IsContagious = true,
            DiagnosisRooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
            TreatmentRooms = ["ward", "operating_theatre"],
        },
        new("diag_blood_machine", 37, 250) { IsPseudo = true },
        new("diag_cardiogram", 38, 150) { IsPseudo = true },
        new("diag_general_diag", 41, 150) { IsPseudo = true },
        new("diag_gp", 1, 100) { IsPseudo = true },
        new("diag_psych", 43, 200) { IsPseudo = true },
        new("diag_scanner", 36, 300) { IsPseudo = true },
        new("diag_ultrascan", 40, 250) { IsPseudo = true },
        new("diag_ward", 42, 100) { IsPseudo = true },
        new("diag_x_ray", 39, 200) { IsPseudo = true },
    ];

    /// <summary>The disease with this id, or null.</summary>
    public static DiseaseDefinition? Find(string id) => Lookup.GetValueOrDefault(id);
}
