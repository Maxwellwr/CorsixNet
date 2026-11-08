namespace CorsixNet.Sim.Definitions;

/// <summary>Block sprites for the walls of a room type, as in CorsixTH's <c>walls/*.lua</c>.</summary>
/// <param name="InsideNorth">North wall seen from inside the room.</param>
/// <param name="InsideWest">West wall seen from inside the room.</param>
/// <param name="OutsideNorth">North wall of the tile south of the room, seen from outside.</param>
/// <param name="OutsideWest">West wall of the tile east of the room, seen from outside.</param>
public sealed record WallSet(ushort InsideNorth, ushort InsideWest, ushort OutsideNorth, ushort OutsideWest);

/// <summary>A kind of room the player can build, as in CorsixTH's <c>rooms/*.lua</c>.</summary>
/// <param name="Id">Stable identifier used in commands and saves, e.g. "gp".</param>
/// <param name="Name">Name shown to the player.</param>
/// <param name="MinimumSize">Smallest allowed width and height, in tiles.</param>
/// <param name="FloorSprite">Block sprite for the room's floor.</param>
/// <param name="Walls">Wall sprites.</param>
/// <param name="NeededObjects">Objects the room needs before it works, with how many of each.</param>
/// <param name="AdditionalObjects">Further objects that may be placed in the room.</param>
public sealed record RoomDefinition(
    string Id,
    string Name,
    int MinimumSize,
    ushort FloorSprite,
    WallSet Walls,
    IReadOnlyDictionary<string, int>? NeededObjects = null,
    IReadOnlyList<string>? AdditionalObjects = null)
{
    /// <summary>Whether objects of type <paramref name="objectId"/> may be placed in the room.</summary>
    public bool Allows(string objectId)
    {
        return (NeededObjects?.ContainsKey(objectId) ?? false) || (AdditionalObjects?.Contains(objectId, StringComparer.Ordinal) ?? false);
    }
}

/// <summary>
/// The game's content definitions: what the simulation's rules refer to by id. Every machine
/// in a game must use the same content; it is not part of the saved state.
/// </summary>
public sealed class GameContent
{
    public static readonly GameContent Empty = new([], []);

    private readonly Dictionary<string, RoomDefinition> _rooms;
    private readonly Dictionary<string, ObjectDefinition> _objects;
    private readonly HashSet<ushort> _externalWallSprites;

    /// <param name="rooms">Room types the player can build.</param>
    /// <param name="externalWallSprites">Sprites of the hospital's outer walls, which rooms keep rather than replace.</param>
    /// <param name="objects">Object types the player can place.</param>
    public GameContent(IEnumerable<RoomDefinition> rooms, IEnumerable<ushort> externalWallSprites, IEnumerable<ObjectDefinition>? objects = null)
    {
        _rooms = rooms.ToDictionary(room => room.Id, StringComparer.Ordinal);
        _objects = (objects ?? []).ToDictionary(obj => obj.Id, StringComparer.Ordinal);
        _externalWallSprites = [.. externalWallSprites];
        Rooms = [.. _rooms.Values.OrderBy(room => room.Id, StringComparer.Ordinal)];
        Objects = [.. _objects.Values.OrderBy(obj => obj.Id, StringComparer.Ordinal)];
    }

    /// <summary>All object types, ordered by id.</summary>
    public IReadOnlyList<ObjectDefinition> Objects { get; }

    public ObjectDefinition? FindObject(string id) => _objects.GetValueOrDefault(id);

    /// <summary>All room types, ordered by id.</summary>
    public IReadOnlyList<RoomDefinition> Rooms { get; }

    public RoomDefinition? FindRoom(string id) => _rooms.GetValueOrDefault(id);

    public bool IsExternalWall(ushort sprite) => _externalWallSprites.Contains(sprite);
}
