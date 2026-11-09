// The objects CorsixTH's research knows about: those of CorsixTH/Lua/objects/*.lua with a research_category or a
// default_strength, with their research_fallback.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

namespace CorsixNet.Sim.Research;

/// <summary>Where research puts its points (the research window's areas).</summary>
public enum ResearchCategory : byte
{
    /// <summary>Discovering treatment equipment (<c>cure</c>).</summary>
    Cure,

    /// <summary>Discovering diagnosis equipment (<c>diagnosis</c>).</summary>
    Diagnosis,

    /// <summary>Improving drugs (<c>drugs</c>).</summary>
    Drugs,

    /// <summary>Improving machines (<c>improvements</c>).</summary>
    Improvements,

    /// <summary>Concentrating on one disease's drug or machine (<c>specialisation</c>).</summary>
    Specialisation,
}

/// <summary>An object research knows about.</summary>
/// <param name="Id">The object's id, as CorsixTH's.</param>
/// <param name="ObjectType">Its number in the level files (<c>thob</c>).</param>
/// <param name="Category">Cure or diagnosis, for objects to discover (<c>research_category</c>); null for none.</param>
/// <param name="Fallback">The expertise whose <c>RschReqd</c> stands in for the object's own (<c>research_fallback</c>), or 0.</param>
/// <param name="DefaultStrength">A machine's strength (<c>default_strength</c>), or 0 for objects that are not machines.</param>
public sealed record ResearchObject(string Id, int ObjectType, ResearchCategory? Category, int Fallback, int DefaultStrength)
{
    /// <summary>Whether the object is a machine, which research can improve.</summary>
    public bool IsMachine => DefaultStrength > 0;
}

/// <summary>CorsixTH's researchable objects, in the order of its objects folder (<c>TheApp.objects</c>).</summary>
public static class ResearchObjects
{
    public static IReadOnlyList<ResearchObject> All { get; } =
    [
        new("analyser", 41, ResearchCategory.Cure, 45, 0),
        new("autopsy", 55, ResearchCategory.Cure, 0, 0),
        new("bed", 8, ResearchCategory.Diagnosis, 0, 0),
        new("computer", 40, ResearchCategory.Cure, 46, 0),
        new("couch", 18, ResearchCategory.Diagnosis, 0, 0),
        new("crash_trolley", 20, ResearchCategory.Diagnosis, 0, 0),
        new("blood_machine", 42, ResearchCategory.Diagnosis, 37, 12),
        new("cardio", 13, ResearchCategory.Diagnosis, 38, 12),
        new("cast_remover", 24, ResearchCategory.Cure, 9, 10),
        new("dna_fixer", 23, ResearchCategory.Cure, 8, 7),
        new("electrolyser", 46, ResearchCategory.Cure, 3, 10),
        new("hair_restorer", 25, ResearchCategory.Cure, 10, 8),
        new("inflator", 9, ResearchCategory.Cure, 2, 10),
        new("jelly_moulder", 47, ResearchCategory.Cure, 12, 7),
        new("operating_table", 30, ResearchCategory.Cure, 19, 8),
        new("scanner", 14, ResearchCategory.Diagnosis, 36, 12),
        new("shower", 54, ResearchCategory.Cure, 6, 10),
        new("slicer", 26, ResearchCategory.Cure, 7, 8),
        new("ultrascanner", 22, ResearchCategory.Diagnosis, 40, 12),
        new("x_ray", 27, ResearchCategory.Diagnosis, 39, 12),
        new("pharmacy_cabinet", 39, ResearchCategory.Cure, 13, 0),
    ];

    /// <summary>The researchable object with this id, or null.</summary>
    public static ResearchObject? Find(string id) => All.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.Ordinal));
}
