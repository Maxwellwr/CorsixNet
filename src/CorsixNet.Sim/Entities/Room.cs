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

    /// <summary>Opens the wall at the door's edge and marks the edge as a tall door edge, as CorsixTH's <c>Door:setTile</c> does.</summary>
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
