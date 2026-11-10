using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;
using static CorsixNet.Content.Tests.PatientFlowTests;

namespace CorsixNet.Content.Tests;

/// <summary>Swing doors (CorsixTH's objects/doors/swing_door_left.lua, swing_door_right.lua) of the ward.</summary>
public class SwingDoorTests
{
    private static readonly TileRect Area = new(2, 12, 8, 8);

    [Fact]
    public void TheWardGetsAPairOfSwingDoorsWithTheirWallPieces()
    {
        var simulation = NewStaffedHospital();
        Run(simulation, new BuildRoomCommand(new RoomPlan("ward", Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "ward");
        var door = Assert.IsType<RoomDoor>(simulation.FindEntity(room.DoorId));
        var map = simulation.Map;

        Assert.True(door.IsSwing);
        Assert.Equal(new TilePosition(4, Area.Y), door.SlaveTile);
        Assert.Equal(0, map[5, Area.Y].NorthWall);
        Assert.True(map[5, Area.Y].Has(TileFlags.DoorNorth));
        var swing = BaseGameContent.WhiteWalls.Swing!;
        Assert.Equal(swing.InsideNorthLeft, map[4, Area.Y].NorthWall);
        Assert.Equal(swing.InsideNorthRight, map[6, Area.Y].NorthWall);

        // Only the right door is walked through; the tiles beside the pair on both sides stay clear.
        Assert.False(map[4, Area.Y].Has(TileFlags.DoorNorth));
        Assert.False(map[4, Area.Y - 1].Has(TileFlags.Buildable));
        Assert.False(map[6, Area.Y].Has(TileFlags.Buildable));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(2)]
    public void TheLeftDoorKeepsOffTheTopCorner(int x)
    {
        var simulation = NewStaffedHospital();

        Assert.NotEqual(
            RoomPlanProblem.None,
            RoomBuilder.Check(simulation.Map, simulation.Content, new RoomPlan("ward", Area, new DoorPlacement(new(x, Area.Y), EdgeOrientation.North)), 0));
        Assert.Equal(
            RoomPlanProblem.None,
            RoomBuilder.Check(simulation.Map, simulation.Content, new RoomPlan("ward", Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North)), 0));
    }

    [Fact]
    public void PeopleSwingTheDoorsOneAtATime()
    {
        var simulation = NewStaffedHospital();
        Run(simulation, new BuildRoomCommand(new RoomPlan("ward", Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "ward");
        var door = Assert.IsType<RoomDoor>(simulation.FindEntity(room.DoorId));
        foreach (var type in new[] { "bed", "desk" })
        {
            PlaceIn(simulation, type);
        }

        Run(simulation, new ConfirmRoomCommand(room.Id));
        Run(simulation, new HireStaffCommand(StaffClass.Nurse, 0, new(14, 16)));

        var swings = new HashSet<long>();
        var swingAnimations = new HashSet<int>();
        for (var tick = 0; tick < 2 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            if (door.SwingStart >= 0)
            {
                swings.Add(door.SwingStart);
            }

            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.IsCrossingDoor && humanoid.ActionAnimation is { } animation)
                {
                    swingAnimations.Add(animation.Id);
                }
            }
        }

        Assert.NotEmpty(swings);
        Assert.Contains(swingAnimations, id => id is 3272 or 3274);
        Assert.True(room.StaffPresent);
    }

    private static void PlaceIn(Simulation simulation, string type)
    {
        for (var y = Area.Bottom - 1; y >= Area.Y; y--)
        {
            for (var x = Area.Right - 1; x >= Area.X; x--)
            {
                foreach (var orientation in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
                {
                    if (CorsixNet.Sim.Objects.ObjectPlacer.Check(simulation, type, new(x, y), orientation, 0) == CorsixNet.Sim.Objects.ObjectPlacementProblem.None)
                    {
                        Run(simulation, new PlaceObjectCommand(type, new(x, y), orientation));
                        return;
                    }
                }
            }
        }

        Assert.Fail($"Found no place for {type}.");
    }
}
