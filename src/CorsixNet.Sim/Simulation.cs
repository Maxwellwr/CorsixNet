using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Pathfinding;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim;

/// <summary>
/// The complete, deterministic game state and the rules that advance it one tick at a time.
/// </summary>
/// <remarks>
/// Each <see cref="Step"/> first executes the commands scheduled for the current tick, in
/// sequence order, then updates every entity in id order, then advances <see cref="Tick"/>.
/// Given the same initial state and the same commands, every machine reaches the same state,
/// which <see cref="ComputeStateHash"/> lets them compare.
/// </remarks>
public sealed class Simulation
{
    /// <summary>Version of the serialized format written by <see cref="Save"/>.</summary>
    public const int FormatVersion = 1;

    private const uint Magic = 0x56534E43; // "CNSV" little-endian
    private const int MaxEntities = 1 << 20;
    private const int MaxPendingCommands = 1 << 20;

    private readonly List<Entity> _entities = [];
    private readonly List<ScheduledCommand> _pending = [];

    public Simulation(LevelMap map, ulong seed)
        : this(map, new SimRandom(seed))
    {
    }

    private Simulation(LevelMap map, SimRandom random)
    {
        Map = map;
        Random = random;
    }

    /// <summary>Number of ticks completed since the game started.</summary>
    public long Tick { get; private set; }

    public LevelMap Map { get; }

    /// <summary>The only source of randomness the simulation may use.</summary>
    public SimRandom Random { get; private set; }

    /// <summary>All entities, in increasing id order.</summary>
    public IReadOnlyList<Entity> Entities => _entities;

    /// <summary>Commands scheduled but not yet executed, in execution order.</summary>
    public IReadOnlyList<ScheduledCommand> PendingCommands => _pending;

    /// <summary>Search helper with reusable buffers; holds no game state.</summary>
    internal Pathfinder Pathfinder { get; } = new();

    private int NextEntityId { get; set; } = 1;

    private long NextSequence { get; set; }

    /// <summary>Schedules a command with an explicit tick and sequence, as received from a lockstep server.</summary>
    /// <exception cref="ArgumentException">The tick has already passed.</exception>
    public void Schedule(ScheduledCommand command)
    {
        if (command.Tick < Tick)
        {
            throw new ArgumentException($"Command for tick {command.Tick} arrived at tick {Tick}.", nameof(command));
        }

        var index = _pending.Count;
        while (index > 0 && Compare(_pending[index - 1], command) > 0)
        {
            index--;
        }

        _pending.Insert(index, command);
        NextSequence = Math.Max(NextSequence, command.Sequence + 1);
    }

    /// <summary>Schedules a command for the current tick (or a later one) with the next sequence number.</summary>
    public ScheduledCommand Schedule(int player, Command command, long? tick = null)
    {
        var scheduled = new ScheduledCommand(tick ?? Tick, NextSequence, player, command);
        Schedule(scheduled);
        return scheduled;
    }

    /// <summary>Advances the simulation by one tick.</summary>
    public void Step()
    {
        while (_pending.Count > 0 && _pending[0].Tick == Tick)
        {
            var scheduled = _pending[0];
            _pending.RemoveAt(0);
            scheduled.Command.Execute(this, scheduled.Player);
        }

        // Entities added during this loop are updated from the next tick on.
        var count = _entities.Count;
        for (var i = 0; i < count && i < _entities.Count; i++)
        {
            _entities[i].Update(this);
        }

        Tick++;
    }

    /// <summary>Creates an entity with the next free id.</summary>
    public T AddEntity<T>(Func<int, T> create)
        where T : Entity
    {
        var entity = create(NextEntityId);
        if (entity.Id != NextEntityId)
        {
            throw new InvalidOperationException("An entity must use the id it is given.");
        }

        NextEntityId++;
        _entities.Add(entity);
        return entity;
    }

    public Entity? FindEntity(int id)
    {
        var index = IndexOfEntity(id);
        return index >= 0 ? _entities[index] : null;
    }

    public bool RemoveEntity(int id)
    {
        var index = IndexOfEntity(id);
        if (index < 0)
        {
            return false;
        }

        _entities.RemoveAt(index);
        return true;
    }

    /// <summary>Writes the complete state.</summary>
    public void Save(Stream stream) => Write(new SimWriter(stream));

    /// <summary>Restores a simulation written by <see cref="Save"/>.</summary>
    /// <exception cref="InvalidDataException">The data is not a valid simulation state.</exception>
    public static Simulation Load(Stream stream)
    {
        var reader = new SimReader(stream);
        if (reader.ReadUInt32() != Magic)
        {
            throw new InvalidDataException("Data is not a CorsixNet simulation state.");
        }

        var version = reader.ReadInt32();
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Simulation state version {version} is not supported (expected {FormatVersion}).");
        }

        var tick = reader.ReadInt64();
        var random = SimRandom.FromState(reader.ReadUInt64(), reader.ReadUInt64());
        var nextEntityId = reader.ReadInt32();
        var nextSequence = reader.ReadInt64();
        var map = LevelMap.Read(reader);

        var simulation = new Simulation(map, random)
        {
            Tick = tick,
            NextEntityId = nextEntityId,
            NextSequence = nextSequence,
        };

        var entityCount = reader.ReadCount(MaxEntities);
        for (var i = 0; i < entityCount; i++)
        {
            var entity = Entity.Read(reader);
            if (entity.Id >= nextEntityId || (simulation._entities.Count > 0 && entity.Id <= simulation._entities[^1].Id))
            {
                throw new InvalidDataException($"Entity id {entity.Id} is out of order.");
            }

            simulation._entities.Add(entity);
        }

        var commandCount = reader.ReadCount(MaxPendingCommands);
        for (var i = 0; i < commandCount; i++)
        {
            simulation.Schedule(ScheduledCommand.Read(reader));
        }

        return simulation;
    }

    /// <summary>Hash of the complete serialized state, for detecting desyncs between machines.</summary>
    public ulong ComputeStateHash()
    {
        using var hash = new StateHash();
        Write(new SimWriter(hash));
        return hash.Value;
    }

    private void Write(SimWriter writer)
    {
        writer.WriteUInt32(Magic);
        writer.WriteInt32(FormatVersion);
        writer.WriteInt64(Tick);
        writer.WriteUInt64(Random.State);
        writer.WriteUInt64(Random.Increment);
        writer.WriteInt32(NextEntityId);
        writer.WriteInt64(NextSequence);
        Map.Write(writer);

        writer.WriteInt32(_entities.Count);
        foreach (var entity in _entities)
        {
            entity.Write(writer);
        }

        writer.WriteInt32(_pending.Count);
        foreach (var command in _pending)
        {
            command.Write(writer);
        }
    }

    private int IndexOfEntity(int id)
    {
        int low = 0, high = _entities.Count - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var midId = _entities[mid].Id;
            if (midId == id)
            {
                return mid;
            }

            if (midId < id)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return -1;
    }

    private static int Compare(ScheduledCommand a, ScheduledCommand b)
    {
        var byTick = a.Tick.CompareTo(b.Tick);
        return byTick != 0 ? byTick : a.Sequence.CompareTo(b.Sequence);
    }
}
