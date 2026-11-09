// The game's difficulty, ported from CorsixTH CorsixTH/Lua/map.lua (Map:getDifficulty) and hospital.lua.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

namespace CorsixNet.Sim.Economy;

/// <summary>
/// The original campaign's difficulty (<c>Map:getDifficulty</c>: 1 easy, 2 medium, 3 hard). CorsixTH's own levels and
/// custom maps are always medium.
/// </summary>
public enum Difficulty : byte
{
    /// <summary>The original's <c>EASY</c> level files.</summary>
    Easy = 1,

    /// <summary>The original's <c>FULL</c> level files, CorsixTH's default.</summary>
    Medium = 2,

    /// <summary>The original's <c>HARD</c> level files.</summary>
    Hard = 3,
}
