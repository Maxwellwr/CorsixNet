// The map editor's changes to the map, after CorsixTH CorsixTH/Lua/dialogs/resizables/map_editor.lua
// (drawCursorSpriteAtArea, makeCellFlags, copyArea, pasteArea, deleteWallsAtArea, setParcelAtArea, the camera and
// heliport tiles) and CorsixTH/Src/th_map.cpp (level_map::load_blank, level_map::save).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Formats;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;

namespace CorsixNet.Content.Levels;

/// <summary>What a floor sprite makes of its tile (the editor's sprite types <c>floor</c>, <c>road</c> and <c>hospital</c>).</summary>
public enum EditorFloorKind
{
    /// <summary>Grass, foliage, ponds: nobody walks or builds there.</summary>
    Outside,

    /// <summary>Roads and paths: people walk, nobody builds.</summary>
    Road,

    /// <summary>The hospital's floor: walked on and built on.</summary>
    Hospital,
}

/// <summary>A tile of a copied area, at its place in the area.</summary>
public readonly record struct MapClipTile(int X, int Y, ushort Floor, ushort NorthWall, ushort WestWall, bool Buildable, bool Passable, bool Hospital);

/// <summary>An area copied with the right mouse button (<c>copyArea</c>): its tiles and the objects on them, by place in the area.</summary>
public sealed record MapClip(int Width, int Height, IReadOnlyList<MapClipTile> Tiles, IReadOnlyList<MapObjectPlacement> Objects);

/// <summary>
/// The map editor's model (CorsixTH's <c>UIMapEditor</c> without its sprite pages): painting floor and wall sprites,
/// deleting walls, setting parcels, the players' camera and heliport tiles, copying and pasting areas, and saving the
/// map as a map file (<c>level_map::save</c>).
/// </summary>
/// <remarks>
/// Objects are kept by the tile the map file places them on; deleting walls and pasting remove the objects placed on
/// the tiles, where CorsixTH removes those whose footprint covers them. CorsixTH's save writes the tile's tall west
/// wall flag into the object direction byte, so its saved maps lose the objects' directions; here a tile with an
/// object keeps the object's direction, and pond foliage moved onto the wall layer when loading goes back to the floor.
/// </remarks>
public sealed class MapEditor
{
    /// <summary>The floor drawn under pond foliage moved to the wall layer (see <see cref="LevelMapLoader"/>).</summary>
    private const ushort PondBase = 69;

    private readonly Dictionary<TilePosition, MapObjectPlacement> _objects = [];

    /// <summary>Edits a map; it should be loaded for editing (<see cref="LevelMapLoader.Load(MapFile, bool)"/>).</summary>
    /// <exception cref="ArgumentException">The map is not the size of a map file.</exception>
    public MapEditor(LevelMap map, IEnumerable<MapObjectPlacement>? objects = null)
    {
        if (map.Width != MapFile.Size || map.Height != MapFile.Size || map.PlayerCount > MapFile.MaxPlayers)
        {
            throw new ArgumentException($"A map file's map is {MapFile.Size} by {MapFile.Size} tiles for up to {MapFile.MaxPlayers} players.", nameof(map));
        }

        Map = map;
        foreach (var placement in objects ?? [])
        {
            _objects[placement.Tile] = placement;
        }
    }

    /// <summary>The map edited.</summary>
    public LevelMap Map { get; }

    /// <summary>The objects the map places, such as the entrance doors and the trees.</summary>
    public IReadOnlyCollection<MapObjectPlacement> Objects => _objects.Values;

    /// <summary>
    /// <c>level_map::load_blank</c>: a map for one player, all checkered grass in no parcel, the camera in the middle
    /// and the heliport in the corner.
    /// </summary>
    public static MapEditor Blank()
    {
        var map = new LevelMap(MapFile.Size, MapFile.Size, playerCount: 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Floor = (ushort)(2 + (x % 2));
            }
        }

        map.SetCameraTile(0, new TilePosition(63, 63));
        map.SetHeliportTile(0, new TilePosition(0, 0));
        return new MapEditor(map);
    }

    /// <summary>A map file to edit: its tiles as the file has them, with no parcel owners.</summary>
    public static MapEditor Open(MapFile file)
    {
        var level = LevelMapLoader.Load(file, editing: true);
        return new MapEditor(level.Map, level.Objects);
    }

    /// <summary><c>getCoveredArea</c>: the tiles of the rectangle with these corners, in any order, that are on the map.</summary>
    public IEnumerable<TilePosition> Area(TilePosition corner, TilePosition other)
    {
        var (minX, maxX) = (Math.Min(corner.X, other.X), Math.Max(corner.X, other.X));
        var (minY, maxY) = (Math.Min(corner.Y, other.Y), Math.Max(corner.Y, other.Y));
        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                if (Map.Contains(x, y))
                {
                    yield return new TilePosition(x, y);
                }
            }
        }
    }

    /// <summary>A floor sprite painted on tiles, with the flags its kind gives (<c>makeCellFlags</c>).</summary>
    public void PaintFloor(IEnumerable<TilePosition> tiles, ushort sprite, EditorFloorKind kind)
    {
        foreach (var position in tiles)
        {
            ref var tile = ref Map[position.X, position.Y];
            tile.Floor = sprite;
            tile.Set(TileFlags.Buildable, kind == EditorFloorKind.Hospital);
            tile.Set(TileFlags.Passable, kind != EditorFloorKind.Outside);
            tile.Set(TileFlags.Hospital, kind == EditorFloorKind.Hospital);
        }
    }

    /// <summary>A north wall sprite painted on tiles (sprite 0 for none).</summary>
    public void PaintNorthWall(IEnumerable<TilePosition> tiles, ushort sprite)
    {
        foreach (var position in tiles)
        {
            Map[position.X, position.Y].NorthWall = sprite;
        }
    }

    /// <summary>A west wall sprite painted on tiles (sprite 0 for none).</summary>
    public void PaintWestWall(IEnumerable<TilePosition> tiles, ushort sprite)
    {
        foreach (var position in tiles)
        {
            Map[position.X, position.Y].WestWall = sprite;
        }
    }

    /// <summary><c>drawObject</c>: an object placed on a tile, instead of any placed there.</summary>
    public void PlaceObject(MapObjectPlacement placement)
    {
        if (Map.Contains(placement.Tile.X, placement.Tile.Y))
        {
            _objects[placement.Tile] = placement;
        }
    }

    /// <summary>The entrance doors' object types (objects/doors/entrance_left_door.lua and entrance_right_door.lua).</summary>
    public const byte EntranceLeftDoor = 58;

    /// <inheritdoc cref="EntranceLeftDoor"/>
    public const byte EntranceRightDoor = 59;

    /// <summary>
    /// <c>checkObjectSpace</c> and <c>drawObject</c> for an entrance door: the right door on <paramref name="tile"/> and
    /// the left one west of it in a north wall, or north of it in a west wall; any doors on those two tiles go first,
    /// with their other halves.
    /// </summary>
    public void PlaceEntranceDoor(TilePosition tile, bool west)
    {
        var left = west ? new TilePosition(tile.X, tile.Y - 1) : new TilePosition(tile.X - 1, tile.Y);
        foreach (var position in new[] { tile, left })
        {
            if (!_objects.TryGetValue(position, out var placed) || placed.ObjectType is not (EntranceLeftDoor or EntranceRightDoor))
            {
                continue;
            }

            var placedWest = (placed.Direction & 1) != 0;
            var (dx, dy) = placedWest ? (0, 1) : (1, 0);
            var other = placed.ObjectType == EntranceRightDoor
                ? new TilePosition(position.X - dx, position.Y - dy)
                : new TilePosition(position.X + dx, position.Y + dy);
            _objects.Remove(position);
            if (_objects.TryGetValue(other, out var half) && half.ObjectType is EntranceLeftDoor or EntranceRightDoor && half.ObjectType != placed.ObjectType)
            {
                _objects.Remove(other);
            }
        }

        var direction = west ? (byte)1 : (byte)0;
        PlaceObject(new MapObjectPlacement(tile, EntranceRightDoor, direction));
        PlaceObject(new MapObjectPlacement(left, EntranceLeftDoor, direction));
    }

    /// <summary><c>deleteWallsAtArea</c>: the tiles lose their walls and their objects; the floor stays.</summary>
    public void DeleteWalls(IEnumerable<TilePosition> tiles)
    {
        foreach (var position in tiles)
        {
            ref var tile = ref Map[position.X, position.Y];
            tile.NorthWall = 0;
            tile.WestWall = 0;
            _objects.Remove(position);
        }
    }

    /// <summary><c>setParcelAtArea</c>: the tiles go into a parcel (0 for none).</summary>
    public void SetParcel(IEnumerable<TilePosition> tiles, int parcel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(parcel);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(parcel, ushort.MaxValue);
        if (parcel >= Map.ParcelCount)
        {
            Map.ParcelCount = parcel + 1;
        }

        foreach (var position in tiles)
        {
            Map[position.X, position.Y].Parcel = (ushort)parcel;
        }
    }

    /// <summary>The camera's starting tile for a player (0-based), as the editor's camera pages set it.</summary>
    public void SetCamera(int player, TilePosition tile) => Map.SetCameraTile(player, tile);

    /// <summary>The VIP helicopter's landing tile for a player (0-based), as the editor's heliport pages set it.</summary>
    public void SetHeliport(int player, TilePosition tile) => Map.SetHeliportTile(player, tile);

    /// <summary><c>copyArea</c>: the tiles of an area, their floor flags and the objects placed on them.</summary>
    public MapClip Copy(TilePosition corner, TilePosition other)
    {
        var minX = Math.Min(corner.X, other.X);
        var minY = Math.Min(corner.Y, other.Y);
        var tiles = new List<MapClipTile>();
        var objects = new List<MapObjectPlacement>();
        foreach (var position in Area(corner, other))
        {
            var tile = Map[position.X, position.Y];
            var at = new TilePosition(position.X - minX, position.Y - minY);
            tiles.Add(new MapClipTile(at.X, at.Y, tile.Floor, tile.NorthWall, tile.WestWall, tile.Has(TileFlags.Buildable), tile.Has(TileFlags.Passable), tile.Has(TileFlags.Hospital)));
            if (_objects.TryGetValue(position, out var placement))
            {
                objects.Add(placement with { Tile = at });
            }
        }

        return new MapClip(Math.Abs(corner.X - other.X) + 1, Math.Abs(corner.Y - other.Y) + 1, tiles, objects);
    }

    /// <summary>
    /// <c>pasteArea</c>: the copied area laid over the target area again and again from its north west corner, cut off
    /// at the target's south and east edges; the objects on the pasted tiles make way for the copied ones.
    /// </summary>
    public void Paste(MapClip clip, TilePosition corner, TilePosition other)
    {
        var (minX, maxX) = (Math.Min(corner.X, other.X), Math.Min(Math.Max(corner.X, other.X), Map.Width - 1));
        var (minY, maxY) = (Math.Min(corner.Y, other.Y), Math.Min(Math.Max(corner.Y, other.Y), Map.Height - 1));
        for (var baseX = minX; baseX <= maxX; baseX += clip.Width)
        {
            for (var baseY = minY; baseY <= maxY; baseY += clip.Height)
            {
                foreach (var copied in clip.Tiles)
                {
                    var (x, y) = (baseX + copied.X, baseY + copied.Y);
                    if (x <= maxX && y <= maxY && Map.Contains(x, y))
                    {
                        ref var tile = ref Map[x, y];
                        tile.Floor = copied.Floor;
                        tile.NorthWall = copied.NorthWall;
                        tile.WestWall = copied.WestWall;
                        tile.Set(TileFlags.Buildable, copied.Buildable);
                        tile.Set(TileFlags.Passable, copied.Passable);
                        tile.Set(TileFlags.Hospital, copied.Hospital);
                        _objects.Remove(new TilePosition(x, y));
                    }
                }

                foreach (var placement in clip.Objects)
                {
                    var (x, y) = (baseX + placement.Tile.X, baseY + placement.Tile.Y);
                    if (x <= maxX && y <= maxY)
                    {
                        PlaceObject(placement with { Tile = new TilePosition(x, y) });
                    }
                }
            }
        }
    }

    /// <summary><c>level_map::save</c>: the map as a map file.</summary>
    public MapFile ToMapFile()
    {
        var tiles = new MapFileTile[MapFile.Size * MapFile.Size];
        for (var y = 0; y < Map.Height; y++)
        {
            for (var x = 0; x < Map.Width; x++)
            {
                var tile = Map[x, y];
                var floor = tile.Floor;
                var northWall = tile.NorthWall;
                if (floor == PondBase && northWall is >= 71 and <= 73)
                {
                    (floor, northWall) = (northWall, 0);
                }

                var flags = 63;
                flags ^= tile.Has(TileFlags.Passable) ? 1 : 0;
                flags ^= tile.Has(TileFlags.Buildable) ? 2 : 0;
                flags ^= tile.Has(TileFlags.BuildableNorth) ? 4 : 0;
                flags ^= tile.Has(TileFlags.BuildableEast) ? 8 : 0;
                flags ^= tile.Has(TileFlags.BuildableSouth) ? 16 : 0;
                flags ^= tile.Has(TileFlags.BuildableWest) ? 32 : 0;

                var placed = _objects.TryGetValue(new TilePosition(x, y), out var placement);
                tiles[(y * MapFile.Size) + x] = new MapFileTile(
                    placed ? placement.Direction : (byte)(tile.Has(TileFlags.TallWest) ? 1 : 0),
                    placed ? placement.ObjectType : (byte)0,
                    MapFile.BlockCode((byte)floor),
                    MapFile.BlockCode((byte)northWall),
                    MapFile.BlockCode((byte)tile.WestWall),
                    (byte)flags,
                    0,
                    (byte)(tile.Has(TileFlags.Hospital) ? 0 : 16),
                    tile.Parcel);
            }
        }

        var players = Enumerable.Range(0, Map.PlayerCount).ToList();
        return MapFile.Create(
            Map.PlayerCount,
            tiles,
            players.Select(player => (Map.GetCameraTile(player).X, Map.GetCameraTile(player).Y)).ToList(),
            players.Select(player => (Map.GetHeliportTile(player).X, Map.GetHeliportTile(player).Y)).ToList());
    }

    /// <summary>Saves the map as an uncompressed map file, such as a CorsixTH <c>.map</c> file.</summary>
    public void Save(string path)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } folder)
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllBytes(path, ToMapFile().ToBytes());
    }
}
