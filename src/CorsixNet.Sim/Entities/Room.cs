using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Entities;

/// <summary>A room built by a player. Its tiles carry its id in <see cref="MapTile.RoomId"/>.</summary>
public sealed class Room : Entity
{
    public Room(int id, string roomType, TileRect area)
        : base(id)
    {
        RoomType = roomType;
        Area = area;
    }

    public override EntityKind Kind => EntityKind.Room;

    /// <summary>Id of the room's <see cref="Definitions.RoomDefinition"/>.</summary>
    public string RoomType { get; }

    public TileRect Area { get; }

    /// <summary>Entity id of the room's door.</summary>
    public int DoorId { get; internal set; }

    public override void Update(Simulation simulation)
    {
    }

    internal static Room ReadState(int id, SimReader reader)
    {
        var roomType = reader.ReadString();
        var area = new TileRect(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        return new Room(id, roomType, area) { DoorId = reader.ReadInt32() };
    }

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteString(RoomType);
        writer.WriteInt32(Area.X);
        writer.WriteInt32(Area.Y);
        writer.WriteInt32(Area.Width);
        writer.WriteInt32(Area.Height);
        writer.WriteInt32(DoorId);
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
