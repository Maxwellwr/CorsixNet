// The level settings research goes by, as CorsixTH reads them from the level files
// (CorsixTH/Lua/research_department.lua, base_config.lua's objects, expertise and gbv entries).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Research;

/// <summary>A level's <c>objects[n]</c> entry for an object research knows about.</summary>
/// <param name="StartCost">What it costs at the start (<c>StartCost</c>).</param>
/// <param name="StartAvail">Whether it is there from the start (<c>StartAvail</c>).</param>
/// <param name="WhenAvail">The month of the game it comes by itself, or 0 for never (<c>WhenAvail</c>).</param>
/// <param name="StartStrength">How many uses a new one takes before it breaks down (<c>StartStrength</c>).</param>
/// <param name="ForLevel">Whether the level has it at all (<c>AvailableForLevel</c>).</param>
/// <param name="ResearchRequired">The points to discover it (<c>RschReqd</c>), or null to use its fallback expertise's.</param>
public readonly record struct ResearchObjectRules(int StartCost, bool StartAvail, int WhenAvail, int StartStrength, bool ForLevel, int? ResearchRequired);

/// <summary>What research goes by on a level.</summary>
public sealed class ResearchRules
{
    /// <summary>base_config.lua's <c>gbv.ResearchPointsDivisor</c>.</summary>
    public const int DefaultPointsDivisor = 5;

    /// <summary>base_config.lua's <c>gbv.RschImproveCostPercent</c>.</summary>
    public const int DefaultImproveCostPercent = 10;

    /// <summary>base_config.lua's <c>gbv.RschImproveIncrementPercent</c>.</summary>
    public const int DefaultImproveIncrementPercent = 10;

    /// <summary>base_config.lua's <c>gbv.DrugImproveRate</c>.</summary>
    public const int DefaultDrugImproveRate = 5;

    /// <summary>base_config.lua's <c>gbv.MinDrugCost</c>.</summary>
    public const int DefaultMinDrugCost = 50;

    /// <summary>base_config.lua's <c>gbv.MaxObjectStrength</c>.</summary>
    public const int DefaultMaxObjectStrength = 20;

    /// <summary>base_config.lua's <c>gbv.ResearchIncrement</c>.</summary>
    public const int DefaultResearchIncrement = 2;

    /// <summary>base_config.lua's <c>gbv.AutopsyRschPercent</c>.</summary>
    public const int DefaultAutopsyPercent = 33;

    /// <summary>base_config.lua's <c>gbv.AutopsyRepHitPercent</c>.</summary>
    public const int DefaultAutopsyReputationHitPercent = 25;

    private const int MaxObjects = 256;

    private readonly SortedDictionary<int, ResearchObjectRules> _objects;
    private readonly int[] _expertiseRequired;

    /// <param name="objects">The level's entries for the objects research knows about, by object type number.</param>
    /// <param name="expertiseRequired">Each expertise's <c>RschReqd</c>, by expertise number; missing ones count 0.</param>
    public ResearchRules(
        IEnumerable<KeyValuePair<int, ResearchObjectRules>>? objects = null,
        IReadOnlyList<int>? expertiseRequired = null,
        int pointsDivisor = DefaultPointsDivisor,
        int improveCostPercent = DefaultImproveCostPercent,
        int improveIncrementPercent = DefaultImproveIncrementPercent,
        int drugImproveRate = DefaultDrugImproveRate,
        int minDrugCost = DefaultMinDrugCost,
        int maxObjectStrength = DefaultMaxObjectStrength,
        int researchIncrement = DefaultResearchIncrement,
        int autopsyPercent = DefaultAutopsyPercent,
        int autopsyReputationHitPercent = DefaultAutopsyReputationHitPercent)
    {
        _objects = new SortedDictionary<int, ResearchObjectRules>();
        foreach (var (thob, entry) in objects ?? [])
        {
            _objects[thob] = entry;
        }

        _expertiseRequired = new int[DiseaseRules.ExpertiseCount + 1];
        for (var i = 0; i < Math.Min(_expertiseRequired.Length, expertiseRequired?.Count ?? 0); i++)
        {
            _expertiseRequired[i] = expertiseRequired![i];
        }

        PointsDivisor = pointsDivisor > 0 ? pointsDivisor : DefaultPointsDivisor;
        ImproveCostPercent = improveCostPercent;
        ImproveIncrementPercent = improveIncrementPercent;
        DrugImproveRate = drugImproveRate;
        MinDrugCost = minDrugCost;
        MaxObjectStrength = maxObjectStrength;
        ResearchIncrement = researchIncrement;
        AutopsyPercent = autopsyPercent;
        AutopsyReputationHitPercent = autopsyReputationHitPercent;
    }

    /// <summary>No objects to research: what levels without settings get.</summary>
    public static ResearchRules Default { get; } = new();

    /// <summary>Divides the points the researchers produce (<c>gbv.ResearchPointsDivisor</c>).</summary>
    public int PointsDivisor { get; }

    /// <summary>The first improvement of an object takes this percent of its discovery's points (<c>RschImproveCostPercent</c>).</summary>
    public int ImproveCostPercent { get; }

    /// <summary>Each further improvement takes this many percent more (<c>RschImproveIncrementPercent</c>).</summary>
    public int ImproveIncrementPercent { get; }

    /// <summary>What an improvement adds to a drug's effectiveness (<c>DrugImproveRate</c>).</summary>
    public int DrugImproveRate { get; }

    /// <summary>The lowest a drug's cost gets by research (<c>MinDrugCost</c>).</summary>
    public int MinDrugCost { get; }

    /// <summary>The highest a machine's strength gets by research (<c>MaxObjectStrength</c>).</summary>
    public int MaxObjectStrength { get; }

    /// <summary>What an improvement adds to a machine's strength (<c>ResearchIncrement</c>).</summary>
    public int ResearchIncrement { get; }

    /// <summary>The share of an object's discovery an autopsy brings, in percent (<c>AutopsyRschPercent</c>).</summary>
    public int AutopsyPercent { get; }

    /// <summary>The share of its reputation a hospital loses when an autopsy comes out, in percent (<c>AutopsyRepHitPercent</c>).</summary>
    public int AutopsyReputationHitPercent { get; }

    /// <summary>The level's entry for an object type number, if it has one.</summary>
    public ResearchObjectRules? Object(int objectType) => _objects.TryGetValue(objectType, out var entry) ? entry : null;

    /// <summary>The points an expertise's drug improvement or fallback discovery takes (<c>expertise[n].RschReqd</c>).</summary>
    public int ExpertiseRequired(int expertiseId) => expertiseId >= 0 && expertiseId < _expertiseRequired.Length ? _expertiseRequired[expertiseId] : 0;

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(_objects.Count);
        foreach (var (thob, entry) in _objects)
        {
            writer.WriteInt32(thob);
            writer.WriteInt32(entry.StartCost);
            writer.WriteBool(entry.StartAvail);
            writer.WriteInt32(entry.WhenAvail);
            writer.WriteInt32(entry.StartStrength);
            writer.WriteBool(entry.ForLevel);
            writer.WriteInt32(entry.ResearchRequired ?? -1);
        }

        writer.WriteInt32(_expertiseRequired.Length);
        foreach (var required in _expertiseRequired)
        {
            writer.WriteInt32(required);
        }

        writer.WriteInt32(PointsDivisor);
        writer.WriteInt32(ImproveCostPercent);
        writer.WriteInt32(ImproveIncrementPercent);
        writer.WriteInt32(DrugImproveRate);
        writer.WriteInt32(MinDrugCost);
        writer.WriteInt32(MaxObjectStrength);
        writer.WriteInt32(ResearchIncrement);
        writer.WriteInt32(AutopsyPercent);
        writer.WriteInt32(AutopsyReputationHitPercent);
    }

    internal static ResearchRules Read(SimReader reader)
    {
        var objects = new List<KeyValuePair<int, ResearchObjectRules>>();
        var count = reader.ReadCount(MaxObjects);
        for (var i = 0; i < count; i++)
        {
            var thob = reader.ReadInt32();
            var entry = new ResearchObjectRules(reader.ReadInt32(), reader.ReadBool(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadBool(), reader.ReadInt32() is var required and >= 0 ? required : null);
            objects.Add(KeyValuePair.Create(thob, entry));
        }

        var expertise = new int[reader.ReadCount(MaxObjects)];
        for (var i = 0; i < expertise.Length; i++)
        {
            expertise[i] = reader.ReadInt32();
        }

        var settings = new int[8];
        for (var i = 0; i < settings.Length; i++)
        {
            settings[i] = reader.ReadInt32();
        }

        var reputationHit = reader.Version is 0 or >= 71 ? reader.ReadInt32() : DefaultAutopsyReputationHitPercent;
        return new ResearchRules(objects, expertise, settings[0], settings[1], settings[2], settings[3], settings[4], settings[5], settings[6], settings[7], reputationHit);
    }
}
