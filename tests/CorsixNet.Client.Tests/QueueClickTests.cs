using CorsixNet.Client.Maps;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;

namespace CorsixNet.Client.Tests;

/// <summary>Which clicks on the map open a room's queue: only its door's (CorsixTH's Door:onClick).</summary>
public class QueueClickTests
{
    [Fact]
    public void OnlyARoomsDoorOpensItsQueueNotItsFloorOrFurniture()
    {
        var host = new WindowTestHost();
        var simulation = host.Simulation;
        host.Submit(new BuildRoomCommand(new RoomPlan("gp", new TileRect(4, 4, 4, 4), new DoorPlacement(new(5, 4), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>());
        room.IsActive = true;

        // The door's tile and the corridor tile in front of it.
        Assert.Same(room, MapViewScreen.QueueRoomAt(simulation, new TilePosition(5, 4)));
        Assert.Same(room, MapViewScreen.QueueRoomAt(simulation, new TilePosition(5, 3)));

        // Anywhere else in the room, such as the desk chair while the doctor is away from it, opens nothing.
        for (var x = 4; x < 8; x++)
        {
            for (var y = 5; y < 8; y++)
            {
                Assert.Null(MapViewScreen.QueueRoomAt(simulation, new TilePosition(x, y)));
            }
        }

        Assert.Null(MapViewScreen.QueueRoomAt(simulation, new TilePosition(6, 4)));
    }
}
