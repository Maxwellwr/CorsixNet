// Shadow rules ported from CorsixTH CorsixTH/Src/th_map.cpp (level_map::update_shadows).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Maps;

/// <summary>
/// The tile map of a level: floors, walls and per-tile flags.
/// </summary>
/// <remarks>
/// Tile (x, y) lies at screen position (32·(x − y), 16·(x + y)) in the isometric view;
/// x grows towards the bottom right and y towards the bottom left.
/// </remarks>
public sealed class LevelMap
{
    /// <summary>Block sprites in this range count as walls when casting shadows.</summary>
    private const int FirstWallSprite = 82;

    private const int LastWallSprite = 164;

    /// <summary>First of the nine bush sprites dotted over parcels without an owner.</summary>
    private const ushort GrassDecoration = 192;

    /// <summary>Divider wall sprites: north wall with no owner on this side; +1 west, +2 owned.</summary>
    private const ushort DividerWall = 142;

    private readonly MapTile[] _tiles;
    private readonly TilePosition[] _cameraTiles;
    private readonly TilePosition[] _heliportTiles;
    private int[] _parcelOwners = [0];

    /// <summary>The tiles as the map file has them (CorsixTH's <c>original_cells</c>), or null if not kept.</summary>
    private MapTile[]? _originalTiles;

    public LevelMap(int width, int height, int playerCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerCount);

        Width = width;
        Height = height;
        PlayerCount = playerCount;
        _tiles = new MapTile[width * height];
        _cameraTiles = new TilePosition[Math.Max(playerCount, MaxPlayers)];
        _heliportTiles = new TilePosition[Math.Max(playerCount, MaxPlayers)];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The most players a map file is for (CorsixTH's <c>max_player_count</c>).</summary>
    public const int MaxPlayers = 4;

    public int PlayerCount { get; private set; }

    /// <summary>
    /// <c>level_map::set_player_count</c>, for the map editor: the players' camera and heliport tiles are kept, also for
    /// players dropped and added again.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The count is not between 1 and <see cref="MaxPlayers"/>.</exception>
    public void SetPlayerCount(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Math.Min(MaxPlayers, _cameraTiles.Length));
        PlayerCount = count;
    }

    /// <summary>Number of land parcels, including parcel 0 (outside every parcel).</summary>
    /// <remarks>Changing it keeps the owners of parcels that still exist; new parcels have no owner.</remarks>
    public int ParcelCount
    {
        get => _parcelOwners.Length;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            var owners = new int[value];
            Array.Copy(_parcelOwners, owners, Math.Min(value, _parcelOwners.Length));
            _parcelOwners = owners;
        }
    }

    /// <summary>The player (1-based) owning <paramref name="parcel"/>, or 0 if nobody owns it.</summary>
    public int GetParcelOwner(int parcel) => _parcelOwners[parcel];

    /// <summary>Whether the map keeps the tiles of its file (see <see cref="KeepOriginalTiles"/>).</summary>
    public bool HasOriginalTiles => _originalTiles is not null;

    /// <summary>
    /// Remembers the current tiles as the map file's own (CorsixTH's <c>original_cells</c>): from now on a parcel
    /// without an owner shows as grass, and its tiles come back when it gets one.
    /// </summary>
    public void KeepOriginalTiles() => _originalTiles = (MapTile[])_tiles.Clone();

    /// <summary>
    /// The tile as the map file has it (CorsixTH's <c>get_original_tile</c>); the current tile on a map that keeps
    /// no originals.
    /// </summary>
    public ref MapTile OriginalTile(int x, int y)
    {
        _ = this[x, y];
        return ref (_originalTiles ?? _tiles)[(y * Width) + x];
    }

    /// <summary>
    /// Sets the owner of a parcel. Use <see cref="Simulation.SetParcelOwner"/> during a game. On a map that keeps its
    /// original tiles this is CorsixTH's <c>level_map::set_parcel_owner</c>: an owned parcel gets its tiles back, one
    /// without an owner becomes mown grass with a few bushes and no walls or flags, and divider walls go up between
    /// indoor parcels of different owners and come down between parcels of the same owner.
    /// </summary>
    /// <returns>The tiles that lost a divider wall, so rooms beside them can get their walls back.</returns>
    public IReadOnlyList<TilePosition> SetParcelOwner(int parcel, int owner)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(owner);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(owner, PlayerCount);
        _parcelOwners[parcel] = owner;
        if (_originalTiles is not { } originals || parcel <= 0)
        {
            return [];
        }

        var split = new List<TilePosition>();
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var index = (y * Width) + x;
                ref var tile = ref _tiles[index];
                if (tile.Parcel == parcel)
                {
                    var original = originals[index];
                    if (owner != 0)
                    {
                        tile.Floor = original.Floor;
                        tile.NorthWall = original.NorthWall;
                        tile.WestWall = original.WestWall;
                        tile.Flags = original.Flags;
                    }
                    else
                    {
                        // Nicely mown grass pattern, and now and then a bush.
                        tile.Floor = (ushort)(((x & 1) << 1) + 1);
                        tile.NorthWall = ((x | y) & 7) == 0 ? (ushort)(GrassDecoration + ((x ^ y) % 9)) : (ushort)0;
                        tile.WestWall = 0;
                        tile.Flags = TileFlags.None;
                    }
                }

                if (AddRemoveDividerWall(originals, index, x, 1, north: false, parcel))
                {
                    split.Add(new TilePosition(x, y));
                }

                if (AddRemoveDividerWall(originals, index, y, Width, north: true, parcel))
                {
                    split.Add(new TilePosition(x, y));
                }
            }
        }

        UpdatePathfinding();
        UpdateShadows();
        return split;
    }

    /// <summary>Number of tiles in <paramref name="parcel"/> (CorsixTH's <c>Map:getParcelTileCount</c>).</summary>
    public int ParcelTileCount(int parcel)
    {
        var count = 0;
        foreach (var tile in _tiles)
        {
            if (tile.Parcel == parcel)
            {
                count++;
            }
        }

        return count;
    }

    public bool Contains(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    /// <summary>The tile at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The position is outside the map.</exception>
    public ref MapTile this[int x, int y]
    {
        get
        {
            if (!Contains(x, y))
            {
                throw new ArgumentOutOfRangeException(nameof(x), $"Tile ({x}, {y}) is outside the {Width}x{Height} map.");
            }

            return ref _tiles[(y * Width) + x];
        }
    }

    /// <summary>Tile the camera starts on for <paramref name="player"/> (0-based).</summary>
    public TilePosition GetCameraTile(int player) => _cameraTiles[CheckPlayer(player)];

    public void SetCameraTile(int player, TilePosition tile) => _cameraTiles[CheckPlayer(player)] = tile;

    /// <summary>Tile where the VIP helicopter lands for <paramref name="player"/> (0-based).</summary>
    public TilePosition GetHeliportTile(int player) => _heliportTiles[CheckPlayer(player)];

    public void SetHeliportTile(int player, TilePosition tile) => _heliportTiles[CheckPlayer(player)] = tile;

    private int CheckPlayer(int player)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, PlayerCount);
        return player;
    }

    /// <summary>
    /// Recomputes the shadow flags cast by walls. Ported from CorsixTH's <c>level_map::update_shadows</c>.
    /// </summary>
    public void UpdateShadows()
    {
        const TileFlags shadows = TileFlags.ShadowFull | TileFlags.ShadowHalf | TileFlags.ShadowWall;
        for (var i = 0; i < _tiles.Length; i++)
        {
            _tiles[i].Set(shadows, false);
        }

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                ref var tile = ref this[x, y];
                if (!IsWall(tile.WestWall, tile.Has(TileFlags.TallWest)))
                {
                    continue;
                }

                tile.Set(TileFlags.ShadowHalf, true);
                if (IsWall(tile.NorthWall, tile.Has(TileFlags.TallNorth)))
                {
                    tile.Set(TileFlags.ShadowWall, true);
                }
                else if (y != 0)
                {
                    ref var north = ref this[x, y - 1];
                    north.Set(TileFlags.ShadowFull, true);

                    // Wrap the shadow around the corner. CorsixTH passes this tile's
                    // TallWest flag rather than the neighbour's here; kept for fidelity.
                    if (x != 0 && !IsWall(north.WestWall, tile.Has(TileFlags.TallWest)))
                    {
                        this[x - 1, y - 1].Set(TileFlags.ShadowFull, true);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Recomputes which neighbours can be reached from each tile: walls and the map edge block
    /// travel, in both directions. Ported from CorsixTH's <c>level_map::update_pathfinding</c>.
    /// </summary>
    public void UpdatePathfinding()
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                ref var tile = ref this[x, y];
                var travel = TileFlags.CanTravelAll;
                if (x == 0)
                {
                    travel &= ~TileFlags.CanTravelWest;
                }
                else if (x == Width - 1)
                {
                    travel &= ~TileFlags.CanTravelEast;
                }

                if (y == 0)
                {
                    travel &= ~TileFlags.CanTravelNorth;
                }
                else if (y == Height - 1)
                {
                    travel &= ~TileFlags.CanTravelSouth;
                }

                tile.Flags = (tile.Flags & ~TileFlags.CanTravelAll) | travel;
            }
        }

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                ref var tile = ref this[x, y];
                if (tile.NorthWall != 0 || tile.Has(TileFlags.BlockedNorth))
                {
                    tile.Set(TileFlags.CanTravelNorth, false);
                    if (y != 0)
                    {
                        this[x, y - 1].Set(TileFlags.CanTravelSouth, false);
                    }
                }

                if (tile.WestWall != 0 || tile.Has(TileFlags.BlockedWest))
                {
                    tile.Set(TileFlags.CanTravelWest, false);
                    if (x != 0)
                    {
                        this[x - 1, y].Set(TileFlags.CanTravelEast, false);
                    }
                }
            }
        }
    }

    /// <summary>Writes the whole map, tile by tile in row order.</summary>
    public void Write(SimWriter writer)
    {
        writer.WriteInt32(Width);
        writer.WriteInt32(Height);
        writer.WriteInt32(PlayerCount);
        writer.WriteInt32(ParcelCount);
        foreach (var owner in _parcelOwners)
        {
            writer.WriteInt32(owner);
        }
        for (var player = 0; player < PlayerCount; player++)
        {
            writer.WriteTile(_cameraTiles[player]);
            writer.WriteTile(_heliportTiles[player]);
        }

        foreach (var tile in _tiles)
        {
            writer.WriteUInt16(tile.Floor);
            writer.WriteUInt16(tile.NorthWall);
            writer.WriteUInt16(tile.WestWall);
            writer.WriteUInt32((uint)tile.Flags);
            writer.WriteUInt16(tile.Parcel);
            writer.WriteInt32(tile.RoomId);
            writer.WriteInt32(tile.ObjectId);
        }

        writer.WriteBool(_originalTiles is not null);
        foreach (var tile in _originalTiles ?? [])
        {
            writer.WriteUInt16(tile.Floor);
            writer.WriteUInt16(tile.NorthWall);
            writer.WriteUInt16(tile.WestWall);
            writer.WriteUInt32((uint)tile.Flags);
        }
    }

    /// <summary>Reads a map written by <see cref="Write"/>.</summary>
    public static LevelMap Read(SimReader reader)
    {
        const int maxSize = 1024;
        const int maxPlayers = 16;
        var width = reader.ReadCount(maxSize);
        var height = reader.ReadCount(maxSize);
        var playerCount = reader.ReadCount(maxPlayers);
        if (width == 0 || height == 0 || playerCount == 0)
        {
            throw new InvalidDataException($"Invalid map dimensions {width}x{height} for {playerCount} players.");
        }

        var map = new LevelMap(width, height, playerCount)
        {
            ParcelCount = reader.ReadCount(ushort.MaxValue + 1),
        };
        for (var parcel = 0; parcel < map.ParcelCount; parcel++)
        {
            map._parcelOwners[parcel] = reader.ReadCount(playerCount);
        }
        for (var player = 0; player < playerCount; player++)
        {
            map._cameraTiles[player] = reader.ReadTile();
            map._heliportTiles[player] = reader.ReadTile();
        }

        for (var i = 0; i < map._tiles.Length; i++)
        {
            map._tiles[i] = new MapTile
            {
                Floor = reader.ReadUInt16(),
                NorthWall = reader.ReadUInt16(),
                WestWall = reader.ReadUInt16(),
                Flags = (TileFlags)reader.ReadUInt32(),
                Parcel = reader.ReadUInt16(),
                RoomId = reader.ReadInt32(),
                ObjectId = reader.ReadInt32(),
            };
        }

        if (reader.ReadBool())
        {
            map._originalTiles = new MapTile[map._tiles.Length];
            for (var i = 0; i < map._tiles.Length; i++)
            {
                map._originalTiles[i] = new MapTile
                {
                    Floor = reader.ReadUInt16(),
                    NorthWall = reader.ReadUInt16(),
                    WestWall = reader.ReadUInt16(),
                    Flags = (TileFlags)reader.ReadUInt32(),
                    Parcel = map._tiles[i].Parcel,
                };
            }
        }

        return map;
    }

    /// <summary>
    /// CorsixTH's <c>addRemoveDividerWalls</c>: where indoor tiles of two parcels meet on the tile's north or west
    /// edge, a divider wall stands if their owners differ; one next to <paramref name="parcel"/> goes if they match.
    /// </summary>
    /// <returns>Whether a divider wall was removed.</returns>
    private bool AddRemoveDividerWall(MapTile[] originals, int index, int coordinate, int delta, bool north, int parcel)
    {
        if (coordinate <= 0 || !originals[index].Has(TileFlags.Hospital) || !originals[index - delta].Has(TileFlags.Hospital))
        {
            return false;
        }

        ref var tile = ref _tiles[index];
        var other = _tiles[index - delta];
        if (tile.Parcel == other.Parcel)
        {
            return false;
        }

        var owner = GetParcelOwner(tile.Parcel);
        var otherOwner = GetParcelOwner(other.Parcel);
        if (owner != otherOwner)
        {
            var wall = (ushort)(DividerWall + (north ? 0 : 1) + (owner != 0 ? 2 : 0));
            if (north)
            {
                tile.NorthWall = wall;
            }
            else
            {
                tile.WestWall = wall;
            }
        }
        else if (tile.Parcel == parcel || other.Parcel == parcel)
        {
            if (north)
            {
                tile.NorthWall = 0;
            }
            else
            {
                tile.WestWall = 0;
            }

            return true;
        }

        return false;
    }

    private static bool IsWall(ushort wallSprite, bool tall)
    {
        return tall || wallSprite is >= FirstWallSprite and <= LastWallSprite;
    }
}
