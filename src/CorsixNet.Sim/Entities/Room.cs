using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Entities;

/// <summary>A room tile as it was before the room was built, so that cancelling the room can restore it.</summary>
/// <param name="Floor">The floor sprite.</param>
/// <param name="Flags">The flags in <see cref="Room.RestorableFlags"/>.</param>
public readonly record struct OriginalTile(ushort Floor, TileFlags Flags);

/// <summary>A room built by a player. Its tiles carry its id in <see cref="MapTile.RoomId"/>.</summary>
/// <remarks>
/// A new room is inactive, like CorsixTH's rooms while the edit dialog is open: its owner furnishes
/// it, and confirms it once the needed objects stand in it. Only then does it work.
/// </remarks>
public sealed class Room : Entity
{
    /// <summary>Tile flags that objects change and that cancelling a room puts back.</summary>
    public const TileFlags RestorableFlags = TileFlags.Passable | TileFlags.Buildable
        | TileFlags.BuildableNorth | TileFlags.BuildableEast | TileFlags.BuildableSouth | TileFlags.BuildableWest;

    /// <summary>Largest width or height accepted when reading a room.</summary>
    private const int MaxSide = 1024;

    public Room(int id, string roomType, TileRect area, IReadOnlyList<OriginalTile> originalTiles)
        : base(id)
    {
        if (originalTiles.Count != area.Width * area.Height)
        {
            throw new ArgumentException("There must be one original tile per tile of the area.", nameof(originalTiles));
        }

        RoomType = roomType;
        Area = area;
        OriginalTiles = originalTiles;
    }

    public override EntityKind Kind => EntityKind.Room;

    /// <summary>Id of the room's <see cref="Definitions.RoomDefinition"/>.</summary>
    public string RoomType { get; }

    public TileRect Area { get; }

    /// <summary>Entity id of the room's door.</summary>
    public int DoorId { get; internal set; }

    /// <summary>Whether the room has been confirmed and works; until then its owner may furnish or cancel it.</summary>
    public bool IsActive { get; internal set; }

    /// <summary>The area's tiles before the room was built, row by row.</summary>
    public IReadOnlyList<OriginalTile> OriginalTiles { get; }

    /// <summary>Whether the tile outside the door could be built on before the door kept it clear.</summary>
    public bool DoorOutsideWasBuildable { get; internal set; }

    /// <summary>The original state of <paramref name="tile"/>, which must lie in the area.</summary>
    public OriginalTile OriginalTileAt(TilePosition tile) => OriginalTiles[((tile.Y - Area.Y) * Area.Width) + (tile.X - Area.X)];

    public override void Update(Simulation simulation)
    {
    }

    internal static Room ReadState(int id, SimReader reader)
    {
        var roomType = reader.ReadString();
        var area = new TileRect(reader.ReadInt32(), reader.ReadInt32(), reader.ReadCount(MaxSide), reader.ReadCount(MaxSide));
        var doorId = reader.ReadInt32();
        var active = reader.ReadBool();
        var outsideBuildable = reader.ReadBool();
        var tiles = new OriginalTile[area.Width * area.Height];
        for (var i = 0; i < tiles.Length; i++)
        {
            tiles[i] = new OriginalTile(reader.ReadUInt16(), (TileFlags)reader.ReadUInt32());
        }

        return new Room(id, roomType, area, tiles) { DoorId = doorId, IsActive = active, DoorOutsideWasBuildable = outsideBuildable };
    }

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteString(RoomType);
        writer.WriteInt32(Area.X);
        writer.WriteInt32(Area.Y);
        writer.WriteInt32(Area.Width);
        writer.WriteInt32(Area.Height);
        writer.WriteInt32(DoorId);
        writer.WriteBool(IsActive);
        writer.WriteBool(DoorOutsideWasBuildable);
        foreach (var tile in OriginalTiles)
        {
            writer.WriteUInt16(tile.Floor);
            writer.WriteUInt32((uint)tile.Flags);
        }
    }
}

/// <summary>An object placed by a player, such as a desk; standing in a room or, for corridor objects, outside one.</summary>
public sealed class RoomObject : Entity
{
    public RoomObject(int id, string objectType, TilePosition tile, Direction orientation, int roomId)
        : base(id)
    {
        ObjectType = objectType;
        Tile = tile;
        Orientation = orientation;
        RoomId = roomId;
    }

    public override EntityKind Kind => EntityKind.RoomObject;

    /// <summary>Id of the object's <see cref="Definitions.ObjectDefinition"/>.</summary>
    public string ObjectType { get; }

    /// <summary>The tile the object was placed on; its footprint is relative to this tile.</summary>
    public TilePosition Tile { get; }

    public Direction Orientation { get; }

    /// <summary>Entity id of the room the object stands in, or 0 in a corridor.</summary>
    public int RoomId { get; }

    public override void Update(Simulation simulation)
    {
    }

    internal static RoomObject ReadState(int id, SimReader reader)
    {
        return new RoomObject(id, reader.ReadString(), reader.ReadTile(), (Direction)reader.ReadByte(), reader.ReadInt32());
    }

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteString(ObjectType);
        writer.WriteTile(Tile);
        writer.WriteByte((byte)Orientation);
        writer.WriteInt32(RoomId);
    }
}

/// <summary>The door of a room, standing in a gap of the room's wall.</summary>
public sealed class RoomDoor : Entity
{
    public RoomDoor(int id, TilePosition tile, EdgeOrientation edge, int roomId)
        : base(id)
    {
        Tile = tile;
        Edge = edge;
        RoomId = roomId;
    }

    public override EntityKind Kind => EntityKind.RoomDoor;

    public TilePosition Tile { get; }

    public EdgeOrientation Edge { get; }

    public int RoomId { get; }

    public override void Update(Simulation simulation)
    {
    }

    /// <summary>The tile on the far side of the door's edge from <see cref="Tile"/>.</summary>
    public TilePosition OtherSide => Edge == EdgeOrientation.North ? Tile with { Y = Tile.Y - 1 } : Tile with { X = Tile.X - 1 };

    /// <summary>Of the two tiles the door connects, the one inside <paramref name="area"/>.</summary>
    public TilePosition InsideTile(TileRect area) => area.Contains(Tile) ? Tile : OtherSide;

    /// <summary>
    /// Opens the wall at the door's edge, marks the edge as a tall door edge and keeps the tiles on
    /// both sides clear of objects, as CorsixTH's <c>Door:setTile</c> does.
    /// </summary>
    internal void Place(LevelMap map)
    {
        ref var tile = ref map[Tile.X, Tile.Y];
        if (Edge == EdgeOrientation.North)
        {
            tile.NorthWall = 0;
            tile.Set(TileFlags.DoorNorth | TileFlags.TallNorth, true);
        }
        else
        {
            tile.WestWall = 0;
            tile.Set(TileFlags.DoorWest | TileFlags.TallWest, true);
        }

        tile.Set(TileFlags.Buildable, false);
        if (map.Contains(OtherSide.X, OtherSide.Y))
        {
            map[OtherSide.X, OtherSide.Y].Set(TileFlags.Buildable, false);
        }
    }

    /// <summary>Takes the door out of its edge again; the wall itself is left to whoever removes the room.</summary>
    internal void Unplace(LevelMap map, bool outsideWasBuildable, TileRect area)
    {
        ref var tile = ref map[Tile.X, Tile.Y];
        tile.Set(Edge == EdgeOrientation.North ? TileFlags.DoorNorth | TileFlags.TallNorth : TileFlags.DoorWest | TileFlags.TallWest, false);
        var outside = area.Contains(Tile) ? OtherSide : Tile;
        if (map.Contains(outside.X, outside.Y))
        {
            map[outside.X, outside.Y].Set(TileFlags.Buildable, outsideWasBuildable);
        }
    }

    internal static RoomDoor ReadState(int id, SimReader reader)
    {
        return new RoomDoor(id, reader.ReadTile(), (EdgeOrientation)reader.ReadByte(), reader.ReadInt32());
    }

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteTile(Tile);
        writer.WriteByte((byte)Edge);
        writer.WriteInt32(RoomId);
    }
}
