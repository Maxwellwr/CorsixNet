namespace CorsixNet.Sim.Maps;

/// <summary>A tile position on the map, in tile units.</summary>
public readonly record struct TilePosition(int X, int Y);

/// <summary>Per-tile properties of the map.</summary>
[Flags]
public enum TileFlags : uint
{
    None = 0,

    /// <summary>Humanoids can stand on the tile.</summary>
    Passable = 1 << 0,

    /// <summary>The tile is inside the hospital building.</summary>
    Hospital = 1 << 1,

    /// <summary>Rooms and objects can be built on the tile.</summary>
    Buildable = 1 << 2,

    BuildableNorth = 1 << 3,
    BuildableEast = 1 << 4,
    BuildableSouth = 1 << 5,
    BuildableWest = 1 << 6,

    /// <summary>A humanoid can move from this tile to the one north of it.</summary>
    CanTravelNorth = 1 << 7,

    CanTravelEast = 1 << 8,
    CanTravelSouth = 1 << 9,
    CanTravelWest = 1 << 10,

    /// <summary>A door or similar tall object stands on the north edge.</summary>
    TallNorth = 1 << 11,

    /// <summary>A door or similar tall object stands on the west edge.</summary>
    TallWest = 1 << 12,

    /// <summary>The floor is fully shaded by a wall to the south-west.</summary>
    ShadowFull = 1 << 13,

    /// <summary>The floor is half shaded by the tile's own west wall.</summary>
    ShadowHalf = 1 << 14,

    /// <summary>The north wall is shaded by the west wall.</summary>
    ShadowWall = 1 << 15,

    CanTravelAll = CanTravelNorth | CanTravelEast | CanTravelSouth | CanTravelWest,
}

/// <summary>
/// One map tile: the sprites of its floor and two walls (numbers in the block sheet, 0 for none),
/// its flags and its land parcel.
/// </summary>
public struct MapTile
{
    public ushort Floor { get; set; }

    public ushort NorthWall { get; set; }

    public ushort WestWall { get; set; }

    public TileFlags Flags { get; set; }

    /// <summary>Land parcel the tile belongs to; 0 is outside every parcel.</summary>
    public ushort Parcel { get; set; }

    public readonly bool Has(TileFlags flags) => (Flags & flags) == flags;

    public void Set(TileFlags flags, bool value)
    {
        Flags = value ? Flags | flags : Flags & ~flags;
    }
}
