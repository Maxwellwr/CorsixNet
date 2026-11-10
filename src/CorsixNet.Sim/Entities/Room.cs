using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.People;
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

    /// <summary>
    /// Whether the room is emptying to be edited (CorsixTH's <c>Room:tryToEdit</c>): it takes nobody new, its queue goes
    /// elsewhere and its people leave; once empty it can be furnished and confirmed again, or taken down.
    /// </summary>
    public bool IsClosing { get; internal set; }

    /// <summary>
    /// Whether a machine exploded in the room (CorsixTH's <c>Room.crashed</c>): it is a sooty wreck that takes nobody and
    /// can only be taken down.
    /// </summary>
    public bool IsCrashed { get; internal set; }

    /// <summary>Whether the room has been paid for, once first confirmed (CorsixTH's <c>UIEditRoom.paid</c>).</summary>
    public bool IsPaid { get; internal set; }

    /// <summary>The area's tiles before the room was built, row by row.</summary>
    public IReadOnlyList<OriginalTile> OriginalTiles { get; }

    /// <summary>Whether the tile outside the door could be built on before the door kept it clear.</summary>
    public bool DoorOutsideWasBuildable { get; internal set; }

    /// <summary>The original state of <paramref name="tile"/>, which must lie in the area.</summary>
    public OriginalTile OriginalTileAt(TilePosition tile) => OriginalTiles[((tile.Y - Area.Y) * Area.Width) + (tile.X - Area.X)];

    /// <summary>Patients waiting at the door; created when the room is confirmed.</summary>
    public HumanoidQueue? DoorQueue { get; internal set; }

    /// <summary>The member of staff in the room, or on the way to it; 0 for none.</summary>
    public int StaffId { get; internal set; }

    /// <summary>Whether the member of staff has come in (CorsixTH counts staff in the room for its staff criteria).</summary>
    public bool StaffPresent { get; internal set; }

    /// <summary>The patient in the room, or let in from the queue and on the way; 0 for none.</summary>
    public int PatientId { get; internal set; }

    /// <summary>
    /// How many more times the doctor may fail to conclude the consultation before the patient is dealt with
    /// anyway (CorsixTH's <c>GPRoom.max_times</c>).
    /// </summary>
    public int MaxTimes { get; internal set; }

    public override void Update(Simulation simulation)
    {
        if (IsActive)
        {
            RoomVisits.Update(simulation, this);
        }
        else if (IsClosing)
        {
            RoomVisits.UpdateClosing(simulation, this);
        }
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

        return new Room(id, roomType, area, tiles)
        {
            DoorId = doorId,
            IsActive = active,
            DoorOutsideWasBuildable = outsideBuildable,
            DoorQueue = reader.ReadBool() ? HumanoidQueue.Read(reader) : null,
            StaffId = reader.ReadInt32(),
            StaffPresent = reader.ReadBool(),
            PatientId = reader.ReadInt32(),
            MaxTimes = reader.ReadInt32(),
            IsPaid = reader.Version >= 59 ? reader.ReadBool() : active,
            IsClosing = reader.Version >= 59 && reader.ReadBool(),
            IsCrashed = reader.Version is 0 or >= 68 && reader.ReadBool(),
        };
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

        writer.WriteBool(DoorQueue is not null);
        DoorQueue?.Write(writer);
        writer.WriteInt32(StaffId);
        writer.WriteBool(StaffPresent);
        writer.WriteInt32(PatientId);
        writer.WriteInt32(MaxTimes);
        writer.WriteBool(IsPaid);
        writer.WriteBool(IsClosing);
        writer.WriteBool(IsCrashed);
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

    /// <summary>The reception desk's receptionist and queue, if the object is a reception desk.</summary>
    public ReceptionDeskState? Desk { get; internal set; }

    /// <summary>
    /// The clothes (a patient's layer 1) hanging on a screen while the patient is undressed, or null; the screen is then
    /// drawn with them (CorsixTH's <c>action_use_screen_patient_clothes_state</c>).
    /// </summary>
    public byte? HangingClothes { get; internal set; }

    /// <summary>For a plant, how thirsty it is; null for other objects.</summary>
    public PlantState? Plant { get; internal set; }

    /// <summary>Whether the object was destroyed (a crashed room's furniture) and goes at the end of the tick.</summary>
    public bool IsDestroyed { get; internal set; }

    /// <summary>For a machine, how worn it is; null for other objects.</summary>
    public Objects.MachineState? Machine { get; internal set; }

    public override void Update(Simulation simulation)
    {
        if (Desk is not null)
        {
            ReceptionDesks.Update(simulation, this);
        }
    }

    internal static RoomObject ReadState(int id, SimReader reader)
    {
        return new RoomObject(id, reader.ReadString(), reader.ReadTile(), (Direction)reader.ReadByte(), reader.ReadInt32())
        {
            Desk = reader.ReadBool() ? ReceptionDeskState.Read(reader) : null,
            HangingClothes = reader.ReadBool() ? reader.ReadByte() : null,
            Plant = reader.ReadBool() ? PlantState.Read(reader) : null,
            Machine = reader.Version is 0 or >= 68 && reader.ReadBool() ? Objects.MachineState.Read(reader) : null,
        };
    }

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteString(ObjectType);
        writer.WriteTile(Tile);
        writer.WriteByte((byte)Orientation);
        writer.WriteInt32(RoomId);
        writer.WriteBool(Desk is not null);
        Desk?.Write(writer);
        writer.WriteBool(HangingClothes is not null);
        if (HangingClothes is { } clothes)
        {
            writer.WriteByte(clothes);
        }

        writer.WriteBool(Plant is not null);
        Plant?.Write(writer);
        writer.WriteBool(Machine is not null);
        Machine?.Write(writer);
    }
}

/// <summary>The door of a room, standing in a gap of the room's wall.</summary>
public sealed class RoomDoor : Entity
{
    public RoomDoor(int id, TilePosition tile, EdgeOrientation edge, int roomId, bool isSwing = false)
        : base(id)
    {
        Tile = tile;
        Edge = edge;
        RoomId = roomId;
        IsSwing = isSwing;
    }

    /// <summary>
    /// Whether this is a pair of swing doors (objects/doors/swing_door_right.lua): <see cref="Tile"/> is the right door,
    /// the one people walk through, and <see cref="SlaveTile"/> the left one beside it.
    /// </summary>
    public bool IsSwing { get; }

    /// <summary>The left swing door's tile: west of the right door in a north wall, north of it in a west wall.</summary>
    public TilePosition SlaveTile => Edge == EdgeOrientation.North ? Tile with { X = Tile.X - 1 } : Tile with { Y = Tile.Y - 1 };

    /// <summary>The tile beyond the right swing door, whose wall gets the right-hand swing piece.</summary>
    public TilePosition BeyondTile => Edge == EdgeOrientation.North ? Tile with { X = Tile.X + 1 } : Tile with { Y = Tile.Y + 1 };

    /// <summary>The tick the swing doors last started swinging, or −1.</summary>
    public long SwingStart { get; private set; } = -1;

    /// <summary>Whether they last swung in (someone walking north or west), else out.</summary>
    public bool SwingIn { get; private set; }

    /// <summary>How long the humanoid took through them, after which the right door swings (<c>swingDoors</c>' length).</summary>
    public int SwingCrossTicks { get; private set; }

    /// <summary><c>SwingDoor:swingDoors</c>: the left door swings at once, the right one once the humanoid is through.</summary>
    internal void StartSwing(long tick, bool swingIn, int crossTicks)
    {
        SwingStart = tick;
        SwingIn = swingIn;
        SwingCrossTicks = crossTicks;
    }

    /// <summary>
    /// Whether the swing doors are still swinging: until the right door's swing ends nobody else goes through
    /// (<c>SwingDoor:swing</c> removes the user then).
    /// </summary>
    public bool IsSwingBusy(long tick, SwingDoorAnimations? animations)
    {
        if (!IsSwing || SwingStart < 0 || animations is null)
        {
            return false;
        }

        var master = SwingIn ? animations.MasterIn : animations.MasterOut;
        return tick < SwingStart + SwingCrossTicks + master.Length;
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
        tile.Set(TileFlags.DoNotIdle, true);
        if (map.Contains(OtherSide.X, OtherSide.Y))
        {
            map[OtherSide.X, OtherSide.Y].Set(TileFlags.Buildable, false);
            map[OtherSide.X, OtherSide.Y].Set(TileFlags.DoNotIdle, true);
        }

        if (IsSwing)
        {
            // SwingDoor:getWalkableTiles: the three tiles on each side of the pair stay clear.
            foreach (var walkable in SwingWalkableTiles())
            {
                if (map.Contains(walkable.X, walkable.Y))
                {
                    map[walkable.X, walkable.Y].Set(TileFlags.Buildable, false);
                    map[walkable.X, walkable.Y].Set(TileFlags.DoNotIdle, true);
                }
            }
        }
    }

    /// <summary>The tiles beside the swing doors on both sides of the wall (<c>SwingDoor:getWalkableTiles</c>).</summary>
    private IEnumerable<TilePosition> SwingWalkableTiles()
    {
        var (x, y) = (Tile.X, Tile.Y);
        for (var i = -1; i <= 1; i++)
        {
            yield return Edge == EdgeOrientation.North ? new TilePosition(x + i, y - 1) : new TilePosition(x - 1, y + i);
            yield return Edge == EdgeOrientation.North ? new TilePosition(x + i, y) : new TilePosition(x, y + i);
        }
    }

    /// <summary>Takes the door out of its edge again; the wall itself is left to whoever removes the room.</summary>
    internal void Unplace(LevelMap map, bool outsideWasBuildable, TileRect area)
    {
        ref var tile = ref map[Tile.X, Tile.Y];
        tile.Set(Edge == EdgeOrientation.North ? TileFlags.DoorNorth | TileFlags.TallNorth : TileFlags.DoorWest | TileFlags.TallWest, false);
        tile.Set(TileFlags.DoNotIdle, false);
        if (map.Contains(OtherSide.X, OtherSide.Y))
        {
            map[OtherSide.X, OtherSide.Y].Set(TileFlags.DoNotIdle, false);
        }

        var outside = area.Contains(Tile) ? OtherSide : Tile;
        if (map.Contains(outside.X, outside.Y))
        {
            map[outside.X, outside.Y].Set(TileFlags.Buildable, outsideWasBuildable);
        }

        if (IsSwing)
        {
            foreach (var walkable in SwingWalkableTiles())
            {
                if (map.Contains(walkable.X, walkable.Y))
                {
                    map[walkable.X, walkable.Y].Set(TileFlags.DoNotIdle, false);
                    if (!area.Contains(walkable))
                    {
                        map[walkable.X, walkable.Y].Set(TileFlags.Buildable, outsideWasBuildable);
                    }
                }
            }
        }
    }

    internal static RoomDoor ReadState(int id, SimReader reader)
    {
        var door = new RoomDoor(id, reader.ReadTile(), (EdgeOrientation)reader.ReadByte(), reader.ReadInt32(), reader.Version is 0 or >= 62 && reader.ReadBool());
        if (door.IsSwing)
        {
            door.SwingStart = reader.ReadInt64();
            door.SwingIn = reader.ReadBool();
            door.SwingCrossTicks = reader.ReadInt32();
        }

        return door;
    }

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteTile(Tile);
        writer.WriteByte((byte)Edge);
        writer.WriteInt32(RoomId);
        writer.WriteBool(IsSwing);
        if (IsSwing)
        {
            writer.WriteInt64(SwingStart);
            writer.WriteBool(SwingIn);
            writer.WriteInt32(SwingCrossTicks);
        }
    }
}
