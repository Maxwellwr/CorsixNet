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
/// <param name="UsePosition">
/// Where someone stands to use the object, relative to the placement tile; null for CorsixTH's
/// <c>use_position = "passable"</c>, the footprint's first use position.
/// </param>
/// <param name="SecondaryUsePosition">A second place of use, such as the receptionist's side of the reception desk.</param>
/// <param name="FinishUsePosition">Where the user ends up after using the object, if not where they started.</param>
/// <param name="AnimateFromUsePosition">
/// Whether the user's animation is drawn from the use position rather than from the object
/// (CorsixTH's <c>use_animate_from_use_position</c>).
/// </param>
public sealed record ObjectOrientation(
    IReadOnlyList<FootprintTile> Footprint,
    (int X, int Y) RenderAttach = default,
    (int X, int Y)? UsePosition = null,
    (int X, int Y)? SecondaryUsePosition = null,
    (int X, int Y)? FinishUsePosition = null,
    bool AnimateFromUsePosition = false)
{
    /// <summary>The use position relative to the placement tile.</summary>
    public (int X, int Y) Use => UsePosition ?? Footprint.Where(part => part.IsUsePosition).Select(part => (part.X, part.Y)).FirstOrDefault();
}

/// <summary>An animation of the original's animation set with its length in frames, one frame per tick.</summary>
/// <param name="Id">Animation number in <c>VSTART-1</c>.</param>
/// <param name="Length">Number of frames, which is how many ticks it takes.</param>
public readonly record struct AnimationRef(int Id, int Length);

/// <summary>
/// How one kind of humanoid uses an object facing one way: an optional animation to start, the
/// animations of use (one picked at random each time round) and an optional one to finish, as in
/// CorsixTH's <c>usage_animations</c>.
/// </summary>
public sealed record UseAnimations(AnimationRef? Begin, IReadOnlyList<AnimationRef> InUse, AnimationRef? Finish);

/// <summary>A kind of object the player can place, as in CorsixTH's <c>objects/*.lua</c>.</summary>
/// <param name="Id">Stable identifier used in commands and saves, e.g. "desk".</param>
/// <param name="Name">Name shown to the player.</param>
/// <param name="ObjectType">Object type number in map files ("thob").</param>
/// <param name="Orientations">Layout per facing direction; directions without one cannot be used.</param>
/// <param name="CorridorObject">Whether the object may stand outside rooms.</param>
/// <param name="UsageNorth">Animations of use when the object faces north (and, mirrored, west), by kind of humanoid.</param>
/// <param name="UsageEast">Animations of use when the object faces east (and, mirrored, south), by kind of humanoid.</param>
/// <param name="WalkInToUse">Whether the user walks from the use position onto the object first (CorsixTH's <c>walk_in_to_use</c>).</param>
public sealed record ObjectDefinition(
    string Id,
    string Name,
    byte ObjectType,
    IReadOnlyDictionary<Direction, ObjectOrientation> Orientations,
    bool CorridorObject = false,
    IReadOnlyDictionary<HumanoidType, UseAnimations>? UsageNorth = null,
    IReadOnlyDictionary<HumanoidType, UseAnimations>? UsageEast = null,
    bool WalkInToUse = false)
{
    /// <summary>
    /// The animations for <paramref name="type"/> using the object facing <paramref name="orientation"/>, and
    /// whether they are mirrored; south and west mirror east and north as in CorsixTH.
    /// </summary>
    public (UseAnimations Animations, bool Flip)? Usage(Direction orientation, HumanoidType type)
    {
        var (table, flip) = orientation switch
        {
            Direction.North => (UsageNorth, false),
            Direction.East => (UsageEast, false),
            Direction.South => (UsageEast, true),
            _ => (UsageNorth, true),
        };
        return table is not null && table.TryGetValue(type, out var animations) ? (animations, flip) : null;
    }
}
