// Texts from the original's strings, by the mapping of CorsixTH CorsixTH/Lua/languages/original_strings.lua
// (diseases and its D function, vip_names).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Collections.Frozen;
using CorsixNet.Formats;

namespace CorsixNet.Content;

/// <summary>A disease's texts in the casebook and faxes; the cause, symptoms and cure are null for pseudo diseases.</summary>
public sealed record DiseaseText(string Name, string? Cause, string? Symptoms, string? Cure);

/// <summary>
/// The game's texts in one language, from the original's strings (<c>LANG-n.DAT</c>) as CorsixTH maps them.
/// </summary>
public sealed class OriginalTexts
{
    // Sections of the original strings (CorsixTH's S[n] is section n - 1).
    private const int DiseaseNamesSection = 3;
    private const int VipNamesSection = 41;
    private const int FaxesAndDiseasesSection = 43;

    /// <summary>
    /// CorsixTH's <c>diseases</c>: the disease's name in S[4] and, for real diseases, the first of its cause, symptoms
    /// and cure in S[44] (CorsixTH's 1-based indices). Keyed by disease id: CorsixTH's diseases/*.lua and
    /// diagnosis/*.lua use the key of their id, except <c>diag_gp</c> (<c>general_practice</c>) and <c>pregnant</c>
    /// (<c>pregnancy</c>).
    /// </summary>
    private static readonly FrozenDictionary<string, (int Name, int Cause)> Diseases = new Dictionary<string, (int, int)>(StringComparer.Ordinal)
    {
        ["diag_gp"] = (2, 0),
        ["bloaty_head"] = (3, 62),
        ["hairyitis"] = (4, 65),
        ["king_complex"] = (5, 68),
        ["invisibility"] = (6, 71),
        ["serious_radiation"] = (7, 74),
        ["slack_tongue"] = (8, 77),
        ["alien_dna"] = (9, 80),
        ["fractured_bones"] = (10, 83),
        ["baldness"] = (11, 86),
        ["discrete_itching"] = (12, 89),
        ["jellyitis"] = (13, 92),
        ["sleeping_illness"] = (14, 95),
        ["pregnant"] = (15, 98),
        ["transparency"] = (16, 101),
        ["uncommon_cold"] = (17, 104),
        ["broken_wind"] = (18, 107),
        ["spare_ribs"] = (19, 110),
        ["kidney_beans"] = (20, 113),
        ["broken_heart"] = (21, 116),
        ["ruptured_nodules"] = (22, 119),
        ["tv_personalities"] = (23, 122),
        ["infectious_laughter"] = (24, 125),
        ["corrugated_ankles"] = (25, 128),
        ["chronic_nosehair"] = (26, 131),
        ["third_degree_sideburns"] = (27, 134),
        ["fake_blood"] = (28, 137),
        ["gastric_ejections"] = (29, 140),
        ["the_squits"] = (30, 143),
        ["iron_lungs"] = (31, 146),
        ["sweaty_palms"] = (32, 149),
        ["heaped_piles"] = (33, 152),
        ["gut_rot"] = (34, 155),
        ["golf_stones"] = (35, 158),
        ["unexpected_swelling"] = (36, 161),
        ["diag_scanner"] = (37, 0),
        ["diag_blood_machine"] = (38, 0),
        ["diag_cardiogram"] = (39, 0),
        ["diag_x_ray"] = (40, 0),
        ["diag_ultrascan"] = (41, 0),
        ["diag_general_diag"] = (42, 0),
        ["diag_ward"] = (43, 0),
        ["diag_psych"] = (44, 0),
        ["autopsy"] = (45, 0),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private readonly StringTable _strings;

    public OriginalTexts(StringTable strings)
    {
        _strings = strings;
        Staff = new StaffTexts(strings);
    }

    /// <summary>Names, descriptions and professions of staff.</summary>
    public StaffTexts Staff { get; }

    /// <summary>
    /// A string by CorsixTH's 1-based <c>S[section][index]</c> (<c>languages/original_strings.lua</c>), such as the
    /// adviser's messages in S[11] and S[28], or null if missing or empty.
    /// </summary>
    public string? String(int section, int index) => _strings.Get(section - 1, index - 1) is { Length: > 0 } text ? text : null;

    /// <summary>The disease's texts, or null for an unknown disease or one missing from the strings.</summary>
    public DiseaseText? Disease(string diseaseId)
    {
        if (!Diseases.TryGetValue(diseaseId, out var indices) || _strings.Get(DiseaseNamesSection, indices.Name - 1) is not { } name)
        {
            return null;
        }

        return indices.Cause == 0
            ? new DiseaseText(name, null, null, null)
            : new DiseaseText(
                name,
                _strings.Get(FaxesAndDiseasesSection, indices.Cause - 1),
                _strings.Get(FaxesAndDiseasesSection, indices.Cause),
                _strings.Get(FaxesAndDiseasesSection, indices.Cause + 1));
    }

    /// <summary>The disease's name, or its id when it has none.</summary>
    public string DiseaseName(string diseaseId) => Disease(diseaseId)?.Name ?? diseaseId;

    /// <summary>
    /// A VIP's name, 1 to 10 (CorsixTH's <c>vip_names[n]</c>, <c>S[42][n + 1]</c>; <c>S[42][1]</c> is the health
    /// minister), or null if missing.
    /// </summary>
    public string? VipName(int number) => number >= 1 ? _strings.Get(VipNamesSection, number) : null;
}
