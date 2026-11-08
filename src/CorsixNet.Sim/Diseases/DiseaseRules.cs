// Which diseases a level has, from when, and at what price, ported from CorsixTH CorsixTH/Lua/world.lua
// (World:initLevel's available diseases, World:applyLevelStartPrices, isDiseaseUsableForNewPatient); the
// defaults are CorsixTH/Lua/base_config.lua's expertise and gbv entries.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Diseases;

/// <summary>
/// The disease settings of a level, from its level files: each disease's price, whether it is known from
/// the start and how hard it is to diagnose (<c>expertise[n]</c>), how common it is (<c>visuals[n]</c>,
/// <c>non_visuals[n]</c>) and from which month it appears (<c>visuals_available[n]</c>,
/// <c>non_visuals_available[n]</c>).
/// </summary>
public sealed class DiseaseRules
{
    /// <summary>Entries of the level files' <c>expertise</c> table, numbered from 1.</summary>
    public const int ExpertiseCount = 46;

    /// <summary>Entries of the level files' <c>visuals</c> tables, numbered from 0.</summary>
    public const int VisualsCount = 14;

    /// <summary>Entries of the level files' <c>non_visuals</c> tables, numbered from 0.</summary>
    public const int NonVisualsCount = 20;

    private static readonly int[] BaseStartPrices =
    [
        0, 100, 850, 1150, 1600, 1400, 1800, 900, 2000, 450, 950, 700, 1000, 750, 0, 800, 300, 1300, 1100, 1050, 1900, 1600, 800,
        1500, 800, 800, 550, 800, 650, 400, 1700, 600, 400, 350, 1600, 500, 300, 250, 150, 200, 250, 150, 100, 200, 500, 500, 500,
    ];

    private static readonly int[] BaseMaxDiagDiffs =
    [
        0, 0, 700, 250, 250, 250, 250, 250, 350, 250, 250, 700, 1000, 700, 400, 350, 350, 1000, 350, 700, 700, 700, 350, 350,
        700, 700, 700, 350, 700, 1000, 700, 1000, 700, 1000, 700, 700, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    ];

    /// <summary>base_config.lua's <c>gbv.StartRating</c>.</summary>
    public const int BaseDrugStartRating = 100;

    /// <summary>base_config.lua's <c>gbv.StartCost</c>.</summary>
    public const int BaseDrugStartCost = 100;

    private readonly int[] _startPrices;
    private readonly bool[] _known;
    private readonly int[] _maxDiagDiffs;
    private readonly int[] _visuals;
    private readonly int[] _nonVisuals;
    private readonly int[] _visualsAvailable;
    private readonly int[] _nonVisualsAvailable;

    /// <param name="startPrices">The <c>StartPrice</c> of each <c>expertise</c> entry, indexed from 1 (index 0 is unused).</param>
    /// <param name="known">The <c>Known</c> of each <c>expertise</c> entry, indexed as <paramref name="startPrices"/>.</param>
    /// <param name="maxDiagDiffs">The <c>MaxDiagDiff</c> of each <c>expertise</c> entry, in thousandths.</param>
    /// <param name="visuals">How common each visual disease is; 0 leaves it out of the level.</param>
    /// <param name="nonVisuals">How common each non-visual disease is; 0 leaves it out of the level.</param>
    /// <param name="visualsAvailable">The month of the game after which each visual disease may appear.</param>
    /// <param name="nonVisualsAvailable">The month of the game after which each non-visual disease may appear.</param>
    /// <param name="holdVisualMonths">No visual diseases while the month of the game is below this (<c>gbv.HoldVisualMonths</c>).</param>
    /// <param name="holdVisualPeepCount">No visual diseases until this many patients came (<c>gbv.HoldVisualPeepCount</c>).</param>
    /// <param name="drugStartRating">How effective a drug is, in percent (<c>gbv.StartRating</c>).</param>
    /// <param name="drugStartCost">What a drug costs per cure (<c>gbv.StartCost</c>).</param>
    public DiseaseRules(
        IReadOnlyList<int> startPrices,
        IReadOnlyList<bool> known,
        IReadOnlyList<int> maxDiagDiffs,
        IReadOnlyList<int> visuals,
        IReadOnlyList<int> nonVisuals,
        IReadOnlyList<int> visualsAvailable,
        IReadOnlyList<int> nonVisualsAvailable,
        int holdVisualMonths,
        int holdVisualPeepCount,
        int drugStartRating = BaseDrugStartRating,
        int drugStartCost = BaseDrugStartCost)
    {
        _startPrices = Copy(startPrices, ExpertiseCount + 1, nameof(startPrices));
        _known = [.. known];
        if (_known.Length != ExpertiseCount + 1)
        {
            throw new ArgumentException($"Expected {ExpertiseCount + 1} entries.", nameof(known));
        }

        _maxDiagDiffs = Copy(maxDiagDiffs, ExpertiseCount + 1, nameof(maxDiagDiffs));
        _visuals = Copy(visuals, VisualsCount, nameof(visuals));
        _nonVisuals = Copy(nonVisuals, NonVisualsCount, nameof(nonVisuals));
        _visualsAvailable = Copy(visualsAvailable, VisualsCount, nameof(visualsAvailable));
        _nonVisualsAvailable = Copy(nonVisualsAvailable, NonVisualsCount, nameof(nonVisualsAvailable));
        HoldVisualMonths = holdVisualMonths;
        HoldVisualPeepCount = holdVisualPeepCount;
        DrugStartRating = drugStartRating;
        DrugStartCost = drugStartCost;
    }

    /// <summary>
    /// Without level files: base_config.lua's prices and diagnosis difficulties, only general practice known,
    /// and, as CorsixTH does for a level without disease settings, every disease present from the start.
    /// </summary>
    public static DiseaseRules Default { get; } = new(
        BaseStartPrices,
        [.. Enumerable.Range(0, ExpertiseCount + 1).Select(i => i == 1)],
        BaseMaxDiagDiffs,
        Enumerable.Repeat(1, VisualsCount).ToArray(),
        Enumerable.Repeat(1, NonVisualsCount).ToArray(),
        new int[VisualsCount],
        new int[NonVisualsCount],
        0,
        0);

    public int HoldVisualMonths { get; }

    public int HoldVisualPeepCount { get; }

    /// <summary>How effective a newly available drug is, in percent (<c>gbv.StartRating</c>).</summary>
    public int DrugStartRating { get; }

    /// <summary>What a newly available drug costs per cure (<c>gbv.StartCost</c>).</summary>
    public int DrugStartCost { get; }

    /// <summary>The level's <c>StartPrice</c> of an <c>expertise</c> entry.</summary>
    public int StartPrice(int expertiseId) => _startPrices[expertiseId];

    /// <summary>Whether the disease of an <c>expertise</c> entry is known from the start.</summary>
    public bool IsKnown(int expertiseId) => _known[expertiseId];

    /// <summary>How hard the disease of an <c>expertise</c> entry is to diagnose, in thousandths.</summary>
    public int MaxDiagDiff(int expertiseId) => _maxDiagDiffs[expertiseId];

    /// <summary>How common the disease is in the level; 0 when it is left out, and for pseudo diseases.</summary>
    public int Frequency(DiseaseDefinition disease)
    {
        return disease switch
        {
            { IsPseudo: true } => 0,
            { VisualsId: { } visual } => _visuals[visual],
            { NonVisualsId: { } nonVisual } => _nonVisuals[nonVisual],
            _ => 0,
        };
    }

    /// <summary>
    /// The price of a disease before the hospital's changes to it: a real disease's level <c>StartPrice</c>, a
    /// pseudo disease's own price (<c>World:applyLevelStartPrices</c> only changes real diseases).
    /// </summary>
    public int CurePrice(DiseaseDefinition disease) => disease.IsPseudo ? disease.CurePrice : StartPrice(disease.ExpertiseId);

    /// <summary>The level's diseases, in catalog order (CorsixTH's <c>world.available_diseases</c>).</summary>
    public IEnumerable<DiseaseDefinition> AvailableDiseases() => DiseaseCatalog.All.Where(disease => Frequency(disease) != 0);

    /// <summary>
    /// <c>isDiseaseUsableForNewPatient</c>: whether a new patient may come with the disease in the given month of
    /// the game, once <paramref name="visitors"/> patients have come to the hospital.
    /// </summary>
    public bool IsUsableForNewPatient(DiseaseDefinition disease, int monthOfGame, int visitors)
    {
        if (disease.OnlyEmergency)
        {
            return false;
        }

        if (disease.VisualsId is { } visual)
        {
            if (HoldVisualMonths > monthOfGame || HoldVisualPeepCount > visitors)
            {
                return false;
            }

            return _visualsAvailable[visual] < monthOfGame;
        }

        return disease.NonVisualsId is { } nonVisual && _nonVisualsAvailable[nonVisual] < monthOfGame;
    }

    internal void Write(SimWriter writer)
    {
        for (var i = 0; i <= ExpertiseCount; i++)
        {
            writer.WriteInt32(_startPrices[i]);
            writer.WriteBool(_known[i]);
            writer.WriteInt32(_maxDiagDiffs[i]);
        }

        WriteAll(writer, _visuals);
        WriteAll(writer, _nonVisuals);
        WriteAll(writer, _visualsAvailable);
        WriteAll(writer, _nonVisualsAvailable);
        writer.WriteInt32(HoldVisualMonths);
        writer.WriteInt32(HoldVisualPeepCount);
        writer.WriteInt32(DrugStartRating);
        writer.WriteInt32(DrugStartCost);
    }

    internal static DiseaseRules Read(SimReader reader)
    {
        var prices = new int[ExpertiseCount + 1];
        var known = new bool[ExpertiseCount + 1];
        var difficulties = new int[ExpertiseCount + 1];
        for (var i = 0; i <= ExpertiseCount; i++)
        {
            prices[i] = reader.ReadInt32();
            known[i] = reader.ReadBool();
            difficulties[i] = reader.ReadInt32();
        }

        return new DiseaseRules(
            prices,
            known,
            difficulties,
            ReadAll(reader, VisualsCount),
            ReadAll(reader, NonVisualsCount),
            ReadAll(reader, VisualsCount),
            ReadAll(reader, NonVisualsCount),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32());
    }

    private static int[] Copy(IReadOnlyList<int> values, int count, string name)
    {
        if (values.Count != count)
        {
            throw new ArgumentException($"Expected {count} entries.", name);
        }

        return [.. values];
    }

    private static void WriteAll(SimWriter writer, int[] values)
    {
        foreach (var value in values)
        {
            writer.WriteInt32(value);
        }
    }

    private static int[] ReadAll(SimReader reader, int count)
    {
        var values = new int[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = reader.ReadInt32();
        }

        return values;
    }
}
