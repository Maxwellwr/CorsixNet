// The grim reaper and the gates to hell, ported from CorsixTH CorsixTH/Lua/humanoid_actions/die.lua
// (action_die_tick_reaper), entities/humanoids/grim_reaper.lua, objects/gates_to_hell.lua and the IdleSpawnAction of
// humanoid_actions/idle_spawn.lua.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.People;

/// <summary>Where the grim reaper is in fetching a dead patient.</summary>
public enum GrimReaperPhase : byte
{
    /// <summary>Rising out of the floor (animation 1660).</summary>
    Spawning,

    /// <summary>Standing for 40 ticks where he rose.</summary>
    Standing,

    /// <summary>Walking to his place beside the gates to hell.</summary>
    Walking,

    /// <summary>Waiting there for the patient to get up and walk in.</summary>
    Waiting,

    /// <summary>Sending the patient to hell (1670).</summary>
    Swiping,

    /// <summary>Leaving (1678).</summary>
    Leaving,

    /// <summary>Gone; the gates to hell close (2552) and then go too.</summary>
    Closing,
}

/// <summary>
/// The grim reaper who comes for a dead man (65 percent of them, in CorsixTH), with the gates to hell he opens: he rises
/// near the hole, stands a moment, walks to his place beside it and waits while the patient lies, gets up and walks in;
/// then he swings his scythe, leaves, and the hole closes.
/// </summary>
/// <remarks>
/// The gates to hell are drawn from this behaviour at <see cref="HoleTile"/> rather than being a placed object, and do
/// not block the tile. CorsixTH also checks that the hole leaves a way round it; that check is left out.
/// </remarks>
public sealed class GrimReaperBehaviour : HumanoidBehaviour
{
    /// <summary>The gates to hell open and waiting (<c>gates_to_hell</c>'s idle animation).</summary>
    public static readonly AnimationRef HoleIdle = new(1602, 6);

    /// <summary>The gates to hell closing.</summary>
    public static readonly AnimationRef HoleClosing = new(2552, 7);

    /// <summary>A standard man falling into the gates to hell (its in-use animation).</summary>
    public static readonly AnimationRef FallingIn = new(4560, 12);

    public static readonly AnimationRef Spawn = new(1660, 21);
    public static readonly AnimationRef Swipe = new(1670, 29);
    public static readonly AnimationRef Leave = new(1678, 7);

    /// <summary>Ticks he stands after rising (<c>IdleSpawnAction(...):setCount(40)</c>).</summary>
    private const int StandTicks = 40;

    public override BehaviourKind Kind => BehaviourKind.GrimReaper;

    public GrimReaperPhase Phase { get; private set; }

    /// <summary>The dead patient he has come for.</summary>
    public int PatientId { get; private set; }

    /// <summary>Where the gates to hell are.</summary>
    public TilePosition HoleTile { get; private set; }

    /// <summary>Whether the hole lies east of where the patient died, rather than south.</summary>
    public bool HoleEast { get; private set; }

    /// <summary>Where the patient steps into the hole from.</summary>
    public TilePosition PatientUseTile { get; private set; }

    /// <summary>Where he waits beside the hole.</summary>
    public TilePosition UseTile { get; private set; }

    /// <summary>The tick the current phase started, for the hole's closing animation.</summary>
    public long PhaseStart { get; private set; }

    /// <summary>Which way he stands after rising.</summary>
    private Direction _spawnFacing;

    private int _timer;

    /// <summary>His animations by the hole are mirrored for a hole to the south.</summary>
    private bool Mirror => !HoleEast;

    /// <summary>
    /// die.lua's phase 2: looks for a place for the gates to hell four tiles south of the patient, then four tiles east,
    /// and for the grim reaper; null when there is none, and the patient goes to heaven instead.
    /// </summary>
    internal static Humanoid? TrySpawn(Simulation simulation, Humanoid patient)
    {
        var map = simulation.Map;
        var tile = patient.Tile;

        // {holes_orientation, hole search start, grim use offset, grim spawn direction, patient use offset, grim spawn tries}
        var scenarios = new (bool East, TilePosition Search, (int X, int Y) GrimUse, Direction Facing, (int X, int Y) PatientUse, (Direction? Facing, int X, int Y)[] Spawns)[]
        {
            (false, new(tile.X, tile.Y + 4), (1, 0), Direction.West, (-1, 0), [(Direction.East, -5, 2), (null, 0, 3)]),
            (true, new(tile.X + 4, tile.Y), (0, 1), Direction.North, (0, -1), [(null, 3, 0)]),
        };
        foreach (var scenario in scenarios)
        {
            if (!map.Contains(scenario.Search.X, scenario.Search.Y)
                || !simulation.Pathfinder.TryFindIdleTile(map, scenario.Search, 0, out var hole)
                || !IsHoleFree(map, hole, scenario.East))
            {
                continue;
            }

            var grimUse = new TilePosition(hole.X + scenario.GrimUse.X, hole.Y + scenario.GrimUse.Y);
            var patientUse = new TilePosition(hole.X + scenario.PatientUse.X, hole.Y + scenario.PatientUse.Y);

            // The patient must be able to walk to the hole, and neither of them be in a room.
            if (!map.Contains(grimUse.X, grimUse.Y) || !map.Contains(patientUse.X, patientUse.Y)
                || simulation.Pathfinder.FindDistance(map, tile, patientUse) is null
                || map[patientUse.X, patientUse.Y].RoomId != 0 || map[grimUse.X, grimUse.Y].RoomId != 0)
            {
                continue;
            }

            // The hole must leave a way round it, so that it does not block a corridor one tile wide.
            map[hole.X, hole.Y].Set(TileFlags.Passable, false);
            var wayRound = simulation.Pathfinder.FindDistance(map, grimUse, patientUse);
            map[hole.X, hole.Y].Set(TileFlags.Passable, true);
            if (wayRound != 4)
            {
                continue;
            }

            // A place for him to rise from which he can walk to his place, else his place itself.
            var spawn = grimUse;
            var facing = scenario.Facing;
            foreach (var attempt in scenario.Spawns)
            {
                var near = new TilePosition(hole.X + attempt.X, hole.Y + attempt.Y);
                if (map.Contains(near.X, near.Y) && simulation.Pathfinder.TryFindIdleTile(map, near, 0, out var found)
                    && map[found.X, found.Y].RoomId == 0 && simulation.Pathfinder.FindDistance(map, found, grimUse) is not null)
                {
                    spawn = found;
                    facing = attempt.Facing ?? scenario.Facing;
                    break;
                }
            }

            SetHoleFlags(map, hole, scenario.East, present: true);
            var reaper = simulation.AddEntity(id => new Humanoid(id, spawn, HumanoidType.GrimReaper));
            reaper.Face(facing);
            reaper.Behaviour = new GrimReaperBehaviour
            {
                PatientId = patient.Id,
                HoleTile = hole,
                HoleEast = scenario.East,
                UseTile = grimUse,
                PatientUseTile = patientUse,
                _spawnFacing = facing,
                PhaseStart = simulation.Tick,
                _timer = Spawn.Length,
            };
            reaper.SetActionAnimation(simulation, Spawn, false, spawn, spawn, 0);
            return reaper;
        }

        return null;
    }

    /// <summary>
    /// <c>canNonSideObjectBeSpawnedAt</c> for the gates to hell: the hole and the tiles either side of it (east and west for
    /// a hole to the south, north and south for one to the east) are passable corridor.
    /// </summary>
    private static bool IsHoleFree(LevelMap map, TilePosition hole, bool east)
    {
        foreach (var d in new[] { -1, 0, 1 })
        {
            var (x, y) = east ? (hole.X, hole.Y + d) : (hole.X + d, hole.Y);
            if (!map.Contains(x, y) || !map[x, y].Has(TileFlags.Passable) || map[x, y].RoomId != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The gates to hell as a placed object (entities/object.lua's <c>setTile</c> with gates_to_hell.lua's footprint): the
    /// hole is neither passable nor buildable, the tiles either side of it not buildable, until it closes.
    /// </summary>
    private static void SetHoleFlags(LevelMap map, TilePosition hole, bool east, bool present)
    {
        foreach (var d in new[] { -1, 0, 1 })
        {
            var (x, y) = east ? (hole.X, hole.Y + d) : (hole.X + d, hole.Y);
            map[x, y].Set(TileFlags.Buildable, !present);
            if (d == 0)
            {
                map[x, y].Set(TileFlags.Passable, !present);
            }
        }
    }

    /// <summary>The patient has walked into the hole: he swings his scythe (die.lua's <c>post_walk_into</c>).</summary>
    internal void PatientFell(Simulation simulation, Humanoid reaper) => Enter(simulation, reaper, GrimReaperPhase.Swiping);

    internal override void Update(Simulation simulation, Humanoid humanoid)
    {
        switch (Phase)
        {
            case GrimReaperPhase.Spawning:
                if (--_timer <= 0)
                {
                    humanoid.ClearActionAnimation();
                    humanoid.Face(_spawnFacing);
                    Enter(simulation, humanoid, GrimReaperPhase.Standing);
                    _timer = StandTicks;
                }

                break;
            case GrimReaperPhase.Standing:
                if (--_timer <= 0)
                {
                    Enter(simulation, humanoid, GrimReaperPhase.Walking);
                    if (humanoid.Tile != UseTile)
                    {
                        humanoid.WalkTo(simulation, UseTile);
                    }
                }

                break;
            case GrimReaperPhase.Walking:
                if (humanoid.IsMoving)
                {
                    break;
                }

                // Idle facing the hole (animation 1002, mirrored for a hole to the south), and the patient may get up.
                humanoid.Face(Mirror ? Direction.West : Direction.North);
                Enter(simulation, humanoid, GrimReaperPhase.Waiting);
                if (simulation.FindEntity(PatientId) is Humanoid { Behaviour: PatientBehaviour patient } dead)
                {
                    patient.ReaperReady(dead);
                }
                else
                {
                    Enter(simulation, humanoid, GrimReaperPhase.Swiping);
                }

                break;
            case GrimReaperPhase.Waiting:
                if (simulation.FindEntity(PatientId) is not Humanoid { IsDespawning: false })
                {
                    Enter(simulation, humanoid, GrimReaperPhase.Swiping);
                }

                break;
            case GrimReaperPhase.Swiping:
            case GrimReaperPhase.Leaving:
                if (--_timer <= 0)
                {
                    Enter(simulation, humanoid, Phase + 1);
                }

                break;
            default:
                if (--_timer <= 0)
                {
                    // die.lua's lava_destroy: the closed hole is taken away.
                    SetHoleFlags(simulation.Map, HoleTile, HoleEast, present: false);
                    humanoid.IsDespawning = true;
                }

                break;
        }
    }

    private void Enter(Simulation simulation, Humanoid humanoid, GrimReaperPhase phase)
    {
        Phase = phase;
        PhaseStart = simulation.Tick;
        switch (phase)
        {
            case GrimReaperPhase.Swiping:
                humanoid.SetActionAnimation(simulation, Swipe, Mirror, humanoid.Tile, humanoid.Tile, 0);
                _timer = Swipe.Length;
                break;
            case GrimReaperPhase.Leaving:
                humanoid.SetActionAnimation(simulation, Leave, Mirror, humanoid.Tile, humanoid.Tile, 0);
                _timer = Leave.Length;
                break;
            case GrimReaperPhase.Closing:
                // He is gone; the hole closes over the next ticks.
                humanoid.ClearActionAnimation();
                _timer = HoleClosing.Length;
                break;
        }
    }

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteByte((byte)Phase);
        writer.WriteInt32(PatientId);
        writer.WriteTile(HoleTile);
        writer.WriteBool(HoleEast);
        writer.WriteTile(PatientUseTile);
        writer.WriteTile(UseTile);
        writer.WriteInt64(PhaseStart);
        writer.WriteByte((byte)_spawnFacing);
        writer.WriteInt32(_timer);
    }

    internal static GrimReaperBehaviour ReadState(SimReader reader)
    {
        var phase = reader.ReadByte();
        var behaviour = new GrimReaperBehaviour
        {
            Phase = phase <= (byte)GrimReaperPhase.Closing ? (GrimReaperPhase)phase : throw new InvalidDataException($"Unknown grim reaper phase {phase}."),
            PatientId = reader.ReadInt32(),
            HoleTile = reader.ReadTile(),
            HoleEast = reader.ReadBool(),
            PatientUseTile = reader.ReadTile(),
            UseTile = reader.ReadTile(),
            PhaseStart = reader.ReadInt64(),
        };
        var facing = reader.ReadByte();
        behaviour._spawnFacing = facing <= (byte)Direction.West ? (Direction)facing : throw new InvalidDataException($"Unknown direction {facing}.");
        behaviour._timer = reader.ReadInt32();
        return behaviour;
    }
}
