// Values from CorsixTH CorsixTH/Lua/rooms/*.lua, CorsixTH/Lua/walls/*.lua and CorsixTH/Lua/objects/*.lua.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using static CorsixNet.Sim.Definitions.FootprintFlags;

namespace CorsixNet.Content;

/// <summary>
/// Content definitions of the original game. For now they are written in code; they will move
/// to data files that mods can extend.
/// </summary>
public static class BaseGameContent
{
    /// <summary>The "white" wall set (walls/white.lua).</summary>
    public static readonly WallSet WhiteWalls = new(InsideNorth: 82, InsideWest: 83, OutsideNorth: 90, OutsideWest: 91);

    /// <summary>The doctor's desk (objects/desk.lua).</summary>
    public static readonly ObjectDefinition Desk = new("desk", "Desk", ObjectType: 1, new Dictionary<Direction, ObjectOrientation>
    {
        [Direction.North] = new([new(-2, -1, CompleteCell), new(-1, -1, CompleteCell), new(-1, 0, CompleteCell), new(0, -1, NeedNorthSide), new(0, 0, UsePosition)], (0, -1)),
        [Direction.East] = new([new(0, -2, NeedEastSide), new(-1, -1, CompleteCell), new(0, -1, CompleteCell), new(-1, 0, UsePosition), new(0, 0, CompleteCell), new(-1, -2, UsePosition)], (0, -1)),
        [Direction.South] = new([new(0, -1, UsePosition), new(-2, 0, NeedSouthSide), new(-1, -1, CompleteCell), new(-1, 0, CompleteCell), new(0, 0, CompleteCell), new(-2, -1, UsePosition)], (-2, 0)),
        [Direction.West] = new([new(-1, -2, CompleteCell), new(0, 0, UsePosition), new(-1, -1, CompleteCell), new(0, -1, CompleteCell), new(-1, 0, NeedWestSide)], (-1, 0)),
    });

    /// <summary>The filing cabinet (objects/cabinet.lua).</summary>
    public static readonly ObjectDefinition Cabinet = new("cabinet", "Cabinet", ObjectType: 2, new Dictionary<Direction, ObjectOrientation>
    {
        [Direction.North] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition)]),
        [Direction.East] = new([new(0, 0, CompleteCell), new(-1, 0, UsePosition)]),
        [Direction.South] = new([new(0, 0, CompleteCell), new(0, -1, UsePosition)]),
        [Direction.West] = new([new(0, 0, CompleteCell), new(1, 0, UsePosition)]),
    });

    /// <summary>The patient's chair (objects/chair.lua).</summary>
    public static readonly ObjectDefinition Chair = new("chair", "Chair", ObjectType: 6, new Dictionary<Direction, ObjectOrientation>
    {
        [Direction.North] = new([new(0, 0, CompleteCell), new(0, -1, UsePosition | Shareable)]),
        [Direction.East] = new([new(0, 0, CompleteCell), new(1, 0, UsePosition | Shareable)]),
        [Direction.South] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition | Shareable)]),
        [Direction.West] = new([new(0, 0, CompleteCell), new(-1, 0, UsePosition | Shareable)]),
    });

    /// <summary>The GP's office (rooms/gp.lua).</summary>
    public static readonly RoomDefinition GpOffice = new(
        "gp",
        "GP's Office",
        MinimumSize: 4,
        FloorSprite: 18,
        WhiteWalls,
        NeededObjects: new Dictionary<string, int> { ["desk"] = 1, ["cabinet"] = 1, ["chair"] = 1 });

    /// <summary>Sprites of the hospital's outer walls (walls/external.lua).</summary>
    private static readonly ushort[] ExternalWalls =
    [
        122, 124, 126, 144, 123, 127, 125, 145,
        114, 116, 118, 120, 142, 115, 119, 117, 121, 143,
    ];

    public static GameContent Create() => new([GpOffice], ExternalWalls, [Desk, Cabinet, Chair]);
}
