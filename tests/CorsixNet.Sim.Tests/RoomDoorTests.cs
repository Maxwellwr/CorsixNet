using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;

namespace CorsixNet.Sim.Tests;

/// <summary>Going through room doors (CorsixTH's <c>navigateDoor</c>) and wandering people keeping out of rooms.</summary>
public class RoomDoorTests
{
    private static readonly DoorAnimations PatientDoor = new(Entering: new(184, 10), Leaving: new(182, 12));

    private static readonly GameContent Content = new(
        [new RoomDefinition("office", "Office", 4, 18, new WallSet(82, 83, 90, 91))],
        [],
        doorAnimations: new Dictionary<HumanoidType, DoorAnimations> { [HumanoidType.StandardMalePatient] = PatientDoor });

    // A 4x4 room at (2, 2)..(5, 5) with its door on the south side, between (3, 5) inside and (3, 6) outside.
    private static readonly RoomPlan Plan = new("office", new TileRect(2, 2, 4, 4), new DoorPlacement(new(3, 6), EdgeOrientation.North));

    [Fact]
    public void GoingInPlaysTheDoorAnimationOnTheDoorsTile()
    {
        var simulation = NewSimulationWithRoom();
        var door = Assert.Single(simulation.Entities.OfType<RoomDoor>());
        var patient = simulation.AddEntity(id => new Humanoid(id, new TilePosition(3, 6)));

        Assert.True(patient.WalkTo(simulation, new TilePosition(3, 5)));
        simulation.Step();

        // Going north, away from the camera: the leaving animation, from the door's tile, with the door hidden.
        Assert.True(patient.IsCrossingDoor);
        Assert.Equal(PatientDoor.Leaving, patient.ActionAnimation);
        Assert.False(patient.ActionAnimationFlip);
        Assert.Equal(new TilePosition(3, 6), patient.ActionAnimationTile);
        Assert.Equal(door.Id, patient.HiddenObjectId);
        Assert.Equal(new TilePosition(3, 5), patient.Destination);

        for (var i = 1; i < PatientDoor.Leaving.Length; i++)
        {
            simulation.Step();
            Assert.True(patient.IsCrossingDoor);
        }

        simulation.Step();
        Assert.False(patient.IsCrossingDoor);
        Assert.False(patient.IsMoving);
        Assert.Null(patient.ActionAnimation);
        Assert.Equal(new TilePosition(3, 5), patient.Tile);
    }

    [Fact]
    public void GoingOutSouthPlaysTheEnteringAnimationMirrored()
    {
        var simulation = NewSimulationWithRoom();
        var patient = simulation.AddEntity(id => new Humanoid(id, new TilePosition(3, 5)));

        Assert.True(patient.WalkTo(simulation, new TilePosition(3, 7)));
        simulation.Step();

        Assert.True(patient.IsCrossingDoor);
        Assert.Equal(PatientDoor.Entering, patient.ActionAnimation);
        Assert.True(patient.ActionAnimationFlip);
        Assert.Equal(new TilePosition(3, 6), patient.Tile);

        RunUntil(simulation, () => !patient.IsMoving);
        Assert.Equal(new TilePosition(3, 7), patient.Tile);
    }

    [Fact]
    public void OnlyOneHumanoidGoesThroughADoorAtATime()
    {
        var simulation = NewSimulationWithRoom();
        var first = simulation.AddEntity(id => new Humanoid(id, new TilePosition(3, 6)));
        var second = simulation.AddEntity(id => new Humanoid(id, new TilePosition(3, 5)));

        first.WalkTo(simulation, new TilePosition(3, 4));
        second.WalkTo(simulation, new TilePosition(3, 7));
        simulation.Step();

        Assert.True(first.IsCrossingDoor);
        Assert.False(second.IsCrossingDoor);
        Assert.Equal(new TilePosition(3, 5), second.Tile);

        RunUntil(simulation, () => !first.IsCrossingDoor);
        RunUntil(simulation, () => second.IsCrossingDoor);
        RunUntil(simulation, () => !first.IsMoving && !second.IsMoving);
        Assert.Equal(new TilePosition(3, 4), first.Tile);
        Assert.Equal(new TilePosition(3, 7), second.Tile);
    }

    [Fact]
    public void HumanoidsWithoutDoorAnimationsWalkThroughPlainly()
    {
        var simulation = NewSimulationWithRoom();
        var nurse = simulation.AddEntity(id => new Humanoid(id, new TilePosition(3, 6), HumanoidType.Nurse));

        nurse.WalkTo(simulation, new TilePosition(3, 5));
        simulation.Step();

        Assert.False(nurse.IsCrossingDoor);
        Assert.True(nurse.IsStepping);
    }

    [Fact]
    public void SaveAndLoadKeepAHumanoidGoingThroughADoor()
    {
        var original = NewSimulationWithRoom();
        var patient = original.AddEntity(id => new Humanoid(id, new TilePosition(3, 6)));
        patient.WalkTo(original, new TilePosition(3, 4));
        original.Step();
        original.Step();

        using var stream = new MemoryStream();
        original.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, Content);
        Assert.Equal(original.ComputeStateHash(), restored.ComputeStateHash());

        for (var i = 0; i < 40; i++)
        {
            original.Step();
            restored.Step();
        }

        Assert.Equal(original.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(new TilePosition(3, 4), Assert.IsType<Humanoid>(restored.FindEntity(patient.Id)).Tile);
    }

    [Fact]
    public void WanderingPeopleStayOutOfRooms()
    {
        var simulation = NewSimulationWithRoom();
        var room = Assert.Single(simulation.Entities.OfType<Room>());
        var outside = simulation.AddEntity(id => new Humanoid(id, new TilePosition(3, 7)));
        var inside = simulation.AddEntity(id => new Humanoid(id, new TilePosition(4, 4)));

        for (var i = 0; i < 3000; i++)
        {
            if (!outside.IsMoving)
            {
                simulation.Schedule(0, new MeanderCommand(outside.Id));
            }

            if (!inside.IsMoving)
            {
                simulation.Schedule(0, new MeanderCommand(inside.Id));
            }

            simulation.Step();
            Assert.False(room.Area.Contains(outside.Tile));
            Assert.False(outside.IsCrossingDoor);
            Assert.True(room.Area.Contains(inside.Tile));
        }
    }

    private static void RunUntil(Simulation simulation, Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            simulation.Step();
        }

        Assert.True(condition());
    }

    private static Simulation NewSimulationWithRoom()
    {
        var map = TestMaps.FromRows(
            "..........",
            "..........",
            "..........",
            "..........",
            "..........",
            "..........",
            "..........",
            "..........",
            "..........",
            "..........");
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Parcel = 1;
                map[x, y].Set(TileFlags.Buildable | TileFlags.Hospital | TileFlags.BuildableNorth | TileFlags.BuildableEast | TileFlags.BuildableSouth | TileFlags.BuildableWest, true);
            }
        }

        map.UpdatePathfinding();
        map.UpdateShadows();
        var simulation = new Simulation(map, seed: 7, Content);
        simulation.Schedule(0, new BuildRoomCommand(Plan));
        simulation.Step();
        Assert.Single(simulation.Entities.OfType<Room>());
        return simulation;
    }
}
