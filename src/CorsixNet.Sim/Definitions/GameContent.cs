using CorsixNet.Sim.Entities;

namespace CorsixNet.Sim.Definitions;

/// <summary>
/// How a kind of humanoid goes through a room door, as CorsixTH's <c>door_animations</c> (<c>entities/humanoid.lua</c>):
/// the humanoid's animation shows the door opening and closing behind them.
/// </summary>
/// <param name="Entering">Going through towards the camera, east or (mirrored) south.</param>
/// <param name="Leaving">Going through away from the camera, north or (mirrored) west.</param>
/// <param name="KnockNorth">Knocking on a door to the north or (mirrored) west (<c>knock_north</c>); patients only.</param>
/// <param name="KnockEast">Knocking on a door to the east or (mirrored) south (<c>knock_east</c>).</param>
public sealed record DoorAnimations(
    AnimationRef Entering,
    AnimationRef Leaving,
    AnimationRef? EnteringSwing = null,
    AnimationRef? LeavingSwing = null,
    AnimationRef? KnockNorth = null,
    AnimationRef? KnockEast = null);

/// <summary>
/// The swing doors' own animations (objects/doors/swing_door_right.lua): the left door (the slave) swings first, the
/// right one (the master) once the humanoid is through; each swings in or out.
/// </summary>
public sealed record SwingDoorAnimations(AnimationRef SlaveIn, AnimationRef SlaveOut, AnimationRef MasterIn, AnimationRef MasterOut);

/// <summary>Block sprites for the walls of a room type, as in CorsixTH's <c>walls/*.lua</c>.</summary>
/// <param name="InsideNorth">North wall seen from inside the room.</param>
/// <param name="InsideWest">West wall seen from inside the room.</param>
/// <param name="OutsideNorth">North wall of the tile south of the room, seen from outside.</param>
/// <param name="OutsideWest">West wall of the tile east of the room, seen from outside.</param>
/// <param name="WindowNorth">North wall with a window, used inside and outside alike; 0 if the room has no windows.</param>
/// <param name="WindowWest">West wall with a window, used inside and outside alike; 0 if the room has no windows.</param>
public sealed record WallSet(ushort InsideNorth, ushort InsideWest, ushort OutsideNorth, ushort OutsideWest, ushort WindowNorth = 0, ushort WindowWest = 0)
{
    /// <summary>
    /// The wall pieces beside swing doors (CorsixTH's <c>swing_inside_tiles</c> and <c>swing_outside_tiles</c>): the
    /// left one behind the left door, the right one beyond the right door, for north and west walls inside and outside.
    /// </summary>
    public SwingWalls? Swing { get; init; }

    /// <summary>Whether the room type allows windows.</summary>
    public bool HasWindows => WindowNorth != 0 && WindowWest != 0;
}

/// <summary>Wall pieces beside swing doors, inside and outside the room, left and right, north and west.</summary>
public sealed record SwingWalls(
    ushort InsideNorthLeft,
    ushort InsideWestLeft,
    ushort InsideNorthRight,
    ushort InsideWestRight,
    ushort OutsideNorthLeft,
    ushort OutsideWestLeft,
    ushort OutsideNorthRight,
    ushort OutsideWestRight);

/// <summary>A kind of room the player can build, as in CorsixTH's <c>rooms/*.lua</c>.</summary>
/// <param name="Id">Stable identifier used in commands and saves, e.g. "gp".</param>
/// <param name="Name">Name shown to the player.</param>
/// <param name="MinimumSize">Smallest allowed width and height, in tiles.</param>
/// <param name="FloorSprite">Block sprite for the room's floor.</param>
/// <param name="Walls">Wall sprites.</param>
/// <param name="NeededObjects">Objects the room needs before it works, with how many of each.</param>
/// <param name="AdditionalObjects">Further objects that may be placed in the room.</param>
/// <param name="LevelConfigId">The room's number in the level files (<c>rooms[n]</c>), CorsixTH's <c>level_config_id</c>; 0 if none.</param>
/// <param name="SwingDoors">Whether the room has a pair of swing doors rather than a door (CorsixTH's <c>swing_doors</c>).</param>
public sealed record RoomDefinition(
    string Id,
    string Name,
    int MinimumSize,
    ushort FloorSprite,
    WallSet Walls,
    IReadOnlyDictionary<string, int>? NeededObjects = null,
    IReadOnlyList<string>? AdditionalObjects = null,
    int LevelConfigId = 0,
    bool SwingDoors = false)
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
/// <summary>
/// What a kind of patient does on the spot (humanoid.lua's <c>vomit_anim</c> and <c>pee_anim</c>); null where it has none.
/// </summary>
public sealed record PatientAnimations(AnimationRef? Vomit, AnimationRef? Pee, AnimationRef? Yawn = null, AnimationRef? TapFoot = null, AnimationRef? CheckWatch = null, AnimationRef? Falling = null, AnimationRef? OnGround = null, AnimationRef? GetUp = null, AnimationRef? ShakeFist = null);

/// <summary>
/// How a kind of patient dies (humanoid.lua's <c>die_anims</c>): the fall, then rising as a ghost, growing wings, putting
/// the hands together and flying up to heaven; <see cref="RiseHell"/> stands up for the trip to hell. <see cref="Extra"/>
/// plays between the fall and the rise (a transformation, or the Chewbacca patient's ending of the fall);
/// <see cref="FallLength"/> cuts the fall short where the original's is broken.
/// </summary>
public sealed record DeathAnimations(AnimationRef Fall, AnimationRef Rise, AnimationRef RiseHell, AnimationRef Wings, AnimationRef Hands, AnimationRef Fly, AnimationRef? Extra = null, int? FallLength = null);

public sealed class GameContent
{
    public static readonly GameContent Empty = new([], []);

    private readonly Dictionary<string, RoomDefinition> _rooms;
    private readonly Dictionary<string, ObjectDefinition> _objects;
    private readonly HashSet<ushort> _externalWallSprites;
    private readonly HashSet<ushort> _windowSprites;
    private readonly Dictionary<HumanoidType, DoorAnimations> _doorAnimations;
    private readonly Dictionary<HumanoidType, PatientAnimations> _patientAnimations;

    /// <param name="rooms">Room types the player can build.</param>
    /// <param name="externalWallSprites">Sprites of the hospital's outer walls, which rooms keep rather than replace.</param>
    /// <param name="objects">Object types the player can place.</param>
    /// <param name="doorAnimations">How each kind of humanoid goes through a room door; the others simply walk through.</param>
    /// <param name="patientAnimations">How each kind of patient vomits and pees; the others do neither.</param>
    public GameContent(
        IEnumerable<RoomDefinition> rooms,
        IEnumerable<ushort> externalWallSprites,
        IEnumerable<ObjectDefinition>? objects = null,
        IReadOnlyDictionary<HumanoidType, DoorAnimations>? doorAnimations = null,
        IReadOnlyDictionary<HumanoidType, PatientAnimations>? patientAnimations = null)
    {
        _patientAnimations = [];
        foreach (var type in Enum.GetValues<HumanoidType>())
        {
            if (patientAnimations?.GetValueOrDefault(type) is { } animations)
            {
                _patientAnimations[type] = animations;
            }
        }

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

    /// <summary>The swing doors' animations, or null when no room has swing doors.</summary>
    public SwingDoorAnimations? SwingDoor { get; init; }

    /// <summary>How each kind of patient dies, by type (humanoid.lua's <c>die_anims</c>).</summary>
    public IReadOnlyDictionary<HumanoidType, DeathAnimations> DeathAnimations { get; init; } = new Dictionary<HumanoidType, DeathAnimations>();

    /// <summary>
    /// How a patient of the type dies: patients undressed or in a gown die as the standard man or woman they are.
    /// </summary>
    public DeathAnimations? FindDeathAnimations(HumanoidType type) => DeathAnimations.GetValueOrDefault(type switch
    {
        HumanoidType.StrippedMalePatient or HumanoidType.StrippedMalePatient2 or HumanoidType.StrippedMalePatient3 or HumanoidType.GownedMalePatient => HumanoidType.StandardMalePatient,
        HumanoidType.StrippedFemalePatient or HumanoidType.StrippedFemalePatient2 or HumanoidType.StrippedFemalePatient3 or HumanoidType.GownedFemalePatient => HumanoidType.StandardFemalePatient,
        _ => type,
    });

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

    /// <summary>How patients of <paramref name="type"/> vomit and pee, or null if they do neither.</summary>
    public PatientAnimations? FindPatientAnimations(HumanoidType type) => _patientAnimations.GetValueOrDefault(type);
}
