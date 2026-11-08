using CorsixNet.Sim.Entities;

namespace CorsixNet.Sim.Definitions;

/// <summary>
/// How a kind of humanoid goes through a room door, as CorsixTH's <c>door_animations</c> (<c>entities/humanoid.lua</c>):
/// the humanoid's animation shows the door opening and closing behind them.
/// </summary>
/// <param name="Entering">Going through towards the camera, east or (mirrored) south.</param>
/// <param name="Leaving">Going through away from the camera, north or (mirrored) west.</param>
public sealed record DoorAnimations(AnimationRef Entering, AnimationRef Leaving);

/// <summary>Block sprites for the walls of a room type, as in CorsixTH's <c>walls/*.lua</c>.</summary>
/// <param name="InsideNorth">North wall seen from inside the room.</param>
/// <param name="InsideWest">West wall seen from inside the room.</param>
/// <param name="OutsideNorth">North wall of the tile south of the room, seen from outside.</param>
/// <param name="OutsideWest">West wall of the tile east of the room, seen from outside.</param>
/// <param name="WindowNorth">North wall with a window, used inside and outside alike; 0 if the room has no windows.</param>
/// <param name="WindowWest">West wall with a window, used inside and outside alike; 0 if the room has no windows.</param>
public sealed record WallSet(ushort InsideNorth, ushort InsideWest, ushort OutsideNorth, ushort OutsideWest, ushort WindowNorth = 0, ushort WindowWest = 0)
{
    /// <summary>Whether the room type allows windows.</summary>
    public bool HasWindows => WindowNorth != 0 && WindowWest != 0;
}

/// <summary>A kind of room the player can build, as in CorsixTH's <c>rooms/*.lua</c>.</summary>
/// <param name="Id">Stable identifier used in commands and saves, e.g. "gp".</param>
/// <param name="Name">Name shown to the player.</param>
/// <param name="MinimumSize">Smallest allowed width and height, in tiles.</param>
/// <param name="FloorSprite">Block sprite for the room's floor.</param>
/// <param name="Walls">Wall sprites.</param>
/// <param name="NeededObjects">Objects the room needs before it works, with how many of each.</param>
/// <param name="AdditionalObjects">Further objects that may be placed in the room.</param>
/// <param name="LevelConfigId">The room's number in the level files (<c>rooms[n]</c>), CorsixTH's <c>level_config_id</c>; 0 if none.</param>
public sealed record RoomDefinition(
    string Id,
    string Name,
    int MinimumSize,
    ushort FloorSprite,
    WallSet Walls,
    IReadOnlyDictionary<string, int>? NeededObjects = null,
    IReadOnlyList<string>? AdditionalObjects = null,
    int LevelConfigId = 0)
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
    private readonly HashSet<ushort> _windowSprites;
    private readonly Dictionary<HumanoidType, DoorAnimations> _doorAnimations;

    /// <param name="rooms">Room types the player can build.</param>
    /// <param name="externalWallSprites">Sprites of the hospital's outer walls, which rooms keep rather than replace.</param>
    /// <param name="objects">Object types the player can place.</param>
    /// <param name="doorAnimations">How each kind of humanoid goes through a room door; the others simply walk through.</param>
    public GameContent(
        IEnumerable<RoomDefinition> rooms,
        IEnumerable<ushort> externalWallSprites,
        IEnumerable<ObjectDefinition>? objects = null,
        IReadOnlyDictionary<HumanoidType, DoorAnimations>? doorAnimations = null)
    {
        _doorAnimations = [];
        foreach (var type in Enum.GetValues<HumanoidType>())
        {
            if (doorAnimations?.GetValueOrDefault(type) is { } animations)
            {
                _doorAnimations[type] = animations;
            }
        }

        _rooms = rooms.ToDictionary(room => room.Id, StringComparer.Ordinal);
        _objects = (objects ?? []).ToDictionary(obj => obj.Id, StringComparer.Ordinal);
        _externalWallSprites = [.. externalWallSprites];
        Rooms = [.. _rooms.Values.OrderBy(room => room.Id, StringComparer.Ordinal)];
        Objects = [.. _objects.Values.OrderBy(obj => obj.Id, StringComparer.Ordinal)];
        _windowSprites = [.. Rooms.SelectMany(room => new[] { room.Walls.WindowNorth, room.Walls.WindowWest }).Where(sprite => sprite != 0)];
    }

    /// <summary>All object types, ordered by id.</summary>
    public IReadOnlyList<ObjectDefinition> Objects { get; }

    public ObjectDefinition? FindObject(string id) => _objects.GetValueOrDefault(id);

    /// <summary>All room types, ordered by id.</summary>
    public IReadOnlyList<RoomDefinition> Rooms { get; }

    public RoomDefinition? FindRoom(string id) => _rooms.GetValueOrDefault(id);

    public bool IsExternalWall(ushort sprite) => _externalWallSprites.Contains(sprite);

    /// <summary>Whether <paramref name="sprite"/> is a room window of any room type.</summary>
    public bool IsWindowWall(ushort sprite) => _windowSprites.Contains(sprite);

    /// <summary>How humanoids of <paramref name="type"/> go through a room door, or null if they have no door animations.</summary>
    public DoorAnimations? FindDoorAnimations(HumanoidType type) => _doorAnimations.GetValueOrDefault(type);
}
