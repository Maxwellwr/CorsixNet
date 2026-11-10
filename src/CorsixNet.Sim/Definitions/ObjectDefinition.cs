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

    /// <summary>
    /// The object stands against one side of the tile, the side it faces, and leaves the tile itself free
    /// ("only_side", CorsixTH's side objects: radiator, fire extinguisher, bin).
    /// </summary>
    OnlySide = 1 << 7,
}

/// <summary>One tile of a footprint, relative to the tile the object is placed on.</summary>
public readonly record struct FootprintTile(int X, int Y, FootprintFlags Flags = FootprintFlags.None)
{
    public bool IsUsePosition => (Flags & FootprintFlags.UsePosition) != 0;

    public bool IsSide => (Flags & FootprintFlags.OnlySide) != 0;

    public bool IsShareable => (Flags & FootprintFlags.Shareable) != 0;
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
/// <param name="Begin">The first animation (<c>begin_use</c>).</param>
/// <param name="InUse">The animation of use, or several to pick from at random (<c>in_use</c>).</param>
/// <param name="Finish">The animation that ends the use (<c>finish_use</c>).</param>
/// <param name="Begin2">A second begin animation after the first (<c>begin_use_2</c>).</param>
/// <param name="Finish2">A second finish animation after the first (<c>finish_use_2</c>).</param>
public sealed record UseAnimations(AnimationRef? Begin, IReadOnlyList<AnimationRef> InUse, AnimationRef? Finish, AnimationRef? Begin2 = null, AnimationRef? Finish2 = null)
{
    /// <summary>The third to fifth begin and finish animations, which few objects have (the scanner).</summary>
    public MoreUseAnimations? More { get; init; }

    /// <summary>Every animation the use may play.</summary>
    public IEnumerable<AnimationRef> All =>
        new[] { Begin, Begin2, More?.Begin3, More?.Begin4, More?.Begin5, Finish, Finish2, More?.Finish3, More?.Finish4, More?.Finish5 }
            .OfType<AnimationRef>().Concat(InUse);
}

/// <summary>An object's <c>begin_use_3</c> to <c>begin_use_5</c> and <c>finish_use_3</c> to <c>finish_use_5</c>.</summary>
public sealed record MoreUseAnimations(
    AnimationRef? Begin3 = null,
    AnimationRef? Begin4 = null,
    AnimationRef? Begin5 = null,
    AnimationRef? Finish3 = null,
    AnimationRef? Finish4 = null,
    AnimationRef? Finish5 = null);

/// <summary>
/// One phase of two people using an object together: the animation of the one who uses it, and the animation the
/// other one plays meanwhile, or null when they are hidden because the first animation shows them too.
/// </summary>
public readonly record struct MultiUsePhase(AnimationRef Primary, AnimationRef? Secondary = null)
{
    /// <summary>How long the phase lasts: the longer of the two animations, as CorsixTH's multi-use action.</summary>
    public int Length => Secondary is { } secondary ? Math.Max(Primary.Length, secondary.Length) : Primary.Length;
}

/// <summary>
/// How two kinds of humanoid use an object together, as CorsixTH's <c>multi_usage_animations</c>: the begin
/// phases (<c>begin_use</c> to <c>begin_use_5</c>), the in-use phase and the finish phases (<c>finish_use</c> to
/// <c>finish_use_5</c>), each played once.
/// </summary>
public sealed record MultiUseAnimations(IReadOnlyList<MultiUsePhase> Begin, MultiUsePhase InUse, IReadOnlyList<MultiUsePhase> Finish)
{
    /// <summary>All phases in order.</summary>
    public IEnumerable<MultiUsePhase> Phases => Begin.Append(InUse).Concat(Finish);
}

/// <summary>A kind of object the player can place, as in CorsixTH's <c>objects/*.lua</c>.</summary>
/// <param name="Id">Stable identifier used in commands and saves, e.g. "desk".</param>
/// <param name="Name">Name shown to the player.</param>
/// <param name="ObjectType">Object type number in map files ("thob").</param>
/// <param name="Orientations">Layout per facing direction; directions without one cannot be used.</param>
/// <param name="CorridorObject">Whether the object may stand outside rooms.</param>
/// <param name="UsageNorth">Animations of use when the object faces north (and, mirrored, west without its own), by kind of humanoid.</param>
/// <param name="UsageEast">Animations of use when the object faces east (and, mirrored, south without its own), by kind of humanoid.</param>
/// <param name="WalkInToUse">Whether the user walks from the use position onto the object first (CorsixTH's <c>walk_in_to_use</c>).</param>
/// <param name="UsageSouth">Animations of use facing south, for objects that have their own (and, mirrored, east without its own).</param>
/// <param name="UsageWest">Animations of use facing west, for objects that have their own (and, mirrored, north without its own).</param>
/// <param name="VisibleWhileUsed">Whether the object stays drawn while used, its use animation showing only the user (<c>object_visible</c>).</param>
/// <param name="MultiUsage">
/// Animations of two people using the object together, per facing direction and by the kinds of the one using it
/// and the one with them. A direction without its own mirrors the opposite one, as CorsixTH's <c>orient_mirror</c>.
/// Where CorsixTH gives lists of animations for the phases, one variant is picked at random for the whole use, as its
/// <c>random_anim</c>; each variant is one entry here.
/// </param>
public sealed record ObjectDefinition(
    string Id,
    string Name,
    byte ObjectType,
    IReadOnlyDictionary<Direction, ObjectOrientation> Orientations,
    bool CorridorObject = false,
    IReadOnlyDictionary<HumanoidType, UseAnimations>? UsageNorth = null,
    IReadOnlyDictionary<HumanoidType, UseAnimations>? UsageEast = null,
    bool WalkInToUse = false,
    IReadOnlyDictionary<Direction, IReadOnlyDictionary<(HumanoidType Primary, HumanoidType Secondary), IReadOnlyList<MultiUseAnimations>>>? MultiUsage = null,
    IReadOnlyDictionary<HumanoidType, UseAnimations>? UsageSouth = null,
    IReadOnlyDictionary<HumanoidType, UseAnimations>? UsageWest = null,
    bool VisibleWhileUsed = false)
{
    /// <summary>
    /// The animation variants for <paramref name="primary"/> using the object facing <paramref name="orientation"/> with
    /// <paramref name="secondary"/>, and whether they are mirrored (<c>action_multi_use_object_start</c>).
    /// </summary>
    public (IReadOnlyList<MultiUseAnimations> Variants, bool Flip)? MultiUse(Direction orientation, HumanoidType primary, HumanoidType secondary)
    {
        if (MultiUsage is null)
        {
            return null;
        }

        var flip = false;
        if (!MultiUsage.TryGetValue(orientation, out var table))
        {
            var mirror = orientation switch
            {
                Direction.North => Direction.West,
                Direction.West => Direction.North,
                Direction.East => Direction.South,
                _ => Direction.East,
            };
            if (!MultiUsage.TryGetValue(mirror, out table))
            {
                return null;
            }

            flip = true;
        }

        return table.TryGetValue((primary, secondary), out var variants) && variants.Count > 0 ? (variants, flip) : null;
    }

    /// <summary>
    /// The animations for <paramref name="type"/> using the object facing <paramref name="orientation"/>, and
    /// whether they are mirrored: a direction without animations of its own mirrors the opposite one
    /// (<c>action_use_object_start</c>'s <c>orient_mirror</c>: north and west, east and south).
    /// </summary>
    public (UseAnimations Animations, bool Flip)? Usage(Direction orientation, HumanoidType type)
    {
        var table = UsageTable(orientation);
        var flip = false;
        if (table is null)
        {
            table = UsageTable(orientation switch
            {
                Direction.North => Direction.West,
                Direction.West => Direction.North,
                Direction.East => Direction.South,
                _ => Direction.East,
            });
            flip = true;
        }

        return table is not null && table.TryGetValue(type, out var animations) ? (animations, flip) : null;
    }

    /// <summary>All the tables of use animations the object has.</summary>
    public IEnumerable<IReadOnlyDictionary<HumanoidType, UseAnimations>> UsageTables =>
        new[] { UsageNorth, UsageEast, UsageSouth, UsageWest }.Where(table => table is not null)!;

    private IReadOnlyDictionary<HumanoidType, UseAnimations>? UsageTable(Direction orientation) => orientation switch
    {
        Direction.North => UsageNorth,
        Direction.East => UsageEast,
        Direction.South => UsageSouth,
        _ => UsageWest,
    };
}
