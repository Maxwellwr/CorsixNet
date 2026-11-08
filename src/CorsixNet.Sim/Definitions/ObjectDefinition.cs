using CorsixNet.Sim.Entities;

namespace CorsixNet.Sim.Definitions;

/// <summary>What an object does to one tile of its footprint, as in CorsixTH's object footprints.</summary>
[Flags]
public enum FootprintFlags : byte
{
    /// <summary>The object stands on the tile; it becomes impassable.</summary>
    None = 0,

    /// <summary>The object fills the whole tile, so every side of the tile must be free ("complete_cell").</summary>
    CompleteCell = 1 << 0,

    /// <summary>
    /// The tile stays passable: someone stands here to use the object ("only_passable").
    /// </summary>
    UsePosition = 1 << 1,

    /// <summary>The object needs the tile's north side to be free ("need_north_side").</summary>
    NeedNorthSide = 1 << 2,

    NeedEastSide = 1 << 3,
    NeedSouthSide = 1 << 4,
    NeedWestSide = 1 << 5,

    /// <summary>Another object's use position may overlap this tile ("shareable").</summary>
    Shareable = 1 << 6,
}

/// <summary>One tile of a footprint, relative to the tile the object is placed on.</summary>
public readonly record struct FootprintTile(int X, int Y, FootprintFlags Flags = FootprintFlags.None)
{
    public bool IsUsePosition => (Flags & FootprintFlags.UsePosition) != 0;
}

/// <summary>An object's layout when facing one direction.</summary>
/// <param name="Footprint">Tiles the object covers or needs.</param>
/// <param name="RenderAttach">Offset of the tile whose drawing pass draws the object.</param>
public sealed record ObjectOrientation(IReadOnlyList<FootprintTile> Footprint, (int X, int Y) RenderAttach = default);

/// <summary>A kind of object the player can place, as in CorsixTH's <c>objects/*.lua</c>.</summary>
/// <param name="Id">Stable identifier used in commands and saves, e.g. "desk".</param>
/// <param name="Name">Name shown to the player.</param>
/// <param name="ObjectType">Object type number in map files ("thob").</param>
/// <param name="Orientations">Layout per facing direction; directions without one cannot be used.</param>
/// <param name="CorridorObject">Whether the object may stand outside rooms.</param>
public sealed record ObjectDefinition(
    string Id,
    string Name,
    byte ObjectType,
    IReadOnlyDictionary<Direction, ObjectOrientation> Orientations,
    bool CorridorObject = false);
