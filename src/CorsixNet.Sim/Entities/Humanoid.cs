using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Serialization;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Sim.Entities;

/// <summary>A compass direction on the map grid.</summary>
public enum Direction : byte
{
    /// <summary>Towards smaller y (up and right on screen).</summary>
    North,

    /// <summary>Towards larger x (down and right on screen).</summary>
    East,

    /// <summary>Towards larger y (down and left on screen).</summary>
    South,

    /// <summary>Towards smaller x (up and left on screen).</summary>
    West,
}

/// <summary>Kind of person, which determines the animations used to draw them.</summary>
public enum HumanoidType : byte
{
    StandardMalePatient = 1,
    StandardFemalePatient = 2,
    Doctor = 3,
    Nurse = 4,
    Handyman = 5,
    Receptionist = 6,
}

/// <summary>A person in the world: for now, one who can walk along a path.</summary>
/// <remarks>
/// Walking follows CorsixTH's timing: one tile takes <see cref="TicksPerStep"/> ticks.
/// During a step <see cref="Tile"/> is still the tile being left; it changes when the step completes.
/// </remarks>
public sealed class Humanoid : Entity
{
    /// <summary>Ticks to walk from one tile to the next at normal speed.</summary>
    public const int TicksPerStep = 8;

    /// <summary>
    /// Number of appearance layers a humanoid sets: 0 to 4 for patients (head, clothes, shoes and two
    /// more) and 5 for staff (skin colour of doctors).
    /// </summary>
    public const int LayerCount = 6;

    /// <summary>The animation layer that <see cref="StaffProfile.Layer5"/> sets.</summary>
    public const int StaffLayer = 5;

    private readonly List<TilePosition> _path = [];
    private readonly byte[] _layers = new byte[LayerCount];

    public Humanoid(int id, TilePosition tile, HumanoidType type = HumanoidType.StandardMalePatient, ReadOnlySpan<byte> layers = default)
        : base(id)
    {
        Tile = tile;
        Type = type;
        Facing = Direction.South;
        layers[..Math.Min(layers.Length, LayerCount)].CopyTo(_layers);
    }

    public HumanoidType Type { get; }

    /// <summary>The member of staff's profile, or null for anyone who is not staff.</summary>
    public StaffProfile? Profile { get; private init; }

    /// <summary>The player (0-based) whose hospital employs the member of staff, or −1.</summary>
    public int Employer { get; private init; } = -1;

    /// <summary>The player (0-based) whose hospital the humanoid works at or visits, or −1.</summary>
    public int HospitalPlayer { get; private init; } = -1;

    /// <summary>What the humanoid is doing: a patient's visit or a member of staff's job. Null for none.</summary>
    public HumanoidBehaviour? Behaviour { get; internal set; }

    /// <summary>
    /// The animation the humanoid plays while using an object, or null while walking or standing;
    /// the client then draws it from <see cref="ActionAnimationTile"/>.
    /// </summary>
    public AnimationRef? ActionAnimation { get; private set; }

    /// <summary>Whether <see cref="ActionAnimation"/> is drawn mirrored.</summary>
    public bool ActionAnimationFlip { get; private set; }

    /// <summary>The tick <see cref="ActionAnimation"/> started on.</summary>
    public long ActionAnimationStart { get; private set; }

    /// <summary>The tile <see cref="ActionAnimation"/> is drawn from.</summary>
    public TilePosition ActionAnimationTile { get; private set; }

    /// <summary>The tile whose drawing pass draws <see cref="ActionAnimation"/>.</summary>
    public TilePosition ActionDrawTile { get; private set; }

    /// <summary>
    /// The object the humanoid is using, which is not drawn on its own meanwhile: the use animation
    /// shows it (CorsixTH's <c>makeInvisible</c>). 0 for none.
    /// </summary>
    public int HiddenObjectId { get; private set; }

    /// <summary>
    /// Whether the humanoid is not drawn, because someone else's animation shows them (CorsixTH's
    /// <c>makeInvisible</c> on the other user of a multi-use object).
    /// </summary>
    public bool IsHidden { get; internal set; }

    /// <summary>Whether the humanoid has left the map and is about to be removed.</summary>
    public bool IsDespawning { get; internal set; }

    /// <summary>Whether the current walk may cross impassable tiles, as CorsixTH's <c>HumanoidRawWalk</c>.</summary>
    private bool RawWalk { get; set; }

    /// <summary>Whether the humanoid is walking or about to take another step.</summary>
    public bool IsMoving => IsStepping || _path.Count > 0;

    /// <summary>
    /// Variant chosen for each animation layer: which head, clothes and so on are drawn.
    /// </summary>
    public IReadOnlyList<byte> Layers => _layers;

    public override EntityKind Kind => EntityKind.Humanoid;

    /// <summary>The tile the humanoid stands on, or is leaving while walking.</summary>
    public TilePosition Tile { get; private set; }

    /// <summary>The direction of the current or last step.</summary>
    public Direction Facing { get; private set; }

    /// <summary>Whether a step between two tiles is in progress.</summary>
    public bool IsStepping { get; private set; }

    /// <summary>Ticks elapsed in the current step, from 0 to <see cref="TicksPerStep"/> − 1.</summary>
    public int StepProgress { get; private set; }

    /// <summary>The tile the current step leads to; equals <see cref="Tile"/> when not stepping.</summary>
    public TilePosition StepTarget => IsStepping ? Neighbour(Tile, Facing) : Tile;

    /// <summary>Tiles still to walk to after the current step, in order.</summary>
    public IReadOnlyList<TilePosition> RemainingPath => _path;

    /// <summary>The final tile of the walk, or null when not walking anywhere.</summary>
    public TilePosition? Destination => _path.Count > 0 ? _path[^1] : IsStepping ? StepTarget : null;

    /// <summary>
    /// Plans a walk to <paramref name="target"/>, continuing from the end of any step in progress.
    /// </summary>
    /// <returns>False if the target cannot be reached; the humanoid then stops after its current step.</returns>
    public bool WalkTo(Simulation simulation, TilePosition target)
    {
        var path = new List<TilePosition>();
        var found = simulation.Pathfinder.TryFindPath(simulation.Map, StepTarget, target, path);
        return FollowPath(path, found);
    }

    /// <summary>Walks along <paramref name="path"/>, whose first tile is where the humanoid is or is stepping to.</summary>
    internal bool FollowPath(IReadOnlyList<TilePosition> path, bool found = true)
    {
        _path.Clear();
        RawWalk = false;
        ClearActionAnimation();
        if (found)
        {
            for (var i = 1; i < path.Count; i++)
            {
                _path.Add(path[i]);
            }
        }

        return found;
    }

    /// <summary>
    /// Takes one step to a neighbouring tile whether or not it is passable, as CorsixTH's
    /// <c>HumanoidRawWalk</c>: onto a chair, or behind the reception desk.
    /// </summary>
    internal void RawStepTo(TilePosition next)
    {
        _path.Clear();
        _path.Add(next);
        RawWalk = true;
        ClearActionAnimation();
    }

    /// <summary>Stops walking after the current step.</summary>
    internal void StopWalking() => _path.Clear();

    /// <summary>Turns to face <paramref name="direction"/> while standing.</summary>
    internal void Face(Direction direction) => Facing = direction;

    /// <summary>Turns towards a neighbouring (or any) tile, as CorsixTH's queue action picks an idle direction.</summary>
    internal void FaceTowards(TilePosition tile)
    {
        if (tile.Y < Tile.Y)
        {
            Facing = Direction.North;
        }
        else if (tile.Y > Tile.Y)
        {
            Facing = Direction.South;
        }
        else if (tile.X > Tile.X)
        {
            Facing = Direction.East;
        }
        else if (tile.X < Tile.X)
        {
            Facing = Direction.West;
        }
    }

    /// <summary>Puts the humanoid on a tile without walking, as CorsixTH's <c>setTilePositionSpeed</c>.</summary>
    internal void PlaceAt(TilePosition tile)
    {
        _path.Clear();
        IsStepping = false;
        StepProgress = 0;
        Tile = tile;
    }

    /// <summary>Starts an animation of using an object.</summary>
    internal void SetActionAnimation(Simulation simulation, AnimationRef animation, bool flip, TilePosition tile, TilePosition drawTile, int hiddenObject)
    {
        ActionAnimation = animation;
        ActionAnimationFlip = flip;
        ActionAnimationStart = simulation.Tick;
        ActionAnimationTile = tile;
        ActionDrawTile = drawTile;
        HiddenObjectId = hiddenObject;
    }

    /// <summary>Sets the variant of one animation layer (CorsixTH's <c>setLayer</c>).</summary>
    internal void SetLayer(int layer, byte value) => _layers[layer] = value;

    /// <summary>Goes back to walking and standing animations.</summary>
    internal void ClearActionAnimation()
    {
        ActionAnimation = null;
        HiddenObjectId = 0;
    }

    public override void Update(Simulation simulation)
    {
        if (IsStepping)
        {
            StepProgress++;
            if (StepProgress < TicksPerStep)
            {
                return;
            }

            Tile = Neighbour(Tile, Facing);
            IsStepping = false;
            StepProgress = 0;
        }

        if (_path.Count == 0)
        {
            Behaviour?.Update(simulation, this);
            return;
        }

        var next = _path[0];
        if (RawWalk && DirectionTo(Tile, next) is { } rawDirection)
        {
            _path.RemoveAt(0);
            Facing = rawDirection;
            IsStepping = true;
            StepProgress = 0;
            return;
        }

        if (DirectionTo(Tile, next) is not { } direction || !CanTravel(simulation.Map, Tile, direction))
        {
            // The map changed under the planned path; plan again towards the same destination.
            var destination = _path[^1];
            if (!WalkTo(simulation, destination) || _path.Count == 0)
            {
                _path.Clear();
                return;
            }

            next = _path[0];
            direction = DirectionTo(Tile, next)!.Value;
        }

        _path.RemoveAt(0);
        Facing = direction;
        IsStepping = true;
        StepProgress = 0;

        // As in CorsixTH's walk action, objects watching the tiles (entrance doors) learn of
        // the move when the step starts: first the tile entered, then the tile left.
        simulation.NotifyOccupantChange(next, 1);
        simulation.NotifyOccupantChange(Tile, -1);
    }

    internal static Humanoid ReadState(int id, SimReader reader)
    {
        var tile = reader.ReadTile();
        var type = (HumanoidType)reader.ReadByte();
        Span<byte> layers = stackalloc byte[LayerCount];
        for (var i = 0; i < LayerCount; i++)
        {
            layers[i] = reader.ReadByte();
        }

        var employer = reader.ReadInt32();
        var hospital = reader.ReadInt32();
        var profile = reader.ReadBool() ? StaffProfile.Read(reader) : null;
        var humanoid = new Humanoid(id, tile, type, layers)
        {
            Employer = employer,
            HospitalPlayer = hospital,
            Profile = profile,
            Facing = (Direction)reader.ReadByte(),
            IsStepping = reader.ReadBool(),
            StepProgress = reader.ReadInt32(),
            RawWalk = reader.ReadBool(),
            IsDespawning = reader.ReadBool(),
            IsHidden = reader.ReadBool(),
        };

        var count = reader.ReadCount(1 << 20);
        for (var i = 0; i < count; i++)
        {
            humanoid._path.Add(reader.ReadTile());
        }

        if (reader.ReadBool())
        {
            humanoid.ActionAnimation = new AnimationRef(reader.ReadInt32(), reader.ReadInt32());
            humanoid.ActionAnimationFlip = reader.ReadBool();
            humanoid.ActionAnimationStart = reader.ReadInt64();
            humanoid.ActionAnimationTile = reader.ReadTile();
            humanoid.ActionDrawTile = reader.ReadTile();
            humanoid.HiddenObjectId = reader.ReadInt32();
        }

        humanoid.Behaviour = HumanoidBehaviour.Read(reader);
        return humanoid;
    }

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteTile(Tile);
        writer.WriteByte((byte)Type);
        writer.WriteBytes(_layers);
        writer.WriteInt32(Employer);
        writer.WriteInt32(HospitalPlayer);
        writer.WriteBool(Profile is not null);
        Profile?.Write(writer);
        writer.WriteByte((byte)Facing);
        writer.WriteBool(IsStepping);
        writer.WriteInt32(StepProgress);
        writer.WriteBool(RawWalk);
        writer.WriteBool(IsDespawning);
        writer.WriteBool(IsHidden);
        writer.WriteInt32(_path.Count);
        foreach (var tile in _path)
        {
            writer.WriteTile(tile);
        }

        writer.WriteBool(ActionAnimation is not null);
        if (ActionAnimation is { } animation)
        {
            writer.WriteInt32(animation.Id);
            writer.WriteInt32(animation.Length);
            writer.WriteBool(ActionAnimationFlip);
            writer.WriteInt64(ActionAnimationStart);
            writer.WriteTile(ActionAnimationTile);
            writer.WriteTile(ActionDrawTile);
            writer.WriteInt32(HiddenObjectId);
        }

        HumanoidBehaviour.Write(writer, Behaviour);
    }

    /// <summary>
    /// Creates a standard patient with a random appearance, using the same choices as
    /// CorsixTH's disease setup (for example <c>diseases/broken_wind.lua</c>).
    /// </summary>
    public static Humanoid CreateRandomPatient(int id, TilePosition tile, SimRandom random, int hospitalPlayer = -1)
    {
        return CreatePatient(id, tile, random, PatientLook.Standard, hospitalPlayer);
    }

    /// <summary>
    /// Creates a patient who looks as their disease makes them, with CorsixTH's random choices from the disease's
    /// <c>initPatient</c> (<c>diseases/broken_wind.lua</c> for the standard look, <c>diseases/bloaty_head.lua</c>).
    /// </summary>
    public static Humanoid CreatePatient(int id, TilePosition tile, SimRandom random, PatientLook look, int hospitalPlayer = -1)
    {
        Span<byte> layers = stackalloc byte[LayerCount];
        HumanoidType type;
        if (look == PatientLook.BloatyHead)
        {
            layers[0] = (byte)(random.NextInclusive(6, 8) * 2);
            layers[1] = (byte)(random.NextInclusive(0, 3) * 2);
            layers[2] = (byte)(random.NextInclusive(0, 1) * 2);
            return new Humanoid(id, tile, HumanoidType.StandardMalePatient, layers) { HospitalPlayer = hospitalPlayer };
        }

        if (random.Next(2) == 0)
        {
            type = HumanoidType.StandardMalePatient;
            layers[0] = (byte)(random.NextInclusive(1, 5) * 2);
            layers[2] = (byte)(random.NextInclusive(0, 2) * 2);
        }
        else
        {
            type = HumanoidType.StandardFemalePatient;
            layers[0] = (byte)(random.NextInclusive(1, 4) * 2);
        }

        layers[1] = (byte)(random.NextInclusive(0, 3) * 2);
        return new Humanoid(id, tile, type, layers) { HospitalPlayer = hospitalPlayer };
    }

    /// <summary>
    /// Creates a newly hired member of staff, as CorsixTH's <c>Staff:setProfile</c> does: the type
    /// follows the class and layer 5 comes from the profile.
    /// </summary>
    public static Humanoid CreateStaff(int id, TilePosition tile, StaffProfile profile, int employer)
    {
        Span<byte> layers = stackalloc byte[LayerCount];
        layers[StaffLayer] = profile.Layer5;
        var type = profile.Class switch
        {
            StaffClass.Nurse => HumanoidType.Nurse,
            StaffClass.Doctor => HumanoidType.Doctor,
            StaffClass.Handyman => HumanoidType.Handyman,
            _ => HumanoidType.Receptionist,
        };
        return new Humanoid(id, tile, type, layers) { Profile = profile, Employer = employer, HospitalPlayer = employer };
    }

    internal static TilePosition Neighbour(TilePosition tile, Direction direction)
    {
        return direction switch
        {
            Direction.North => tile with { Y = tile.Y - 1 },
            Direction.East => tile with { X = tile.X + 1 },
            Direction.South => tile with { Y = tile.Y + 1 },
            _ => tile with { X = tile.X - 1 },
        };
    }

    private static Direction? DirectionTo(TilePosition from, TilePosition to)
    {
        return (to.X - from.X, to.Y - from.Y) switch
        {
            (0, -1) => Direction.North,
            (1, 0) => Direction.East,
            (0, 1) => Direction.South,
            (-1, 0) => Direction.West,
            _ => null,
        };
    }

    private static bool CanTravel(LevelMap map, TilePosition from, Direction direction)
    {
        var flag = direction switch
        {
            Direction.North => TileFlags.CanTravelNorth,
            Direction.East => TileFlags.CanTravelEast,
            Direction.South => TileFlags.CanTravelSouth,
            _ => TileFlags.CanTravelWest,
        };
        var to = Neighbour(from, direction);
        return map[from.X, from.Y].Has(flag) && map.Contains(to.X, to.Y) && map[to.X, to.Y].Has(TileFlags.Passable);
    }
}
