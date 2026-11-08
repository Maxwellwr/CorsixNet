using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.Pathfinding;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Content.Tests;

/// <summary>Patients arriving, queueing at reception and seeing the GP, with the original game's content.</summary>
public class PatientFlowTests
{
    private const int Size = 24;
    private static readonly TileRect GpArea = new(5, 5, 4, 4);

    [Fact]
    public void NoPatientsComeWithoutAStaffedReceptionDesk()
    {
        var simulation = NewHospital();

        StepFor(simulation, 40 * GameDate.HoursPerDay);

        Assert.DoesNotContain(simulation.Entities, entity => entity is Humanoid { Behaviour: PatientBehaviour });
    }

    [Fact]
    public void AReceptionistGoesToANewDeskAndStandsOnIt()
    {
        var simulation = NewHospital();
        Run(simulation, new HireStaffCommand(StaffClass.Receptionist, 0, new(3, 20)));
        var desk = PlaceReceptionDesk(simulation);
        var receptionist = simulation.Entities.OfType<Humanoid>().Single(h => h.Type == HumanoidType.Receptionist);

        Assert.Equal(receptionist.Id, desk.Desk!.ReservedForId);
        StepFor(simulation, 400);

        Assert.Equal(receptionist.Id, desk.Desk.ReceptionistId);
        Assert.Equal(0, desk.Desk.ReservedForId);
        Assert.Equal(desk.Tile, receptionist.Tile);
    }

    [Fact]
    public void ReceptionDesksAreBoughtWhenPlaced()
    {
        var simulation = NewHospital();
        var balance = simulation.Hospitals[0].Balance;

        PlaceReceptionDesk(simulation);

        Assert.Equal(balance - simulation.Rules.ObjectCost(ReceptionDesks.ObjectId), simulation.Hospitals[0].Balance);
    }

    [Fact]
    public void PatientsQueueAtReceptionSeeTheGpAndGoHome()
    {
        var simulation = NewStaffedHospital();
        var seenByGp = new HashSet<int>();
        var everArrived = new HashSet<int>();
        var maxReceptionQueue = 0;

        for (var tick = 0; tick < 3 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is PatientBehaviour patient)
                {
                    everArrived.Add(humanoid.Id);
                    if (patient.IsBeingSeen)
                    {
                        seenByGp.Add(humanoid.Id);
                    }
                }
            }

            maxReceptionQueue = Math.Max(maxReceptionQueue, Desk(simulation).Desk!.Queue.Count);
        }

        var stillHere = simulation.Entities.OfType<Humanoid>().Where(h => h.Behaviour is PatientBehaviour).Select(h => h.Id).ToHashSet();
        var gp = simulation.Entities.OfType<Room>().Single();

        Assert.True(everArrived.Count >= 10, $"Only {everArrived.Count} patients arrived.");
        Assert.True(Desk(simulation).Desk!.Queue.VisitorCount >= 10);
        Assert.True(gp.DoorQueue!.VisitorCount >= 5, $"Only {gp.DoorQueue.VisitorCount} patients went into the GP's office.");
        Assert.True(seenByGp.Count >= 5);
        Assert.Contains(seenByGp, id => !stillHere.Contains(id));
        Assert.True(maxReceptionQueue >= 1);
    }

    [Fact]
    public void AQueueLinesUpOnNeighbouringTiles()
    {
        var simulation = NewHospital();
        var map = simulation.Map;
        var start = new TilePosition(12, 12);
        var pathfinder = new Pathfinder();
        var tiles = Enumerable.Range(0, 4).Select(i => pathfinder.TryFindIdleTile(map, start, i, out var tile) ? tile : default).ToList();

        Assert.Equal(start, tiles[0]);
        Assert.Equal(4, tiles.Distinct().Count());
        for (var i = 1; i < tiles.Count; i++)
        {
            Assert.Equal(1, Math.Abs(tiles[i].X - tiles[i - 1].X) + Math.Abs(tiles[i].Y - tiles[i - 1].Y));
        }
    }

    [Fact]
    public void ASavedGameWithPatientsContinuesIdentically()
    {
        var simulation = NewStaffedHospital();
        StepFor(simulation, 45 * GameDate.HoursPerDay);
        Assert.Contains(simulation.Entities, entity => entity is Humanoid { Behaviour: PatientBehaviour });

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());

        StepFor(simulation, 20 * GameDate.HoursPerDay);
        StepFor(loaded, 20 * GameDate.HoursPerDay);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    [Fact]
    public void PoissonRandomNumbersHaveTheRequestedMean()
    {
        var random = new SimRandom(5);
        var total = 0;
        for (var i = 0; i < 2000; i++)
        {
            total += PatientArrivals.PoissonRandom(random, 7.75m);
        }

        Assert.InRange(total / 2000m, 7.4m, 8.1m);
    }

    private static RoomObject Desk(Simulation simulation) => simulation.Entities.OfType<RoomObject>().Single(o => o.Desk is not null);

    /// <summary>A hospital with a GP's office, a doctor, a receptionist and a staffed reception desk.</summary>
    private static Simulation NewStaffedHospital()
    {
        var simulation = NewHospital();
        Run(simulation, new BuildRoomCommand(new RoomPlan("gp", GpArea, new DoorPlacement(new(6, GpArea.Bottom), EdgeOrientation.North))));
        var room = simulation.Entities.OfType<Room>().Single();
        foreach (var type in new[] { "desk", "cabinet", "chair" })
        {
            PlaceAnywhere(simulation, type, GpArea);
        }

        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, new(15, 15)));
        Run(simulation, new HireStaffCommand(StaffClass.Receptionist, 0, new(16, 15)));
        PlaceReceptionDesk(simulation);
        return simulation;
    }

    private static RoomObject PlaceReceptionDesk(Simulation simulation)
    {
        PlaceAnywhere(simulation, ReceptionDesks.ObjectId, new TileRect(12, 14, 4, 4));
        return Desk(simulation);
    }

    private static void PlaceAnywhere(Simulation simulation, string type, TileRect area)
    {
        for (var y = area.Y; y < area.Bottom; y++)
        {
            for (var x = area.X; x < area.Right; x++)
            {
                foreach (var orientation in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
                {
                    if (ObjectPlacer.Check(simulation, type, new(x, y), orientation, 0) == ObjectPlacementProblem.None)
                    {
                        Run(simulation, new PlaceObjectCommand(type, new(x, y), orientation));
                        return;
                    }
                }
            }
        }

        Assert.Fail($"Found no place for {type}.");
    }

    /// <summary>
    /// A map of grass with a hospital building in the middle (tiles 2 to 21), all of it the player's;
    /// patients come from the edges.
    /// </summary>
    private static Simulation NewHospital()
    {
        var map = new LevelMap(Size, Size, playerCount: 1);
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                ref var tile = ref map[x, y];
                tile.Floor = 1;
                tile.Set(TileFlags.Passable, true);
                if (x is >= 2 and < Size - 2 && y is >= 2 and < Size - 2)
                {
                    tile.Parcel = 1;
                    tile.Set(TileFlags.Buildable | TileFlags.Hospital | TileFlags.BuildableNorth | TileFlags.BuildableEast | TileFlags.BuildableSouth | TileFlags.BuildableWest, true);
                }
            }
        }

        map.UpdatePathfinding();
        map.UpdateShadows();
        var rules = new LevelRules(1_000_000, [KeyValuePair.Create("gp", 2500)], [KeyValuePair.Create(ReceptionDesks.ObjectId, 600)]);
        return new Simulation(map, seed: 3, BaseGameContent.Create(), rules);
    }

    private static void Run(Simulation simulation, Command command)
    {
        simulation.Schedule(0, command);
        simulation.Step();
    }

    private static void StepFor(Simulation simulation, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            simulation.Step();
        }
    }
}
