using CorsixNet.Client.Maps;
using CorsixNet.Client.Ui;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
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

    [Fact]
    public void TheBlueprintShowsMoveResizeAndArrowCursors()
    {
        var host = new WindowTestHost();
        var simulation = host.Simulation;
        var tool = new BuildRoomTool();
        tool.Start(simulation.Content.FindRoom("gp")!);
        Assert.Equal(GameCursor.ResizeRoom, tool.Cursor(new TilePosition(5, 5)));

        // Drag out 4 by 4 from (4, 4) to (7, 7).
        tool.Update(simulation, 0, Mouse(new(4, 4), down: true, pressed: true));
        tool.Update(simulation, 0, Mouse(new(7, 7), down: true));
        tool.Update(simulation, 0, Mouse(new(7, 7), released: true));

        // UIEditRoom:onCursorWorldPositionChange.
        Assert.Equal(GameCursor.MoveRoom, tool.Cursor(new TilePosition(5, 6)));
        Assert.Equal(GameCursor.AllArrows, tool.Cursor(new TilePosition(4, 4)));
        Assert.Equal(GameCursor.WestEastArrow, tool.Cursor(new TilePosition(7, 5)));
        Assert.Equal(GameCursor.NorthSouthArrow, tool.Cursor(new TilePosition(5, 4)));
        Assert.Equal(GameCursor.ResizeRoom, tool.Cursor(new TilePosition(9, 9)));
    }

    [Fact]
    public void AnotherToiletOrSinkBoughtFromTheListStaysOnTheCursor()
    {
        var host = new WindowTestHost();
        var simulation = host.Simulation;
        var definition = simulation.Content.FindRoom("toilets")!;
        host.Submit(new BuildRoomCommand(new RoomPlan("toilets", new TileRect(4, 4, 4, 4), new DoorPlacement(new(5, 4), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>());
        foreach (var needed in new[] { "loo", "sink" })
        {
            var spot = Enumerable.Range(4, 4).SelectMany(x => Enumerable.Range(4, 4).Select(y => new TilePosition(x, y)))
                .First(tile => ObjectPlacer.Check(simulation, needed, tile, Direction.North, 0) == ObjectPlacementProblem.None);
            host.Submit(new PlaceObjectCommand(needed, spot, Direction.North));
        }

        Assert.Empty(RoomBuilder.MissingObjects(simulation, room));

        var tool = new BuildRoomTool();
        tool.StartEditing(definition, room.Id, simulation.Tick);
        foreach (var extra in new[] { "loo", "sink" })
        {
            tool.PlaceExtra(simulation.Content.FindObject(extra)!);
            tool.Update(simulation, 0, Mouse(new(6, 5)));
            Assert.Equal(extra, tool.Ghost?.ObjectType);
        }
    }
    private static BuildInput Mouse(TilePosition at, bool down = false, bool pressed = false, bool released = false) =>
        new(at, 128, 128, down, pressed, released, false, false, false, false);

    private static BuildInput Back() => new(null, 0, 0, false, false, false, false, false, true, false);
}
