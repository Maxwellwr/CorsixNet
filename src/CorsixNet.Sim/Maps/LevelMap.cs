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

    private readonly MapTile[] _tiles;
    private readonly TilePosition[] _cameraTiles;
    private readonly TilePosition[] _heliportTiles;

    public LevelMap(int width, int height, int playerCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerCount);

        Width = width;
        Height = height;
        PlayerCount = playerCount;
        _tiles = new MapTile[width * height];
        _cameraTiles = new TilePosition[playerCount];
        _heliportTiles = new TilePosition[playerCount];
    }

    public int Width { get; }

    public int Height { get; }

    public int PlayerCount { get; }

    /// <summary>Number of land parcels, including parcel 0 (outside every parcel).</summary>
    public int ParcelCount { get; set; } = 1;

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
    public TilePosition GetCameraTile(int player) => _cameraTiles[player];

    public void SetCameraTile(int player, TilePosition tile) => _cameraTiles[player] = tile;

    /// <summary>Tile where the VIP helicopter lands for <paramref name="player"/> (0-based).</summary>
    public TilePosition GetHeliportTile(int player) => _heliportTiles[player];

    public void SetHeliportTile(int player, TilePosition tile) => _heliportTiles[player] = tile;

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

    /// <summary>Writes the whole map, tile by tile in row order.</summary>
    public void Write(SimWriter writer)
    {
        writer.WriteInt32(Width);
        writer.WriteInt32(Height);
        writer.WriteInt32(PlayerCount);
        writer.WriteInt32(ParcelCount);
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
            };
        }

        return map;
    }

    private static bool IsWall(ushort wallSprite, bool tall)
    {
        return tall || wallSprite is >= FirstWallSprite and <= LastWallSprite;
    }
}
