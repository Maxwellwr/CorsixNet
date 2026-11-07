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
public sealed record RoomDefinition(string Id, string Name, int MinimumSize, ushort FloorSprite, WallSet Walls);

/// <summary>
/// The game's content definitions: what the simulation's rules refer to by id. Every machine
/// in a game must use the same content; it is not part of the saved state.
/// </summary>
public sealed class GameContent
{
    public static readonly GameContent Empty = new([], []);

    private readonly Dictionary<string, RoomDefinition> _rooms;
    private readonly HashSet<ushort> _externalWallSprites;

    /// <param name="rooms">Room types the player can build.</param>
    /// <param name="externalWallSprites">Sprites of the hospital's outer walls, which rooms keep rather than replace.</param>
    public GameContent(IEnumerable<RoomDefinition> rooms, IEnumerable<ushort> externalWallSprites)
    {
        _rooms = rooms.ToDictionary(room => room.Id, StringComparer.Ordinal);
        _externalWallSprites = [.. externalWallSprites];
        Rooms = [.. _rooms.Values.OrderBy(room => room.Id, StringComparer.Ordinal)];
    }

    /// <summary>All room types, ordered by id.</summary>
    public IReadOnlyList<RoomDefinition> Rooms { get; }

    public RoomDefinition? FindRoom(string id) => _rooms.GetValueOrDefault(id);

    public bool IsExternalWall(ushort sprite) => _externalWallSprites.Contains(sprite);
}
