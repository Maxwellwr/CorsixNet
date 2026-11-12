namespace CorsixNet.Sim.Diseases;

/// <summary>How a patient with a disease looks: which humanoid type and appearance layers they get.</summary>
public enum PatientLook : byte
{
    /// <summary>A standard male or female patient, chosen at random (most non-visual diseases).</summary>
    Standard,

    /// <summary>A standard male patient with one of the swollen heads (<c>diseases/bloaty_head.lua</c>).</summary>
    BloatyHead,

    /// <summary>A slack man or woman with the tongue hanging out (<c>diseases/slack_tongue.lua</c>).</summary>
    SlackTongue,

    /// <summary>A slack man with a bald head (<c>diseases/baldness.lua</c>).</summary>
    Baldness,

    /// <summary>A patient covered in hair (<c>diseases/hairyitis.lua</c>).</summary>
    Hairyitis,

    /// <summary>A man in casts (<c>diseases/fractured_bones.lua</c>; women are off by CorsixTH's default).</summary>
    FracturedBones,

    /// <summary>An alien man or woman (<c>diseases/alien_dna.lua</c>).</summary>
    Alien,

    /// <summary>A standard woman, never a man (<c>diseases/pregnant.lua</c>).</summary>
    Pregnant,

    /// <summary>A see-through man or woman (<c>diseases/transparency.lua</c>).</summary>
    Transparent,

    /// <summary>A patient of whom only the clothes show (<c>diseases/invisibility.lua</c>).</summary>
    Invisible,
}

/// <summary>
/// A disease, or a diagnosis room's "pseudo disease" that a patient pays for when diagnosed there
/// (CorsixTH's <c>diseases/*.lua</c> and <c>diagnosis/*.lua</c>).
/// </summary>
/// <remarks>
/// Visual diseases mostly need humanoid types the client cannot draw yet (invisible, Elvis, slack tongue,
/// ...); until it can, their patients look like <see cref="PatientLook.Standard"/> ones.
/// </remarks>
public sealed class DiseaseDefinition
{
    public DiseaseDefinition(string id, int expertiseId, int curePrice)
    {
        Id = id;
        ExpertiseId = expertiseId;
        CurePrice = curePrice;
    }

    /// <summary>Such as <c>bloaty_head</c>, or <c>diag_gp</c> for a pseudo disease.</summary>
    public string Id { get; }

    /// <summary>The disease's entry in the level files' <c>expertise[n]</c>.</summary>
    public int ExpertiseId { get; }

    /// <summary>Price before the level's <c>StartPrice</c>, which replaces it for real diseases.</summary>
    public int CurePrice { get; }

    /// <summary>Index into the level files' <c>visuals</c> and <c>visuals_available</c>, or null.</summary>
    public int? VisualsId { get; init; }

    /// <summary>Index into the level files' <c>non_visuals</c> and <c>non_visuals_available</c>, or null.</summary>
    public int? NonVisualsId { get; init; }

    /// <summary>Whether this is a diagnosis room's price rather than a disease.</summary>
    public bool IsPseudo { get; init; }

    /// <summary>Whether patients only come with it in emergencies (alien DNA, as CorsixTH's default config).</summary>
    public bool OnlyEmergency { get; init; }

    /// <summary>At most this many patients come with it in an emergency (<c>emergency_number</c>); 0 for pseudo diseases.</summary>
    public int EmergencyNumber { get; init; }

    /// <summary>Whether its patients need the toilet about three times as fast (<c>more_loo_use</c>).</summary>
    public bool MoreLooUse { get; init; }

    /// <summary>Whether the disease spreads in epidemics.</summary>
    public bool IsContagious { get; init; }

    /// <summary>Rooms other than the GP's office that may help diagnose it, in any order.</summary>
    public IReadOnlyList<string> DiagnosisRooms { get; init; } = [];

    /// <summary>Rooms that cure it, visited in this order.</summary>
    public IReadOnlyList<string> TreatmentRooms { get; init; } = [];

    public PatientLook Look { get; init; }
}
