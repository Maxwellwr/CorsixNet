using CorsixNet.Client.Maps;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;

namespace CorsixNet.Client.Tests;

/// <summary>The room building flow's way back from a finished room to its blueprint (CorsixTH's UIEditRoom:cancel).</summary>
public class BuildRoomToolTests
{
    [Fact]
    public void EscWhileEditingAPaidRoomGoesBackToTheBlueprintAndThenAsksToDeleteIt()
    {
        var host = new WindowTestHost();
        var simulation = host.Simulation;
        var definition = simulation.Content.FindRoom("gp")!;
        var area = new TileRect(4, 4, 4, 4);
        host.Submit(new BuildRoomCommand(new RoomPlan("gp", area, new DoorPlacement(new(5, 4), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>());
        room.IsPaid = true;

        var tool = new BuildRoomTool();
        var asked = 0;
        tool.AbandonRequested = () => asked++;
        tool.StartEditing(definition, room.Id, simulation.Tick);
        Assert.True(tool.IsEditing);

        var command = tool.Update(simulation, 0, Back());
        Assert.Equal(new ReturnToBlueprintCommand(room.Id), command);
        host.Submit(command!);
        Assert.Null(simulation.FindEntity(room.Id));
        Assert.NotNull(simulation.Hospitals[0].Rebuild);
        Assert.Contains("place the door", tool.Status(simulation), StringComparison.Ordinal);

        // Esc again goes back to the walls, and there it asks before deleting the paid room.
        Assert.IsType<UnmarkBlueprintCommand>(tool.Update(simulation, 0, Back()));
        Assert.Contains("delete the room", tool.Status(simulation), StringComparison.Ordinal);
        Assert.Null(tool.Update(simulation, 0, Back()));
        Assert.Equal(1, asked);
        Assert.True(tool.IsActive);
    }

    [Fact]
    public void ARoomPickedForEditingWaitsWhileItEmpties()
    {
        var host = new WindowTestHost();
        var simulation = host.Simulation;
        var definition = simulation.Content.FindRoom("gp")!;
        host.Submit(new BuildRoomCommand(new RoomPlan("gp", new TileRect(4, 4, 4, 4), new DoorPlacement(new(5, 4), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>());
        room.IsActive = true;

        // The edit command is on its way, then the room's people leave: GameUI's waiting cursor meanwhile.
        var tool = new BuildRoomTool();
        tool.StartEditing(definition, room.Id, simulation.Tick);
        Assert.True(tool.IsWaitingForRoom(simulation));
        room.IsActive = false;
        room.IsClosing = true;
        Assert.True(tool.IsWaitingForRoom(simulation));
        room.IsClosing = false;
        Assert.False(tool.IsWaitingForRoom(simulation));
    }

    private static BuildInput Back() => new(null, 0, 0, false, false, false, false, false, true, false);
}
