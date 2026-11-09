// Ported from CorsixTH CorsixTH/Lua/world.lua (World:newFloatingDollarSign, the floating dollars in World:onTick) and
// CorsixTH/Lua/hospital.lua (Hospital:receiveMoneyForTreatment).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Maps;

namespace CorsixNet.Sim.Economy;

/// <summary>
/// A banknote with an amount floating up from a patient who has just paid (<c>World:newFloatingDollarSign</c>): it
/// starts 60 pixels above the tile the patient stood on, rises a pixel each tick and is gone after
/// <see cref="Lifetime"/> ticks. Only something to look at: it is not part of the saved state.
/// </summary>
/// <param name="Player">The hospital paid; CorsixTH shows only the local player's.</param>
/// <param name="Tile">The patient's tile when they paid.</param>
/// <param name="Amount">The amount paid.</param>
/// <param name="Insurance">Whether an insurance company pays (CorsixTH draws the banknote blue then).</param>
/// <param name="StartTick">The tick during which the patient paid.</param>
public sealed record FloatingDollarSign(int Player, TilePosition Tile, int Amount, bool Insurance, long StartTick)
{
    /// <summary>How many ticks a sign floats (<c>setLifetime(100)</c>).</summary>
    public const int Lifetime = 100;

    /// <summary>Where the sign starts, relative to the tile's drawing position (<c>setPosition(-17, -60)</c>).</summary>
    public const int StartX = -17;

    /// <inheritdoc cref="StartX"/>
    public const int StartY = -60;

    /// <summary>How many ticks the sign has floated after <paramref name="tick"/> ticks of the game.</summary>
    public long Age(long tick) => tick - StartTick;
}
