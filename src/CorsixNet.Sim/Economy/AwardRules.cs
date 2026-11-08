// The level's trophy and award settings, with the defaults of CorsixTH CorsixTH/Lua/base_config.lua (awards_trophies).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Economy;

/// <summary>
/// What a hospital must do in a year to win a trophy or an award, or to be fined, and what it gets: the level files'
/// <c>awards_trophies</c> settings by name, each falling back on CorsixTH's base_config.lua. Only the settings the
/// year end uses so far are kept.
/// </summary>
public sealed class AwardRules
{
    // Trophies.
    public const string CansofCoke = "CansofCoke";
    public const string CansofCokeBonus = "CansofCokeBonus";
    public const string Reputation = "Reputation";
    public const string TrophyReputationBonus = "TrophyReputationBonus";
    public const string TrophyAllCuredBonus = "TrophyAllCuredBonus";
    public const string TrophyDeathBonus = "TrophyDeathBonus";
    public const string TrophyCuresBonus = "TrophyCuresBonus";
    public const string TrophyMayorBonus = "TrophyMayorBonus";

    // Awards and penalties.
    public const string CuresAward = "CuresAward";
    public const string CuresPoor = "CuresPoor";
    public const string DeathsAward = "DeathsAward";
    public const string DeathsPoor = "DeathsPoor";
    public const string CuresVDeathsAward = "CuresVDeathsAward";
    public const string CuresVDeathsPoor = "CuresVDeathsPoor";
    public const string ReputationAward = "ReputationAward";
    public const string ReputationPoor = "ReputationPoor";
    public const string HospValueAward = "HospValueAward";
    public const string HospValuePoor = "HospValuePoor";
    public const string CuresBonus = "CuresBonus";
    public const string CuresPenalty = "CuresPenalty";
    public const string AllCuresBonus = "AllCuresBonus";
    public const string DeathsBonus = "DeathsBonus";
    public const string DeathsPenalty = "DeathsPenalty";
    public const string CuresVDeathsBonus = "CuresVDeathsBonus";
    public const string CuresVDeathsPenalty = "CuresVDeathsPenalty";
    public const string AwardReputationBonus = "AwardReputationBonus";
    public const string AwardReputationPenalty = "AwardReputationPenalty";
    public const string HospValueBonus = "HospValueBonus";
    public const string HospValuePenalty = "HospValuePenalty";

    /// <summary>CorsixTH's base_config.lua.</summary>
    public static readonly AwardRules Default = new(new SortedDictionary<string, int>(StringComparer.Ordinal)
    {
        [CansofCoke] = 100,
        [CansofCokeBonus] = 1000,
        [Reputation] = 400,
        [TrophyReputationBonus] = 2000,
        [TrophyAllCuredBonus] = 20000,
        [TrophyDeathBonus] = 10000,
        [TrophyCuresBonus] = 6000,
        [TrophyMayorBonus] = 5,
        [CuresAward] = 50,
        [CuresPoor] = 10,
        [DeathsAward] = 10,
        [DeathsPoor] = 25,
        [CuresVDeathsAward] = 5,
        [CuresVDeathsPoor] = 1,
        [ReputationAward] = 500,
        [ReputationPoor] = 200,
        [HospValueAward] = 150000,
        [HospValuePoor] = 50000,
        [CuresBonus] = 2000,
        [CuresPenalty] = -3000,
        [AllCuresBonus] = 5000,
        [DeathsBonus] = 3000,
        [DeathsPenalty] = -5000,
        [CuresVDeathsBonus] = 4000,
        [CuresVDeathsPenalty] = -4000,
        [AwardReputationBonus] = 2000,
        [AwardReputationPenalty] = -3000,
        [HospValueBonus] = 3,
        [HospValuePenalty] = -6,
    });

    private readonly SortedDictionary<string, int> _values;

    private AwardRules(SortedDictionary<string, int> values)
    {
        _values = values;
    }

    /// <summary>The names of the settings, in ordinal order.</summary>
    public static IEnumerable<string> Names => Default._values.Keys;

    /// <summary>A setting by its name in the level files.</summary>
    public int this[string name] => _values[name];

    /// <summary>The defaults with the given settings changed; names not in <see cref="Names"/> are ignored.</summary>
    public static AwardRules With(IEnumerable<KeyValuePair<string, int>> values)
    {
        var merged = new SortedDictionary<string, int>(Default._values, StringComparer.Ordinal);
        foreach (var (name, value) in values)
        {
            if (merged.ContainsKey(name))
            {
                merged[name] = value;
            }
        }

        return new AwardRules(merged);
    }

    internal void Write(SimWriter writer)
    {
        foreach (var value in _values.Values)
        {
            writer.WriteInt32(value);
        }
    }

    internal static AwardRules Read(SimReader reader)
    {
        var values = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in Names)
        {
            values[name] = reader.ReadInt32();
        }

        return new AwardRules(values);
    }
}
