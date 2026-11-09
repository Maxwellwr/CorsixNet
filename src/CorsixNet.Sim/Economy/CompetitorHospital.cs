// Ported from CorsixTH CorsixTH/Lua/hospitals/ai_hospital.lua (AIHospital) and the parts of CorsixTH/Lua/world.lua
// (World:World, World:spawnPatient) and CorsixTH/Lua/hospital.lua (Hospital:countNewPatient) that concern it.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Economy;

/// <summary>
/// A computer opponent a level names (<c>computer[n].Playing</c> and <c>.Name</c> in the level files).
/// </summary>
/// <param name="Number">CorsixTH's competitor number: the level files' index plus one, which picks the default name in
/// <c>competitor_names</c>.</param>
/// <param name="Name">The name the level gives it, which in CorsixTH's base_config.lua every index has.</param>
public readonly record struct PlannedCompetitor(int Number, string Name);

/// <summary>
/// A computer opponent's hospital (CorsixTH's <c>AIHospital</c>). As in CorsixTH it is only a name so far: it is not
/// on the map, builds nothing and has no staff, but it takes its turn for patients, and those sent to it are counted
/// as its visitors without coming (<c>World:spawnPatient</c> for a hospital without a staffed desk).
/// </summary>
public sealed class CompetitorHospital
{
    /// <summary>The longest name read back from a save.</summary>
    private const int MaxNameBytes = 64;

    /// <param name="number">CorsixTH's competitor number (the level files' index plus one).</param>
    /// <param name="name">Its name.</param>
    public CompetitorHospital(int number, string name)
    {
        Number = number;
        Name = name;
    }

    /// <summary>CorsixTH's competitor number (the level files' index plus one).</summary>
    public int Number { get; }

    /// <summary>Its name, as the level gives it.</summary>
    public string Name { get; }

    /// <summary>Patients sent to it so far, whether they came or not (CorsixTH's <c>spawn_attempts</c>).</summary>
    public long SpawnAttempts { get; internal set; }

    /// <summary>Patients so far (<c>num_visitors</c>).</summary>
    public int Visitors { get; internal set; }

    /// <summary>Patients this year (<c>num_visitors_ty</c>), reset at the end of each year.</summary>
    public int VisitorsThisYear { get; internal set; }

    /// <summary><c>Hospital:countNewPatient</c>: a patient sent to it, counted as a visitor.</summary>
    internal void CountNewPatient()
    {
        SpawnAttempts++;
        Visitors++;
        VisitorsThisYear++;
    }

    /// <summary>The part of <c>Hospital:onEndYear</c> that concerns it.</summary>
    internal void OnEndYear() => VisitorsThisYear = 0;

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(Number);
        writer.WriteString(Name);
        writer.WriteInt64(SpawnAttempts);
        writer.WriteInt32(Visitors);
        writer.WriteInt32(VisitorsThisYear);
    }

    internal static CompetitorHospital Read(SimReader reader)
    {
        return new CompetitorHospital(reader.ReadInt32(), reader.ReadString(MaxNameBytes))
        {
            SpawnAttempts = reader.ReadInt64(),
            Visitors = reader.ReadInt32(),
            VisitorsThisYear = reader.ReadInt32(),
        };
    }
}
