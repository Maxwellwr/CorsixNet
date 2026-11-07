using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;

namespace CorsixNet.Sim.Tests;

public class EntranceDoorTests
{
    // A north-facing door pair: left half at (2, 2), right half (the master) at (3, 2).
    private static readonly MapObjectPlacement LeftNorth = new(new(2, 2), EntranceDoor.LeftObjectType, 0);
    private static readonly MapObjectPlacement RightNorth = new(new(3, 2), EntranceDoor.RightObjectType, 0);

    [Fact]
    public void PlacesAndPairsDoorHalves()
    {
        var simulation = NewSimulation();

        simulation.PlaceMapObjects([LeftNorth, RightNorth]);

        var doors = simulation.Entities.OfType<EntranceDoor>().ToList();
        Assert.Equal(2, doors.Count);
        var (left, right) = (doors[0], doors[1]);
        Assert.False(left.IsRight);
        Assert.True(right.IsRight);
        Assert.Equal(right.Id, left.PartnerId);
        Assert.Equal(left.Id, right.PartnerId);
        Assert.All(doors, door => Assert.Equal(EdgeOrientation.North, door.Orientation));
    }

    [Fact]
    public void MarksDoorTilesOnTheMap()
    {
        var simulation = NewSimulation();

        simulation.PlaceMapObjects([LeftNorth, RightNorth]);

        var map = simulation.Map;
        Assert.True(map[3, 2].Has(TileFlags.TallNorth));
        Assert.True(map[2, 2].Has(TileFlags.TallNorth));
        foreach (var (x, y) in new[] { (2, 2), (3, 2), (2, 1), (3, 1), (4, 2), (4, 1) })
        {
            Assert.False(map[x, y].Has(TileFlags.Buildable), $"({x}, {y}) should not be buildable");
        }

        Assert.True(map[1, 1].Has(TileFlags.Buildable));
    }

    [Fact]
    public void WestDirectionByteMakesWestDoors()
    {
        var simulation = NewSimulation();

        simulation.PlaceMapObjects([new(new(2, 2), EntranceDoor.RightObjectType, 1)]);

        var door = Assert.IsType<EntranceDoor>(Assert.Single(simulation.Entities));
        Assert.Equal(EdgeOrientation.West, door.Orientation);
        Assert.True(simulation.Map[2, 2].Has(TileFlags.TallWest));
    }

    [Fact]
    public void IgnoresUnknownObjectTypes()
    {
        var simulation = NewSimulation();

        simulation.PlaceMapObjects([new(new(2, 2), 12, 0)]);

        Assert.Empty(simulation.Entities);
        Assert.Empty(simulation.DelayedMapObjects);
    }

    [Fact]
    public void WaitsForParcelOwner()
    {
        var simulation = NewSimulation();
        simulation.Map[2, 2].Parcel = 1;
        simulation.Map[3, 2].Parcel = 1;

        simulation.PlaceMapObjects([LeftNorth, RightNorth]);
        Assert.Empty(simulation.Entities);
        Assert.Equal(2, simulation.DelayedMapObjects.Count);

        simulation.SetParcelOwner(1, 1);

        Assert.Empty(simulation.DelayedMapObjects);
        Assert.Equal(2, simulation.Entities.OfType<EntranceDoor>().Count(door => door.PartnerId != 0));
    }

    [Fact]
    public void OpensWhilePeopleWalkThrough()
    {
        var simulation = NewSimulation();
        simulation.PlaceMapObjects([LeftNorth, RightNorth]);
        var doors = simulation.Entities.OfType<EntranceDoor>().ToList();
        var walker = simulation.AddEntity(id => new Humanoid(id, new(3, 4)));
        simulation.Schedule(0, new WalkToCommand(walker.Id, new(3, 0)));

        // The walk command runs on the first tick, so check the destination after stepping.
        var openTicks = 0;
        do
        {
            simulation.Step();
            if (doors.All(door => door.IsOpen))
            {
                openTicks++;
            }
            else
            {
                Assert.All(doors, door => Assert.False(door.IsOpen));
            }
        }
        while (walker.Destination is not null);

        // Open from entering the door tile (3, 2) until leaving the tile in front, (3, 1).
        Assert.Equal(2 * Humanoid.TicksPerStep, openTicks);
        Assert.All(doors, door => Assert.Equal(0, door.OccupantCount));
    }

    [Fact]
    public void SaveAndLoadKeepDoorsAndWaitingObjects()
    {
        var original = NewSimulation();
        original.Map[5, 5].Parcel = 1;
        original.PlaceMapObjects([LeftNorth, RightNorth, new(new(5, 5), EntranceDoor.RightObjectType, 1)]);
        var walker = original.AddEntity(id => new Humanoid(id, new(3, 4)));
        original.Schedule(0, new WalkToCommand(walker.Id, new(3, 0)));

        using var stream = new MemoryStream();
        original.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);

        Assert.Single(restored.DelayedMapObjects);
        for (var i = 0; i < 20; i++)
        {
            original.Step();
            restored.Step();
        }

        // The restored simulation rebuilt which door watches which tile.
        Assert.True(restored.Entities.OfType<EntranceDoor>().All(door => door.IsOpen));
        Assert.Equal(original.ComputeStateHash(), restored.ComputeStateHash());
    }

    private static Simulation NewSimulation()
    {
        var map = TestMaps.FromRows(
            "......",
            "......",
            "......",
            "......",
            "......",
            "......");
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Set(TileFlags.Buildable, true);
            }
        }

        map.ParcelCount = 2;
        return new Simulation(map, seed: 7);
    }
}
