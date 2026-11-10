// Machine data, ported from CorsixTH CorsixTH/Lua/objects/machines/*.lua (default_strength, crashed_animation,
// smoke_animation, the handyman's usage_animations, handyman_position, added_handyman_animate_offset_while_in_use).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Entities;

namespace CorsixNet.Sim.Definitions;

/// <summary>What makes an object a machine that wears out and is repaired by handymen.</summary>
/// <param name="Crashed">What is left of it after it exploded (<c>crashed_animation</c>).</param>
/// <param name="Smoke">The smoke rising from it when it needs urgent repair (<c>smoke_animation</c>).</param>
/// <param name="Repair">
/// A handyman repairing it, facing north and south; east and west mirror them (the handyman's
/// <c>usage_animations</c>).
/// </param>
/// <param name="HandymanPositions">
/// Where the handyman stands to repair it, relative to the placement tile, by orientation; the first one in the same room
/// that can be reached from the use position (<c>setHandymanRepairPosition</c>). Without one he stands at the use position.
/// </param>
/// <param name="HandymanOffsets">
/// How far the handyman's animation is drawn from where a patient's is, by orientation
/// (<c>added_handyman_animate_offset_while_in_use</c>).
/// </param>
public sealed record MachineDefinition(
    AnimationRef Crashed,
    AnimationRef Smoke,
    UseAnimations Repair,
    IReadOnlyDictionary<Direction, IReadOnlyList<(int X, int Y)>>? HandymanPositions = null,
    IReadOnlyDictionary<Direction, (int X, int Y)>? HandymanOffsets = null)
{
    /// <summary>The fewest frames a repair takes (<c>repair_action.min_length</c>).</summary>
    public const int MinimumRepairFrames = 20;
}
