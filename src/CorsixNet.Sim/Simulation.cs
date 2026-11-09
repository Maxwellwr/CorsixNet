using System.Globalization;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Disasters;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.Pathfinding;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Serialization;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Sim;

/// <summary>
/// The complete, deterministic game state and the rules that advance it one tick at a time.
/// </summary>
/// <remarks>
/// Each <see cref="Step"/> first executes the commands scheduled for the current tick, in
/// sequence order, then handles the end of the day if the tick is the day's last hour, then
/// updates every entity in id order, then advances <see cref="Tick"/>.
/// Given the same initial state and the same commands, every machine reaches the same state,
/// which <see cref="ComputeStateHash"/> lets them compare.
/// </remarks>
public sealed class Simulation
{
    /// <summary>Version of the serialized format written by <see cref="Save"/>.</summary>
    private const int FormatVersion = 58;

    /// <summary>The oldest version still read: 57 lacks only what handymen are doing (version 58).</summary>
    private const int OldestFormatVersion = 57;

    private const uint Magic = 0x56534E43; // "CNSV" little-endian
    private const int MaxEntities = 1 << 20;
    private const int MaxPendingCommands = 1 << 20;
    private const int MaxAvailableStaff = 1 << 16;

    /// <summary>The object type whose number the heating bill goes by.</summary>
    private const string Radiator = "radiator";

    private readonly List<Entity> _entities = [];
    private readonly List<FloatingDollarSign> _floatingDollarSigns = [];
    private readonly List<ScheduledCommand> _pending = [];
    private readonly List<MapObjectPlacement> _delayedObjects = [];
    private readonly Hospital[] _hospitals;
    private readonly List<CompetitorHospital> _competitors = [];

    /// <summary>
    /// Which parcels touch, by passable tiles of the map as the level began (CorsixTH's
    /// <c>parcel_adjacency_matrix</c>), as parcel × parcel count + parcel.
    /// </summary>
    private bool[] _parcelAdjacency = [];

    /// <summary>Candidates for hire per <see cref="StaffClass"/>, shared by every hospital as in CorsixTH.</summary>
    private readonly List<StaffProfile>[] _availableStaff = [[], [], [], []];

    /// <summary>Entity to tell when someone walks onto or off a tile. Derived from the entities; not saved.</summary>
    private readonly Dictionary<TilePosition, int> _occupantObservers = [];

    /// <summary>Scratch space for <see cref="UpdateTemperatures"/>: each tile's radiator temperature, not saved.</summary>
    private ushort[]? _radiatorTemperatures;

    /// <param name="map">The level map; the simulation takes ownership and changes it.</param>
    /// <param name="seed">Seed for <see cref="Random"/>.</param>
    /// <param name="content">Content definitions; every machine in a game must use the same.</param>
    /// <param name="rules">The level's money settings; by default everything is free.</param>
    public Simulation(LevelMap map, ulong seed, GameContent? content = null, LevelRules? rules = null)
        : this(map, new SimRandom(seed), content ?? GameContent.Empty, rules ?? LevelRules.Free)
    {
        for (var player = 0; player < _hospitals.Length; player++)
        {
            // CorsixTH's Hospital:Hospital prices the parcel numbered as the player: hospital tiles and 20,000.
            var land = (long)map.ParcelTileCount(player + 1) * Rules.LandCostPerTile;
            _hospitals[player] = new Hospital(player, Rules.StartCash, Rules.StartReputation, land + Hospital.StartValueBesidesLand, Rules.InterestRate, Rules.OverdraftDifferential);
            _hospitals[player].Policies = new HospitalPolicies(Rules.Tiredness.Tired);
            _hospitals[player].FreeBuild = Rules.FreeBuild;

            _hospitals[player].ScheduleNextDisaster(Random, Rules.Difficulty);
            _hospitals[player].Vips.ScheduleNext(Random, Rules.MayorLaunch, today: 0);
            Emergencies.Start(this, _hospitals[player]);

            // CorsixTH's Hospital:Hospital: the rooms of the level whose objects are all there from the start.
            foreach (var room in Content.Rooms)
            {
                if (Rules.IsRoomDiscoveredAtStart(room))
                {
                    _hospitals[player].DiscoverRoom(room.Id);
                }
            }

            _hospitals[player].Research = Research.ResearchDepartment.Create(Rules, _hospitals[player]);
        }

        // World:World adds the computer opponents after the players' hospitals.
        foreach (var competitor in Rules.Competitors)
        {
            _competitors.Add(new CompetitorHospital(competitor.Number, competitor.Name));
        }

        // Then Hospital:initStaff, before the first candidates for hire.
        for (var player = 0; player < _hospitals.Length; player++)
        {
            StaffHiring.HireStartStaff(this, player);
        }

        _parcelAdjacency = ParcelAdjacency(map);
        MakeAvailableStaff(0);

        // CorsixTH's World:World plans the first earthquake after the candidates for hire and before the arrivals.
        Earthquakes = Earthquakes.Start(Rules.Earthquakes, Random);
        Arrivals = PatientArrivals.Start(this);
    }

    private Simulation(LevelMap map, SimRandom random, GameContent content, LevelRules rules)
    {
        Map = map;
        Temperatures = new TileTemperatures(map.Width, map.Height);
        Random = random;
        Content = content;
        Rules = rules;
        _hospitals = new Hospital[map.PlayerCount];
    }

    /// <summary>The content definitions the rules refer to. Not part of the saved state.</summary>
    public GameContent Content { get; }

    /// <summary>The level's money settings. Part of the saved state.</summary>
    public LevelRules Rules { get; }

    /// <summary>How warm each tile is. Part of the saved state.</summary>
    public TileTemperatures Temperatures { get; private set; } = null!;

    /// <summary>The world's earthquakes. Part of the saved state.</summary>
    public Earthquakes Earthquakes { get; private set; } = null!;

    /// <summary>When patients arrive. Part of the saved state.</summary>
    public PatientArrivals Arrivals { get; private set; } = null!;

    /// <summary>Each player's hospital, by player (0-based).</summary>
    public IReadOnlyList<Hospital> Hospitals => _hospitals;

    /// <summary>
    /// The computer opponents' hospitals (CorsixTH's <c>AIHospital</c>s), which come after the players' in the turns
    /// for patients.
    /// </summary>
    public IReadOnlyList<CompetitorHospital> Competitors => _competitors;

    /// <summary>
    /// The candidates for hire of a class, in the order the hire window lists them. Every hospital
    /// hires from the same candidates, as in CorsixTH's <c>world.available_staff</c>.
    /// </summary>
    public IReadOnlyList<StaffProfile> AvailableStaff(StaffClass staffClass) => _availableStaff[(int)staffClass];

    /// <summary>Number of ticks completed since the game started.</summary>
    public long Tick { get; private set; }

    /// <summary>
    /// The hour of the game's calendar: the ticks so far plus the hours the end-of-month and end-of-year cheats skipped
    /// (CorsixTH's <c>game_date</c>).
    /// </summary>
    public long Hours => Tick + SkippedHours;

    /// <summary>The game date: each tick is one game hour, as in CorsixTH.</summary>
    public GameDate Date => GameDate.FromHours(Hours);

    /// <summary>Hours of the calendar skipped by cheats (<see cref="SkipToLastHour"/>).</summary>
    public long SkippedHours { get; private set; }

    public LevelMap Map { get; }

    /// <summary>The only source of randomness the simulation may use.</summary>
    public SimRandom Random { get; private set; }

    /// <summary>All entities, in increasing id order.</summary>
    public IReadOnlyList<Entity> Entities => _entities;

    /// <summary>The banknotes floating up from patients who have just paid. Not part of the saved state.</summary>
    public IReadOnlyList<FloatingDollarSign> FloatingDollarSigns => _floatingDollarSigns;

    /// <summary>Map objects waiting for their parcel to get an owner, in map order.</summary>
    public IReadOnlyList<MapObjectPlacement> DelayedMapObjects => _delayedObjects;

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

        // CorsixTH's World:onTick ticks the earthquake, then handles the end of a day before the hour that starts the next one.
        Earthquakes.Tick(Rules.Earthquakes, Random);
        if (Date.Day != GameDate.FromHours(Hours + 1).Day)
        {
            OnEndDay();
        }

        Arrivals.SpawnPatients(this);

        // Entities added during this loop are updated from the next tick on.
        var count = _entities.Count;
        for (var i = 0; i < count && i < _entities.Count; i++)
        {
            _entities[i].Update(this);
        }

        // The opening watch counts down after the entities, as CorsixTH's UI windows tick after them.
        // So do the faxes, which take their default choice when they run out.
        foreach (var hospital in _hospitals)
        {
            hospital.TickOpeningWatch();
            VipVisits.TickInvitation(this, hospital);
            Emergencies.Tick(this, hospital);
            Epidemics.Tick(this, hospital);
        }

        // People who have left the map go now, as CorsixTH's _flushDestroyedEntities.
        // So do construction sites that are done, litter swept up or displaced and rats gone: removing them during the
        // loop would skip an entity.
        _entities.RemoveAll(entity => entity is Humanoid { IsDespawning: true } or ConstructionSite { IsDone: true } or Litter { IsRemoved: true } or Rat { IsGone: true });

        // Then the heat spreads, at the new hour's month (CorsixTH's World:onTick after the entities).
        UpdateTemperatures(GameDate.FromHours(Hours + 1).Month);

        // World:onTick: every floating dollar sign rises a step and goes when its lifetime is up.
        _floatingDollarSigns.RemoveAll(sign => sign.Age(Tick + 1) >= FloatingDollarSign.Lifetime);

        Tick++;
    }

    /// <summary>
    /// <c>World:newFloatingDollarSign</c>: a banknote with the amount floats up from the patient, except in free build
    /// mode.
    /// </summary>
    internal void ShowFloatingDollarSign(Humanoid patient, int amount, bool insurance)
    {
        if (Rules.FreeBuild)
        {
            return;
        }

        _floatingDollarSigns.Add(new FloatingDollarSign(patient.HospitalPlayer, patient.Tile, amount, insurance, Tick));
    }

    /// <summary>
    /// One hour of heat. CorsixTH heats every radiator by the first hospital's setting; here a radiator goes by the
    /// hospital that owns its parcel, or by the first hospital on land nobody owns.
    /// </summary>
    private void UpdateTemperatures(int month)
    {
        _radiatorTemperatures ??= new ushort[Map.Width * Map.Height];
        Array.Clear(_radiatorTemperatures);
        foreach (var entity in _entities)
        {
            if (entity is RoomObject { ObjectType: Radiator } radiator)
            {
                var owner = Map.GetParcelOwner(Map[radiator.Tile.X, radiator.Tile.Y].Parcel) - 1;
                var hospital = owner >= 0 && owner < _hospitals.Length ? _hospitals[owner] : _hospitals[0];
                _radiatorTemperatures[(radiator.Tile.Y * Map.Width) + radiator.Tile.X] = TileTemperatures.RadiatorTemperature(hospital.RadiatorHeat);
            }
        }

        Temperatures.Update(Map, TileTemperatures.OutsideTemperature(month), _radiatorTemperatures);
    }

    /// <summary>The player's staff at work in a research room (<c>researchCost</c>'s <c>staff_member_set</c>).</summary>
    private int Researchers(int player)
    {
        var count = 0;
        foreach (var entity in _entities)
        {
            if (entity is Humanoid { Behaviour: People.IRoomStaff { RoomId: > 0 } staff, Employer: var employer } && employer == player
                && FindEntity(staff.RoomId) is Room { RoomType: "research" })
            {
                count++;
            }
        }

        return count;
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
        RegisterObserver(entity);
        return entity;
    }

    /// <summary>
    /// Places the objects of a map file, as CorsixTH does at level start: objects on parcels
    /// without an owner wait until the parcel gets one.
    /// </summary>
    public void PlaceMapObjects(IEnumerable<MapObjectPlacement> placements)
    {
        foreach (var placement in placements)
        {
            var parcel = Map[placement.Tile.X, placement.Tile.Y].Parcel;
            if (parcel != 0 && Map.GetParcelOwner(parcel) == 0)
            {
                _delayedObjects.Add(placement);
            }
            else
            {
                CreateMapObject(placement);
            }
        }

        LinkEntranceDoors();
        Map.UpdateShadows();
    }

    /// <summary>Gives a parcel to a player (1-based; 0 for nobody) and places objects that were waiting for it.</summary>
    public void SetParcelOwner(int parcel, int owner)
    {
        CloseRoomsAtSplitTiles(Map.SetParcelOwner(parcel, owner));
        if (owner == 0)
        {
            return;
        }

        var ready = new List<MapObjectPlacement>();
        for (var i = 0; i < _delayedObjects.Count; i++)
        {
            var placement = _delayedObjects[i];
            if (Map[placement.Tile.X, placement.Tile.Y].Parcel == parcel)
            {
                ready.Add(placement);
                _delayedObjects.RemoveAt(i--);
            }
        }

        foreach (var placement in ready)
        {
            CreateMapObject(placement);
        }

        LinkEntranceDoors();
        Map.UpdateShadows();
    }

    /// <summary>
    /// Replaces the candidates for hire with new random ones for <paramref name="month"/> of the game,
    /// as CorsixTH's <c>World:makeAvailableStaff</c> does at the start and every month.
    /// </summary>
    public void MakeAvailableStaff(int month)
    {
        // The super doctor cheat of the first (local) hospital (World:getLocalPlayerHospital).
        var superDoctor = Hospitals.Count > 0 && (Hospitals[0].ActiveCheats & ToggleCheats.SuperDoctor) != 0;
        foreach (var staffClass in StaffClasses.HireOrder)
        {
            var pool = _availableStaff[(int)staffClass];
            pool.Clear();
            var count = Rules.Staff.AvailableCount(staffClass, month);
            for (var i = 0; i < count; i++)
            {
                pool.Add(StaffProfile.CreateRandom(staffClass, month, Rules.Staff, Random, superDoctor));
            }
        }
    }

    /// <summary>Takes a candidate out of the list for hire.</summary>
    internal StaffProfile TakeAvailableStaff(StaffClass staffClass, int index)
    {
        var pool = _availableStaff[(int)staffClass];
        var profile = pool[index];
        pool.RemoveAt(index);
        return profile;
    }

    /// <summary>Tells the entity watching <paramref name="tile"/>, if any, that its occupant count changed.</summary>
    internal void NotifyOccupantChange(TilePosition tile, int delta)
    {
        if (_occupantObservers.TryGetValue(tile, out var id) && FindEntity(id) is EntranceDoor door)
        {
            door.OnOccupantChange(this, delta);
        }
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

        var entity = _entities[index];
        _entities.RemoveAt(index);
        if (entity is EntranceDoor door)
        {
            foreach (var tile in door.ObservedTiles)
            {
                if (_occupantObservers.TryGetValue(tile, out var observer) && observer == id)
                {
                    _occupantObservers.Remove(tile);
                }
            }
        }

        return true;
    }

    /// <summary>Writes the complete state.</summary>
    public void Save(Stream stream) => Write(new SimWriter(stream), includeInputs: true);

    /// <summary>Restores a simulation written by <see cref="Save"/>, using the given content definitions.</summary>
    /// <exception cref="InvalidDataException">The data is not a valid simulation state.</exception>
    public static Simulation Load(Stream stream, GameContent? content = null)
    {
        var reader = new SimReader(stream);
        if (reader.ReadUInt32() != Magic)
        {
            throw new InvalidDataException("Data is not a CorsixNet simulation state.");
        }

        var version = reader.ReadInt32();
        if (version is < OldestFormatVersion or > FormatVersion)
        {
            throw new InvalidDataException($"Simulation state version {version} is not supported (expected {FormatVersion}).");
        }

        reader.Version = version;

        var tick = reader.ReadInt64();
        var skippedHours = reader.ReadInt64();
        var random = SimRandom.FromState(reader.ReadUInt64(), reader.ReadUInt64());
        var nextEntityId = reader.ReadInt32();
        var nextSequence = reader.ReadInt64();
        var map = LevelMap.Read(reader);
        var rules = LevelRules.Read(reader);

        var simulation = new Simulation(map, random, content ?? GameContent.Empty, rules)
        {
            Tick = tick,
            SkippedHours = skippedHours,
            NextEntityId = nextEntityId,
            NextSequence = nextSequence,
        };

        for (var player = 0; player < map.PlayerCount; player++)
        {
            simulation._hospitals[player] = Hospital.Read(reader, player);
            simulation._hospitals[player].FreeBuild = rules.FreeBuild;
        }

        var competitorCount = reader.ReadCount(rules.Competitors.Count);
        for (var i = 0; i < competitorCount; i++)
        {
            simulation._competitors.Add(CompetitorHospital.Read(reader));
        }

        simulation.Temperatures = TileTemperatures.Read(reader, map.Width, map.Height);
        simulation.Earthquakes = Earthquakes.Read(reader);
        simulation.Arrivals = PatientArrivals.Read(reader);

        foreach (var pool in simulation._availableStaff)
        {
            var count = reader.ReadCount(MaxAvailableStaff);
            for (var i = 0; i < count; i++)
            {
                pool.Add(StaffProfile.Read(reader));
            }
        }

        var entityCount = reader.ReadCount(MaxEntities);
        for (var i = 0; i < entityCount; i++)
        {
            var entity = Entity.Read(reader);
            if (entity.Id >= nextEntityId || (simulation._entities.Count > 0 && entity.Id <= simulation._entities[^1].Id))
            {
                throw new InvalidDataException($"Entity id {entity.Id} is out of order.");
            }

            simulation._entities.Add(entity);
            simulation.RegisterObserver(entity);
        }

        var commandCount = reader.ReadCount(MaxPendingCommands);
        for (var i = 0; i < commandCount; i++)
        {
            simulation.Schedule(ScheduledCommand.Read(reader));
        }

        var delayedCount = reader.ReadCount(map.Width * map.Height);
        for (var i = 0; i < delayedCount; i++)
        {
            simulation._delayedObjects.Add(MapObjectPlacement.Read(reader));
        }

        simulation._parcelAdjacency = new bool[map.ParcelCount * map.ParcelCount];
        for (var i = 0; i < simulation._parcelAdjacency.Length; i++)
        {
            simulation._parcelAdjacency[i] = reader.ReadBool();
        }

        return simulation;
    }

    /// <summary>
    /// Hash of the game state, for detecting desyncs between machines.
    /// </summary>
    /// <remarks>
    /// Pending commands and the next sequence number are inputs, not state: a client behind the
    /// server may already hold commands for future ticks. They are left out, so machines at the
    /// same tick agree however far ahead their command queues reach.
    /// </remarks>
    public ulong ComputeStateHash()
    {
        using var hash = new StateHash();
        Write(new SimWriter(hash), includeInputs: false);
        return hash.Value;
    }

    /// <summary>
    /// <c>World:setEndMonth</c> and <c>setEndYear</c>: the calendar jumps to the last hour before
    /// <paramref name="next"/>, so that this tick ends the month or year. An emergency planned in the hours skipped is
    /// replanned (<c>World:wasEmergencySkipped</c>).
    /// </summary>
    internal void SkipToLastHour(GameDate next)
    {
        var previous = Hours;
        var target = next.ToHours() - 1;
        if (target <= previous)
        {
            return;
        }

        SkippedHours += target - previous;
        foreach (var hospital in _hospitals)
        {
            Emergencies.OnCalendarSkipped(this, hospital, previous, target);
        }
    }

    private void Write(SimWriter writer, bool includeInputs)
    {
        writer.WriteUInt32(Magic);
        writer.WriteInt32(FormatVersion);
        writer.WriteInt64(Tick);
        writer.WriteInt64(SkippedHours);
        writer.WriteUInt64(Random.State);
        writer.WriteUInt64(Random.Increment);
        writer.WriteInt32(NextEntityId);
        if (includeInputs)
        {
            writer.WriteInt64(NextSequence);
        }

        Map.Write(writer);
        Rules.Write(writer);
        foreach (var hospital in _hospitals)
        {
            hospital.Write(writer);
        }

        writer.WriteInt32(_competitors.Count);
        foreach (var competitor in _competitors)
        {
            competitor.Write(writer);
        }

        Temperatures.Write(writer);
        Earthquakes.Write(writer);
        Arrivals.Write(writer);

        foreach (var pool in _availableStaff)
        {
            writer.WriteInt32(pool.Count);
            foreach (var profile in pool)
            {
                profile.Write(writer);
            }
        }

        writer.WriteInt32(_entities.Count);
        foreach (var entity in _entities)
        {
            entity.Write(writer);
        }

        if (includeInputs)
        {
            writer.WriteInt32(_pending.Count);
            foreach (var command in _pending)
            {
                command.Write(writer);
            }
        }

        writer.WriteInt32(_delayedObjects.Count);
        foreach (var placement in _delayedObjects)
        {
            placement.Write(writer);
        }

        foreach (var adjacent in _parcelAdjacency)
        {
            writer.WriteBool(adjacent);
        }
    }

    /// <summary>
    /// <c>level_map::is_parcel_purchasable</c>: a parcel nobody owns, other than the outside (0), that touches one of
    /// the player's parcels or the outside.
    /// </summary>
    /// <param name="parcel">The parcel.</param>
    /// <param name="player">The player, 0-based.</param>
    public bool IsParcelPurchasable(int parcel, int player)
    {
        var count = Map.ParcelCount;
        if (parcel <= 0 || parcel >= count || Map.GetParcelOwner(parcel) != 0)
        {
            return false;
        }

        for (var other = 0; other < count; other++)
        {
            if ((other == 0 || Map.GetParcelOwner(other) == player + 1) && _parcelAdjacency[(parcel * count) + other])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What a parcel costs (CorsixTH's <c>Map:getParcelPrice</c>): its tiles at the level's land cost per tile; nothing in
    /// free build mode (<c>Hospital:purchasePlot</c>).
    /// </summary>
    public long ParcelPrice(int parcel) => Rules.FreeBuild ? 0 : (long)Map.ParcelTileCount(parcel) * Rules.LandCostPerTile;

    /// <summary>
    /// <c>Hospital:purchasePlot</c>: the player buys a parcel they may buy and can afford; it adds its price to the
    /// hospital's value.
    /// </summary>
    internal void PurchaseParcel(int parcel, int player)
    {
        if (player < 0 || player >= _hospitals.Length || !IsParcelPurchasable(parcel, player))
        {
            return;
        }

        var hospital = _hospitals[player];
        var price = ParcelPrice(parcel);
        if (price <= hospital.Balance)
        {
            SetParcelOwner(parcel, player + 1);
            hospital.Spend(Hours, checked((int)price), TransactionKind.BuyLand, parcel.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// <c>level_map::make_adjacency_matrix</c>: every parcel touches itself, and two parcels touch where passable tiles
    /// of each are side by side.
    /// </summary>
    private static bool[] ParcelAdjacency(LevelMap map)
    {
        var count = map.ParcelCount;
        var adjacency = new bool[count * count];
        for (var i = 0; i < count; i++)
        {
            adjacency[(i * count) + i] = true;
        }

        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                if (x > 0)
                {
                    TestAdjacent(map, adjacency, map.OriginalTile(x, y), map.OriginalTile(x - 1, y));
                }

                if (y > 0)
                {
                    TestAdjacent(map, adjacency, map.OriginalTile(x, y), map.OriginalTile(x, y - 1));
                }
            }
        }

        return adjacency;

        static void TestAdjacent(LevelMap map, bool[] adjacency, MapTile tile, MapTile other)
        {
            var count = map.ParcelCount;
            if (tile.Parcel != other.Parcel && tile.Parcel < count && other.Parcel < count && tile.Has(TileFlags.Passable) && other.Has(TileFlags.Passable))
            {
                adjacency[(tile.Parcel * count) + other.Parcel] = true;
                adjacency[tile.Parcel + (other.Parcel * count)] = true;
            }
        }
    }

    /// <summary>
    /// The end of the current day, as CorsixTH's <c>World:onTick</c> handles it: the next day's patient arrivals
    /// get their hours (<c>World:onEndDay</c>). At the end of a month every hospital pays its staff's wages
    /// and receives what insurance companies owed from two months ago (<c>Hospital:onEndMonth</c>), at the end of
    /// a year prices go up (<c>Hospital:onEndYear</c>), then the next month's arrivals are planned and the
    /// candidates for hire are replaced (<c>World:onEndMonth</c>).
    /// </summary>
    private void OnEndDay()
    {
        var date = Date;
        var radiators = new int[_hospitals.Length];
        var handymen = new int[_hospitals.Length];
        foreach (var entity in _entities)
        {
            if (entity is RoomObject { ObjectType: Radiator } radiator)
            {
                var owner = Map.GetParcelOwner(Map[radiator.Tile.X, radiator.Tile.Y].Parcel) - 1;
                if (owner >= 0 && owner < radiators.Length)
                {
                    radiators[owner]++;
                }
            }
            else if (entity is Humanoid { Profile.Class: StaffClass.Handyman, Employer: var employer } && employer >= 0 && employer < handymen.Length)
            {
                handymen[employer]++;
            }
        }

        foreach (var hospital in _hospitals)
        {
            hospital.OnEndDay();

            // Hospital:onEndDay: then the day's research cost, by the researchers at work.
            hospital.Research.AccumulateCost(Researchers(hospital.Player));
            hospital.OnEndDayHeating(Random, radiators[hospital.Player], handymen[hospital.Player], date.DaysInMonth, Rules.Difficulty);

            // PlayerHospital's end of the day: rat holes, then rats.
            Rats.OnEndDay(this, hospital);
        }

        // Then each person's and plant's day, as World:onEndDay's entity:tickDay().
        foreach (var entity in _entities)
        {
            if (entity is Humanoid humanoid)
            {
                (humanoid.Behaviour as VipBehaviour)?.OnEndDay(this, humanoid);
                Feelings.OnEndDay(this, humanoid);

                // Doctor:tickDay: a researcher in the research department researches, 1,550 points and 1,000 more for full skill.
                if (humanoid is { Behaviour: DoctorBehaviour { RoomId: > 0 } doctor, Profile: { IsResearcher: true } profile, Employer: var employer, IsFired: false }
                    && employer >= 0 && employer < _hospitals.Length
                    && FindEntity(doctor.RoomId) is Room { RoomType: RoomVisits.ResearchRoom } research && research.Area.Contains(humanoid.Tile))
                {
                    _hospitals[employer].Research.AddPoints(this, _hospitals[employer], 1550 + (1000 * profile.Skill / StaffProfile.MaxSkill));
                }
            }
            else if (entity is RoomObject { Plant: { } plant } placed)
            {
                plant.OnEndDay(Temperatures[placed.Tile.X, placed.Tile.Y] / (decimal)TileTemperatures.Max);
            }
        }

        // World:onEndDay: then perhaps a VIP asks to visit, before the earthquakes.
        foreach (var hospital in _hospitals)
        {
            VipVisits.OnEndDay(this, hospital);
        }

        Earthquakes.OnEndDay(date);

        // World:onEndDay: then perhaps an emergency.
        foreach (var hospital in _hospitals)
        {
            Emergencies.OnEndDay(this, hospital);
        }

        Arrivals.OnEndDay(this);
        if (!date.IsLastDayOfMonth)
        {
            return;
        }

        var wages = new int[_hospitals.Length];
        foreach (var entity in _entities)
        {
            if (entity is Humanoid { Profile: { } profile, Employer: var employer, IsFired: false } && employer >= 0 && employer < wages.Length)
            {
                wages[employer] += profile.Wage;
            }
        }

        foreach (var hospital in _hospitals)
        {
            // PlayerHospital:onEndMonth looks for a win at the end of March, June and September, before the wages.
            if (date.Month is 3 or 6 or 9)
            {
                CheckEndConditions(hospital, mayLose: false);
            }

            hospital.PayWages(Hours, wages[hospital.Player]);
            hospital.PayHeating(Hours);
            hospital.PayInterest(Hours);
            hospital.PayResearch(Hours);
            hospital.RaiseSalary(Rules.ScoreMaxIncrease);
            hospital.PayInsurance(Hours);

            // Hospital:onEndMonth: equipment the level brings this month.
            hospital.Research.CheckAutomaticDiscovery(this, hospital, date.MonthOfGame);
            hospital.RecordMonth(wages[hospital.Player]);
            if (date.IsLastDayOfYear)
            {
                hospital.OnEndYear(Hours, Random, Rules.Awards);

                // Hospital:onEndYear: at the end of the third year of level 3 the salary rises a lot, as in the original.
                if (date.Year == 3 && Rules.LevelNumber == 3)
                {
                    hospital.PlayerSalary += Random.NextInclusive(8000, 20000);
                }

                // World:onEndYear looks for a loss, and the annual report on closing for a win. CorsixTH means the
                // loss to come after the trophies and awards but pays them only when the report closes; here both
                // checks come after them.
                CheckEndConditions(hospital, mayLose: true);
            }
        }

        if (date.IsLastDayOfYear)
        {
            foreach (var competitor in _competitors)
            {
                competitor.OnEndYear();
            }
        }

        Arrivals.OnEndMonth(this);
        MakeAvailableStaff(date.MonthOfGame);
    }

    /// <summary>
    /// <c>World:checkIfGameWon</c>, or with <paramref name="mayLose"/> the year end's check for a loss followed by one
    /// for a win. A hospital that has won or lost is not checked again.
    /// </summary>
    private void CheckEndConditions(Hospital hospital, bool mayLose)
    {
        if (hospital.Outcome != EndOutcome.None)
        {
            return;
        }

        var (outcome, lostTo) = Rules.EndConditions.Check(this, hospital);
        if (outcome == EndOutcome.Won || (mayLose && outcome == EndOutcome.Lost))
        {
            hospital.End(outcome, lostTo, Random);
        }
    }

    private void CreateMapObject(MapObjectPlacement placement)
    {
        if (placement.ObjectType is not (EntranceDoor.LeftObjectType or EntranceDoor.RightObjectType))
        {
            // CorsixTH only creates entrance doors from map files and ignores other types.
            return;
        }

        var orientation = placement.Direction % 2 == 0 ? EdgeOrientation.North : EdgeOrientation.West;
        var door = AddEntity(id => new EntranceDoor(id, placement.Tile, orientation, placement.ObjectType == EntranceDoor.RightObjectType));
        door.Place(Map);
    }

    /// <summary>
    /// CorsixTH's <c>Map:setPlotOwner</c>: where a divider wall came down beside a room, the room gets its own wall
    /// there, seen from inside or outside.
    /// </summary>
    private void CloseRoomsAtSplitTiles(IReadOnlyList<TilePosition> splitTiles)
    {
        if (splitTiles.Count == 0)
        {
            return;
        }

        foreach (var (x, y) in splitTiles)
        {
            ref var tile = ref Map[x, y];
            var north = y > 0 ? Map[x, y - 1] : tile;
            var west = x > 0 ? Map[x - 1, y] : tile;
            if (tile.NorthWall == 0)
            {
                if (RoomWalls(tile, north) is { } inside)
                {
                    tile.NorthWall = inside.InsideNorth;
                }
                else if (RoomWalls(north, tile) is { } outside)
                {
                    tile.NorthWall = outside.OutsideNorth;
                }
            }

            if (tile.WestWall == 0)
            {
                if (RoomWalls(tile, west) is { } inside)
                {
                    tile.WestWall = inside.InsideWest;
                }
                else if (RoomWalls(west, tile) is { } outside)
                {
                    tile.WestWall = outside.OutsideWest;
                }
            }
        }

        Map.UpdatePathfinding();
        Map.UpdateShadows();

        WallSet? RoomWalls(MapTile roomTile, MapTile neighbour) =>
            roomTile.RoomId != 0 && roomTile.Parcel != neighbour.Parcel && FindEntity(roomTile.RoomId) is Room room
                ? Content.FindRoom(room.RoomType)?.Walls
                : null;
    }

    /// <summary>Pairs each unpaired left door half with the right half east or south of it.</summary>
    private void LinkEntranceDoors()
    {
        foreach (var entity in _entities)
        {
            if (entity is not EntranceDoor { IsRight: false, PartnerId: 0 } left)
            {
                continue;
            }

            var right = _entities.OfType<EntranceDoor>().FirstOrDefault(d =>
                d is { IsRight: true, PartnerId: 0 } && (d.Tile == left.Tile with { X = left.Tile.X + 1 } || d.Tile == left.Tile with { Y = left.Tile.Y + 1 }));
            if (right is not null)
            {
                left.PartnerId = right.Id;
                right.PartnerId = left.Id;
            }
        }
    }

    private void RegisterObserver(Entity entity)
    {
        if (entity is EntranceDoor door)
        {
            foreach (var tile in door.ObservedTiles)
            {
                _occupantObservers[tile] = door.Id;
            }
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
