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

    /// <summary>A male patient undressed behind the screen (CorsixTH's three "Stripped Male Patient" classes look the same).</summary>
    StrippedMalePatient = 7,

    StrippedFemalePatient = 8,

    /// <summary>A VIP visiting the hospital (CorsixTH's "VIP", who looks like the inspector).</summary>
    Vip = 9,

    /// <summary>The health inspector, after an epidemic's cover-up (CorsixTH's "Inspector").</summary>
    Inspector = 10,

    /// <summary>
    /// CorsixTH's "Stripped Male Patient 2" and "3": they look like <see cref="StrippedMalePatient"/>, but some machines
    /// (the scanner, the cardiogram) have animations of their own for each (<c>use_screen.lua</c> picks one of the three).
    /// </summary>
    StrippedMalePatient2 = 11,

    StrippedMalePatient3 = 12,

    /// <summary>CorsixTH's "Stripped Female Patient 2" and "3".</summary>
    StrippedFemalePatient2 = 13,

    StrippedFemalePatient3 = 14,
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
    private int _doorTicks;
    private bool _skipStepTick;
    private TilePosition _doorExit;
    private TilePosition? _resumeTo;

    public Humanoid(int id, TilePosition tile, HumanoidType type = HumanoidType.StandardMalePatient, ReadOnlySpan<byte> layers = default)
        : base(id)
    {
        Tile = tile;
        Type = type;
        Facing = Direction.South;
        layers[..Math.Min(layers.Length, LayerCount)].CopyTo(_layers);
    }

    /// <summary>How warm the person feels at first (CorsixTH's <c>attributes["warmth"]</c>).</summary>
    public const decimal StartWarmth = 0.29m;

    /// <summary>How warm the person feels, 0 to 1 like a tile's temperature over 50 °C (<c>attributes["warmth"]</c>).</summary>
    public decimal Warmth { get; internal set; } = StartWarmth;

    /// <summary>How happy the person is, 0 to 1 (<c>attributes["happiness"]</c>).</summary>
    public decimal Happiness { get; internal set; } = 1m;

    /// <summary>How tired a member of staff is, from 0 (rested) to 1 (CorsixTH's <c>fatigue</c> attribute).</summary>
    public decimal Fatigue { get; internal set; }

    /// <summary>
    /// Tired with no staff room to go to (CorsixTH's <c>waiting_for_staffroom</c>); cleared when one is built.
    /// </summary>
    public bool WaitingForStaffRoom { get; internal set; }

    /// <summary>Fired or quit: leaving the hospital and no longer counted as its staff (CorsixTH's <c>fired</c>).</summary>
    public bool IsFired { get; internal set; }

    /// <summary>Hours of unhappiness left before asking for a raise, or −1 (CorsixTH's <c>timer_until_raise</c>).</summary>
    public int RaiseTimer { get; internal set; } = -1;

    /// <summary>The raise asked for and not yet answered, or 0 (CorsixTH's <c>pay_rise</c> mood and message).</summary>
    public int RaiseRequest { get; internal set; }

    /// <summary>Hours the raise request still waits for an answer, or −1 (CorsixTH's <c>quitting_in</c>).</summary>
    public int QuittingIn { get; internal set; } = -1;

    /// <summary>
    /// Whether the humanoid moves at half speed, every other tick skipped (CorsixTH's <c>slow_animation</c>, which VIPs
    /// have).
    /// </summary>
    public bool IsSlow { get; internal set; }

    /// <summary>
    /// Whether the humanoid's steps take half the ticks (CorsixTH's <c>speed == "fast"</c>: consultants, and everyone
    /// with the no-rest cheat).
    /// </summary>
    public bool IsFast { get; internal set; }

    /// <summary>Whether the current step was started fast; the speed counts from the start of a step.</summary>
    private bool _fastStep;

    /// <summary>Ticks the current step takes: <see cref="TicksPerStep"/>, or half of it for a fast one.</summary>
    public int StepTicks => _fastStep ? TicksPerStep / 2 : TicksPerStep;

    /// <summary>The kind of person; a patient changes it when undressing and dressing (CorsixTH's <c>setType</c>).</summary>
    public HumanoidType Type { get; internal set; }

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
    public bool IsMoving => IsStepping || IsCrossingDoor || _path.Count > 0;

    /// <summary>
    /// Whether the humanoid is going through a room door, as CorsixTH's <c>navigateDoor</c>: their
    /// <see cref="ActionAnimation"/> shows the door (<see cref="HiddenObjectId"/>) opening and closing.
    /// </summary>
    public bool IsCrossingDoor => _doorTicks > 0;

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

    /// <summary>
    /// The tile the current step (or the way through a door) leads to; equals <see cref="Tile"/> when not stepping.
    /// </summary>
    public TilePosition StepTarget => IsCrossingDoor ? _doorExit : IsStepping ? Neighbour(Tile, Facing) : Tile;

    /// <summary>Tiles still to walk to after the current step, in order.</summary>
    public IReadOnlyList<TilePosition> RemainingPath => _path;

    /// <summary>The final tile of the walk, or null when not walking anywhere.</summary>
    public TilePosition? Destination => _path.Count > 0 ? _path[^1] : IsStepping || IsCrossingDoor ? StepTarget : null;

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
        _resumeTo = null;
        RawWalk = false;
        if (!IsCrossingDoor)
        {
            ClearActionAnimation();
        }

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
        _resumeTo = null;
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
    /// <summary>
    /// The tile that objects watching tiles (entrance doors) count the humanoid on: the tile being entered from the
    /// start of a step, as the walk action tells them then.
    /// </summary>
    internal TilePosition CountedTile => IsCrossingDoor ? _doorExit : IsStepping ? Neighbour(Tile, Facing) : Tile;

    internal void PlaceAt(TilePosition tile)
    {
        _path.Clear();
        _resumeTo = null;
        if (IsCrossingDoor)
        {
            _doorTicks = 0;
            ClearActionAnimation();
        }

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
        // Staff tire and think of their pay before they act, as CorsixTH's Staff:tick does its checks after Entity.tick.
        if (Profile is not null && Employer >= 0 && !IsDespawning)
        {
            if (!IsFired)
            {
                StaffEmployment.Tick(simulation, this);
            }
            else if (Behaviour is not LeavingBehaviour)
            {
                StaffEmployment.LeaveWhenFree(simulation, this);
            }
        }

        if (IsCrossingDoor)
        {
            _doorTicks--;
            if (_doorTicks > 0)
            {
                return;
            }

            // Through the door: carry on from the far side in the same tick, as action_walk_tick_door does.
            Tile = _doorExit;
            ClearActionAnimation();
        }

        if (IsStepping)
        {
            // Entity:_tick: slow humanoids skip every other tick, the first one included.
            if (IsSlow)
            {
                _skipStepTick = !_skipStepTick;
                if (_skipStepTick)
                {
                    return;
                }
            }

            StepProgress++;
            if (StepProgress < StepTicks)
            {
                return;
            }

            Tile = Neighbour(Tile, Facing);
            IsStepping = false;
            StepProgress = 0;
        }

        if (_path.Count == 0)
        {
            if (_resumeTo is { } resume)
            {
                // Out of a blueprint's way: back to where the humanoid was going (LeaveArea).
                _resumeTo = null;
                if (resume != Tile && WalkTo(simulation, resume) && _path.Count > 0)
                {
                    return;
                }
            }

            Behaviour?.Update(simulation, this);
            return;
        }

        var next = _path[0];
        if (RawWalk && DirectionTo(Tile, next) is { } rawDirection)
        {
            _path.RemoveAt(0);
            Facing = rawDirection;
            IsStepping = true;
            _fastStep = IsFast;
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
        else if (!RawWalk && !simulation.Map[Tile.X, Tile.Y].Has(TileFlags.AvoidTile) && simulation.Map[next.X, next.Y].Has(TileFlags.AvoidTile))
        {
            // About to walk into a blueprint: plan again, as walk.lua's action_walk_tick does, which goes around
            // it where it can. CorsixTH keeps planning again for as long as the new path still leads into the
            // blueprint; here the new path is taken as it is.
            var destination = _path[^1];
            if (!WalkTo(simulation, destination) || _path.Count == 0)
            {
                _path.Clear();
                return;
            }

            next = _path[0];
            if (DirectionTo(Tile, next) is not { } replanned || !CanTravel(simulation.Map, Tile, replanned))
            {
                return;
            }

            direction = replanned;
        }

        var door = RoomDoorBetween(simulation, Tile, direction);
        var doorAnimations = door is null ? null : simulation.Content.FindDoorAnimations(Type);
        if (door is not null && doorAnimations is not null && IsDoorInUse(simulation, door))
        {
            // Someone else is going through; wait on this side until the door is free (CorsixTH's door.user).
            Facing = direction;
            return;
        }

        _path.RemoveAt(0);
        Facing = direction;

        // As in CorsixTH's walk action, objects watching the tiles (entrance doors) learn of
        // the move when the step starts: first the tile entered, then the tile left.
        simulation.NotifyOccupantChange(next, 1);
        simulation.NotifyOccupantChange(Tile, -1);

        // CorsixTH's walk action moves the humanoid to the next tile when the step starts (Patient:setTile).
        if (Behaviour is PatientBehaviour patient)
        {
            Littering.OnTileEntered(simulation, this, patient, next);
        }

        if (door is not null && doorAnimations is not null)
        {
            CrossDoor(simulation, door, doorAnimations, direction, next);
            return;
        }

        IsStepping = true;
        _fastStep = IsFast;
        StepProgress = 0;
    }

    /// <summary>
    /// Goes through a room door as CorsixTH's <c>navigateDoor</c> (<c>humanoid_actions/walk.lua</c>): the humanoid stands on
    /// the door's tile while its door animation plays, the door itself hidden (<c>Door:setUser</c>), and comes out on
    /// <paramref name="exit"/> when the animation ends. Going north or west uses the leaving animation, east or south the
    /// entering one, mirrored for west and south.
    /// </summary>
    private void CrossDoor(Simulation simulation, RoomDoor door, DoorAnimations animations, Direction direction, TilePosition exit)
    {
        var inwards = direction is Direction.North or Direction.West;
        if (door.IsSwing && (inwards ? animations.LeavingSwing : animations.EnteringSwing) is { } swing)
        {
            // navigateDoor with swing doors: the humanoid's own swing animation, and the doors swing by themselves
            // (SwingDoor:swingDoors), so they stay drawn.
            Tile = door.Tile;
            IsStepping = false;
            StepProgress = 0;
            _doorExit = exit;
            _doorTicks = swing.Length;
            SetActionAnimation(simulation, swing, direction is Direction.West or Direction.South, door.Tile, door.Tile, 0);
            door.StartSwing(simulation.Tick, inwards, swing.Length);
            return;
        }

        var animation = inwards ? animations.Leaving : animations.Entering;
        Tile = door.Tile;
        IsStepping = false;
        StepProgress = 0;
        _doorExit = exit;
        _doorTicks = animation.Length;
        SetActionAnimation(simulation, animation, direction is Direction.West or Direction.South, door.Tile, door.Tile, door.Id);
    }

    /// <summary>
    /// Whether the humanoid is inside the rectangle from (<paramref name="x1"/>, <paramref name="y1"/>) to
    /// (<paramref name="x2"/>, <paramref name="y2"/>), or walking into it from its border, as CorsixTH's
    /// <c>Humanoid:isObscuringArea</c>. A blueprint passes the tiles of its walls, one tile beyond the room's area.
    /// </summary>
    public bool IsObscuringArea(int x1, int x2, int y1, int y2)
    {
        var (x, y) = (Tile.X, Tile.Y);
        if (x < x1 || x > x2 || y < y1 || y > y2)
        {
            return false;
        }

        if (x == x1 || x == x2 || y == y1 || y == y2)
        {
            // On the border: only someone walking into the rectangle is in the way. CorsixTH looks at the tile
            // the walk is heading for, which is the tile stood on while not stepping.
            if (!IsMoving)
            {
                return false;
            }

            var next = StepTarget;
            if (x1 >= next.X || next.X >= x2 || y1 >= next.Y || next.Y >= y2)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Gets out of the way of a blueprint whose tiles are marked <see cref="TileFlags.AvoidTile"/>, as CorsixTH's
    /// <c>Humanoid:leaveArea</c>: everyone in a queue is sent to their place again (which is no longer in the
    /// blueprint); anyone else walks to a nearby idle tile, which is never in a blueprint, and someone who was on
    /// their way somewhere goes on there afterwards.
    /// </summary>
    internal void LeaveArea(Simulation simulation)
    {
        foreach (var entity in simulation.Entities)
        {
            var queue = entity switch
            {
                RoomObject { Desk: { } desk } => desk.Queue,
                Room { DoorQueue: { } doorQueue } => doorQueue,
                _ => null,
            };
            if (queue is not null && queue.Contains(Id))
            {
                queue.Arrange(simulation);
                return;
            }
        }

        // A walk to somewhere is resumed after a single meander (CorsixTH queues MeanderAction():setCount(1)
        // before it); someone just wandering about picks a new place to wander to, outside the blueprint.
        TilePosition? resume = Behaviour is not null && IsMoving ? Destination : null;
        var start = StepTarget;
        if (simulation.Pathfinder.TryFindIdleTile(simulation.Map, start, simulation.Random.NextInclusive(1, 24), out var tile) && tile != start)
        {
            WalkTo(simulation, tile);
            _resumeTo = resume;
        }
    }

    /// <summary>The room door on the edge crossed by a step from <paramref name="start"/> towards <paramref name="direction"/>, if any.</summary>
    private static RoomDoor? RoomDoorBetween(Simulation simulation, TilePosition start, Direction direction)
    {
        var map = simulation.Map;
        var (doorTile, edge) = direction switch
        {
            Direction.North => (start, EdgeOrientation.North),
            Direction.West => (start, EdgeOrientation.West),
            Direction.South => (start with { Y = start.Y + 1 }, EdgeOrientation.North),
            _ => (start with { X = start.X + 1 }, EdgeOrientation.West),
        };
        var flag = edge == EdgeOrientation.North ? TileFlags.DoorNorth : TileFlags.DoorWest;
        if (!map.Contains(doorTile.X, doorTile.Y) || !map[doorTile.X, doorTile.Y].Has(flag))
        {
            return null;
        }

        foreach (var entity in simulation.Entities)
        {
            if (entity is RoomDoor door && door.Tile == doorTile && door.Edge == edge)
            {
                return door;
            }
        }

        return null;
    }

    /// <summary>Whether another humanoid is going through <paramref name="door"/>.</summary>
    private bool IsDoorInUse(Simulation simulation, RoomDoor door)
    {
        if (door.IsSwingBusy(simulation.Tick, simulation.Content.SwingDoor))
        {
            return true;
        }

        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { IsCrossingDoor: true } other && other.Id != Id && other.HiddenObjectId == door.Id)
            {
                return true;
            }
        }

        return false;
    }

    private static decimal ReadAttribute(SimReader reader)
    {
        var value = reader.ReadDecimal();
        return value is >= 0m and <= 1m ? value : throw new InvalidDataException($"Attribute {value} is not between 0 and 1.");
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
            _doorTicks = reader.ReadInt32(),
            _doorExit = reader.ReadTile(),
            _resumeTo = reader.ReadBool() ? reader.ReadTile() : null,
            Warmth = ReadAttribute(reader),
            Happiness = ReadAttribute(reader),
            Fatigue = ReadAttribute(reader),
            WaitingForStaffRoom = reader.ReadBool(),
            IsFired = reader.ReadBool(),
            RaiseTimer = reader.ReadInt32(),
            RaiseRequest = reader.ReadInt32(),
            QuittingIn = reader.ReadInt32(),
            IsSlow = reader.ReadBool(),
            _skipStepTick = reader.ReadBool(),
            IsFast = reader.ReadBool(),
            _fastStep = reader.ReadBool(),
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
        writer.WriteInt32(_doorTicks);
        writer.WriteTile(_doorExit);
        writer.WriteBool(_resumeTo is not null);
        if (_resumeTo is { } resumeTo)
        {
            writer.WriteTile(resumeTo);
        }

        writer.WriteDecimal(Warmth);
        writer.WriteDecimal(Happiness);
        writer.WriteDecimal(Fatigue);
        writer.WriteBool(WaitingForStaffRoom);
        writer.WriteBool(IsFired);
        writer.WriteInt32(RaiseTimer);
        writer.WriteInt32(RaiseRequest);
        writer.WriteInt32(QuittingIn);
        writer.WriteBool(IsSlow);
        writer.WriteBool(_skipStepTick);
        writer.WriteBool(IsFast);
        writer.WriteBool(_fastStep);

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

    /// <summary>A VIP visiting <paramref name="hospitalPlayer"/>'s hospital, who walks slowly (<c>World:spawnVIP</c>).</summary>
    public static Humanoid CreateVip(int id, TilePosition tile, int hospitalPlayer) =>
        new(id, tile, HumanoidType.Vip) { HospitalPlayer = hospitalPlayer, IsSlow = true };

    /// <summary>Creates the health inspector coming to a hospital, who walks at normal speed.</summary>
    public static Humanoid CreateInspector(int id, TilePosition tile, int hospitalPlayer) =>
        new(id, tile, HumanoidType.Inspector) { HospitalPlayer = hospitalPlayer };

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
