// Values from CorsixTH CorsixTH/Lua/rooms/*.lua and CorsixTH/Lua/walls/*.lua.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Definitions;

namespace CorsixNet.Content;

/// <summary>
/// Content definitions of the original game. For now they are written in code; they will move
/// to data files that mods can extend.
/// </summary>
public static class BaseGameContent
{
    /// <summary>The "white" wall set (walls/white.lua).</summary>
    public static readonly WallSet WhiteWalls = new(InsideNorth: 82, InsideWest: 83, OutsideNorth: 90, OutsideWest: 91);

    /// <summary>The GP's office (rooms/gp.lua).</summary>
    public static readonly RoomDefinition GpOffice = new("gp", "GP's Office", MinimumSize: 4, FloorSprite: 18, WhiteWalls);

    /// <summary>Sprites of the hospital's outer walls (walls/external.lua).</summary>
    private static readonly ushort[] ExternalWalls =
    [
        122, 124, 126, 144, 123, 127, 125, 145,
        114, 116, 118, 120, 142, 115, 119, 117, 121, 143,
    ];

    public static GameContent Create() => new([GpOffice], ExternalWalls);
}
