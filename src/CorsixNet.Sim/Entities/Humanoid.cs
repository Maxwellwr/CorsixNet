using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;

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

    /// <summary>Number of appearance layers (head, clothes, shoes and two more) a humanoid sets.</summary>
    public const int LayerCount = 5;

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
        _path.Clear();
        if (found)
        {
            // The first tile of the path is where the humanoid already is (or is stepping to).
            for (var i = 1; i < path.Count; i++)
            {
                _path.Add(path[i]);
            }
        }

        return found;
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
            return;
        }

        var next = _path[0];
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

        var humanoid = new Humanoid(id, tile, type, layers)
        {
            Facing = (Direction)reader.ReadByte(),
            IsStepping = reader.ReadBool(),
            StepProgress = reader.ReadInt32(),
        };

        var count = reader.ReadCount(1 << 20);
        for (var i = 0; i < count; i++)
        {
            humanoid._path.Add(reader.ReadTile());
        }

        return humanoid;
    }

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteTile(Tile);
        writer.WriteByte((byte)Type);
        writer.WriteBytes(_layers);
        writer.WriteByte((byte)Facing);
        writer.WriteBool(IsStepping);
        writer.WriteInt32(StepProgress);
        writer.WriteInt32(_path.Count);
        foreach (var tile in _path)
        {
            writer.WriteTile(tile);
        }
    }

    /// <summary>
    /// Creates a standard patient with a random appearance, using the same choices as
    /// CorsixTH's disease setup (for example <c>diseases/broken_wind.lua</c>).
    /// </summary>
    public static Humanoid CreateRandomPatient(int id, TilePosition tile, SimRandom random)
    {
        Span<byte> layers = stackalloc byte[LayerCount];
        HumanoidType type;
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
        return new Humanoid(id, tile, type, layers);
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
