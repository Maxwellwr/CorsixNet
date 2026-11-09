// Ported from CorsixTH CorsixTH/Lua/entities/rat.lua (Rat).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Entities;

/// <summary>A hole in a corridor wall that rats run between (CorsixTH's <c>hospital.ratholes</c> entries).</summary>
/// <param name="Tile">The corridor tile the hole opens onto.</param>
/// <param name="Wall">The tile's wall it is in; only holes in north and west walls are seen.</param>
/// <param name="Parcel">The parcel of the tile.</param>
public readonly record struct Rathole(TilePosition Tile, Direction Wall, int Parcel)
{
    /// <summary>Whether the hole is drawn (CorsixTH makes a <c>rathole</c> object only for north and west walls).</summary>
    public bool IsVisible => Wall is Direction.North or Direction.West;
}

/// <summary>What a rat is doing.</summary>
public enum RatPhase : byte
{
    /// <summary>Coming out of a hole in a north wall.</summary>
    Emerging,

    /// <summary>Running along the corridor to the other hole, a tile every few ticks.</summary>
    Running,

    /// <summary>Going into the hole at the end of the run.</summary>
    Entering,
}

/// <summary>
/// A rat running along a corridor from one rat hole to another (CorsixTH's <c>Rat</c>). It never stops on the way and
/// goes once it is in the other hole; the player can shoot it.
/// </summary>
public sealed class Rat : Entity
{
    /// <summary>Ticks to cross a tile (<c>walk_quantity</c>).</summary>
    public const int TicksPerTile = 4;

    /// <summary>
    /// Length of the north hole's leaving animation 1928. Not known here without the game's data; see the PR's oddities.
    /// </summary>
    internal const int LeaveHoleTicks = 8;

    /// <summary>Length of the entering animations 1924 (north) and 1926 (west), likewise not known here.</summary>
    internal const int EnterHoleTicks = 8;

    private const int MaxPath = 128 * 128;

    private readonly List<TilePosition> _path;

    /// <param name="id">The entity id.</param>
    /// <param name="player">The hospital whose holes it runs between.</param>
    /// <param name="origin">The hole it comes out of.</param>
    /// <param name="target">The hole it runs to.</param>
    public Rat(int id, int player, Rathole origin, Rathole target)
        : this(id, player, origin, target, [])
    {
    }

    private Rat(int id, int player, Rathole origin, Rathole target, List<TilePosition> path)
        : base(id)
    {
        Player = player;
        Origin = origin;
        Target = target;
        _path = path;
    }

    public override EntityKind Kind => EntityKind.Rat;

    /// <summary>The hospital whose holes it runs between.</summary>
    public int Player { get; }

    /// <summary>The hole it came out of.</summary>
    public Rathole Origin { get; }

    /// <summary>The hole it runs to.</summary>
    public Rathole Target { get; }

    public RatPhase Phase { get; private set; }

    /// <summary>Ticks left of the current phase or tile.</summary>
    public int Timer { get; private set; }

    /// <summary>Which tile of its path it runs from; it runs to the next one.</summary>
    public int PathIndex { get; private set; }

    /// <summary>The tiles from the hole it came out of to the one it runs to.</summary>
    public IReadOnlyList<TilePosition> Path => _path;

    /// <summary>The tile it runs from, or stands on.</summary>
    public TilePosition From => _path.Count == 0 ? Origin.Tile : _path[Math.Min(PathIndex, _path.Count - 1)];

    /// <summary>The tile it runs to, or stands on.</summary>
    public TilePosition To => Phase == RatPhase.Running && PathIndex + 1 < _path.Count ? _path[PathIndex + 1] : From;

    /// <summary>Shot, or gone into a hole or nowhere; it leaves the world at the end of the tick.</summary>
    public bool IsGone { get; private set; }

    /// <summary>
    /// <c>Rat:init</c>: a rat out of <paramref name="origin"/>, running to a random other hole of the hospital; null
    /// (as CorsixTH's rat that is destroyed at once) if there is none.
    /// </summary>
    internal static Rat? Spawn(Simulation simulation, int player, Rathole origin, IReadOnlyList<Rathole> holes)
    {
        // Rat:_chooseTarget: a random hole on another tile.
        var candidates = holes.Where(hole => hole.Tile != origin.Tile).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var target = candidates[simulation.Random.NextInclusive(1, candidates.Count) - 1];
        var rat = simulation.AddEntity(id => new Rat(id, player, origin, target));

        // Rat:_emerge: only north holes have a leaving animation; from the others it sets off at once.
        if (origin.Wall == Direction.North)
        {
            rat.Phase = RatPhase.Emerging;
            rat.Timer = LeaveHoleTicks;
        }
        else
        {
            rat.BeginRun(simulation);
        }

        return rat;
    }

    public override void Update(Simulation simulation)
    {
        if (IsGone || --Timer > 0)
        {
            return;
        }

        switch (Phase)
        {
            case RatPhase.Emerging:
                BeginRun(simulation);
                break;
            case RatPhase.Running:
                PathIndex++;
                WalkToNextTile();
                break;
            default:
                IsGone = true;
                break;
        }
    }

    /// <summary>Shot by the player (<c>Rat:onClick</c>).</summary>
    internal void Shoot() => IsGone = true;

    /// <summary>
    /// <c>Rat:_beginRun</c> and <c>_determinePath</c>: the shortest path to the target hole, which must keep to the
    /// corridors; without one, or a path of a single tile, the rat is gone.
    /// </summary>
    private void BeginRun(Simulation simulation)
    {
        var map = simulation.Map;
        if (!simulation.Pathfinder.TryFindPath(map, Origin.Tile, Target.Tile, _path)
            || _path.Count < 2
            || _path.Any(tile => map[tile.X, tile.Y].RoomId != 0))
        {
            _path.Clear();
            IsGone = true;
            return;
        }

        Phase = RatPhase.Running;
        PathIndex = 0;
        WalkToNextTile();
    }

    /// <summary><c>Rat:_walkToNextTile</c>: on to the next tile, or at the end into the hole (<c>_arrive</c>, <c>_enterHole</c>).</summary>
    private void WalkToNextTile()
    {
        if (PathIndex + 1 < _path.Count)
        {
            Timer = TicksPerTile;
            return;
        }

        // Only holes in north and west walls have an entering animation; into the others the rat goes at once.
        if (Target.IsVisible)
        {
            Phase = RatPhase.Entering;
            Timer = EnterHoleTicks;
        }
        else
        {
            IsGone = true;
        }
    }

    internal static void WriteRathole(SimWriter writer, Rathole hole)
    {
        writer.WriteTile(hole.Tile);
        writer.WriteByte((byte)hole.Wall);
        writer.WriteInt32(hole.Parcel);
    }

    internal static Rathole ReadRathole(SimReader reader)
    {
        var tile = reader.ReadTile();
        var wall = reader.ReadByte();
        if (wall > (byte)Direction.West)
        {
            throw new InvalidDataException($"Unknown rat hole wall {wall}.");
        }

        return new Rathole(tile, (Direction)wall, reader.ReadInt32());
    }

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteInt32(Player);
        WriteRathole(writer, Origin);
        WriteRathole(writer, Target);
        writer.WriteByte((byte)Phase);
        writer.WriteInt32(Timer);
        writer.WriteInt32(PathIndex);
        writer.WriteInt32(_path.Count);
        foreach (var tile in _path)
        {
            writer.WriteTile(tile);
        }
    }

    internal static Rat ReadState(int id, SimReader reader)
    {
        var player = reader.ReadInt32();
        var origin = ReadRathole(reader);
        var target = ReadRathole(reader);
        var phase = reader.ReadByte();
        if (phase > (byte)RatPhase.Entering)
        {
            throw new InvalidDataException($"Unknown rat phase {phase}.");
        }

        var timer = reader.ReadInt32();
        var index = reader.ReadInt32();
        var path = new List<TilePosition>();
        var count = reader.ReadCount(MaxPath);
        for (var i = 0; i < count; i++)
        {
            path.Add(reader.ReadTile());
        }

        return new Rat(id, player, origin, target, path) { Phase = (RatPhase)phase, Timer = timer, PathIndex = index };
    }
}
