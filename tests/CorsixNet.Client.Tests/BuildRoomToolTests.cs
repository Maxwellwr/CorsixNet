using CorsixNet.Client.Maps;
using CorsixNet.Client.Ui;
using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
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
        tool.PlaceExtra(simulation.Content.FindObject("loo")!);
        tool.Update(simulation, 0, Mouse(new(6, 5)));
        Assert.Equal("loo", tool.Ghost?.ObjectType);

        // A second one bought waits until the first is placed.
        tool.PlaceExtra(simulation.Content.FindObject("sink")!);
        tool.Update(simulation, 0, Mouse(new(6, 5)));
        Assert.Equal("loo", tool.Ghost?.ObjectType);
        Assert.Equal(1, tool.ExtrasToPlace("sink"));
    }

    [Fact]
    public void EscDropsAnExtraObjectFromTheCursorAndPicksUpSellBack()
    {
        var host = new WindowTestHost(new LevelRules(100_000, [], [KeyValuePair.Create("sink", 300)]));
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

        var hospital = simulation.Hospitals[0];
        var balance = hospital.Balance;
        var tool = new BuildRoomTool();
        tool.StartEditing(definition, room.Id, simulation.Tick);
        tool.PlaceExtra(simulation.Content.FindObject("sink")!);
        tool.Update(simulation, 0, Mouse(new(6, 5)));
        Assert.Equal("sink", tool.Ghost?.ObjectType);

        // Esc cancels the purchase: nothing is paid and the room stays as it is.
        Assert.Null(tool.Update(simulation, 0, Back()));
        Assert.Null(tool.Ghost);
        Assert.True(tool.IsFurnishing);
        Assert.Equal(balance, hospital.Balance);
        Assert.Null(tool.Update(simulation, 0, Mouse(new(6, 5))));
        Assert.Null(tool.Ghost);

        // Bought and placed, then picked up again: it is sold back and Esc takes it off the cursor.
        tool.PlaceExtra(simulation.Content.FindObject("sink")!);
        var spot2 = Enumerable.Range(4, 4).SelectMany(x => Enumerable.Range(4, 4).Select(y => new TilePosition(x, y)))
            .First(tile => ObjectPlacer.Check(simulation, "sink", tile, Direction.North, 0) == ObjectPlacementProblem.None);
        tool.Update(simulation, 0, Mouse(spot2));
        host.Submit(Assert.IsType<PlaceObjectCommand>(tool.Update(simulation, 0, Mouse(spot2, down: true, pressed: true))));
        Assert.Equal(balance - 300, hospital.Balance);
        tool.Update(simulation, 0, Mouse(spot2));

        tool.Update(simulation, 0, new BuildInput(spot2, 128, 128, false, false, false, false, false, false, true));
        host.Submit(Assert.IsType<PickUpObjectCommand>(tool.Update(simulation, 0, Mouse(spot2, down: true, pressed: true))));
        Assert.Equal(balance, hospital.Balance);
        Assert.Equal("sink", tool.Ghost?.ObjectType);

        Assert.Null(tool.Update(simulation, 0, Back()));
        Assert.Null(tool.Ghost);
        Assert.True(tool.IsFurnishing);
        Assert.Equal(balance, hospital.Balance);
    }
    [Fact]
    public void SeveralExtrasBoughtAtOnceArePlacedOneAfterAnother()
    {
        // UIPlaceObjects:addObjects: each Buy adds one to the line's qty, and they are placed one by one.
        var host = new WindowTestHost();
        var simulation = host.Simulation;
        var definition = simulation.Content.FindRoom("toilets")!;
        host.Submit(new BuildRoomCommand(new RoomPlan("toilets", new TileRect(4, 4, 6, 6), new DoorPlacement(new(5, 4), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>());
        foreach (var needed in new[] { "loo", "sink" })
        {
            host.Submit(new PlaceObjectCommand(needed, FreeSpot(simulation, room, needed), Direction.North));
        }

        var tool = new BuildRoomTool();
        tool.StartEditing(definition, room.Id, simulation.Tick);
        var panel = new RoomEditWindow(tool, _ => { });
        var buyRows = host.Lines(panel).Where(line => line.EndsWith("|Buy", StringComparison.Ordinal)).ToList();
        var loo = buyRows.FindIndex(line => line.Contains(simulation.Content.FindObject("loo")!.Name, StringComparison.Ordinal));
        var sink = buyRows.FindIndex(line => line.Contains(simulation.Content.FindObject("sink")!.Name, StringComparison.Ordinal));
        for (var i = 0; i < 3; i++)
        {
            host.Click(panel, "Buy", loo);
        }

        host.Click(panel, "Buy", sink);
        Assert.Equal(3, tool.ExtrasToPlace("loo"));
        Assert.Equal(1, tool.ExtrasToPlace("sink"));
        Assert.Contains(host.Lines(panel), line => line.Contains(simulation.Content.FindObject("loo")!.Name + "|3|", StringComparison.Ordinal));

        var placed = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            tool.Update(simulation, 0, Mouse(new(9, 9)));
            var ghost = tool.Ghost?.ObjectType;
            Assert.NotNull(ghost);
            var spot = FreeSpot(simulation, room, ghost);
            tool.Update(simulation, 0, Mouse(spot));
            var command = Assert.IsType<PlaceObjectCommand>(tool.Update(simulation, 0, Mouse(spot, down: true, pressed: true)));
            host.Submit(command);
            placed.Add(command.ObjectType);
        }

        Assert.Equal(["loo", "loo", "loo", "sink"], placed);
        Assert.Equal(0, tool.ExtrasToPlace("loo"));
        Assert.Equal(0, tool.ExtrasToPlace("sink"));
        tool.Update(simulation, 0, Mouse(new(9, 9)));
        Assert.Null(tool.Ghost);
        Assert.Equal(4, simulation.Entities.OfType<RoomObject>().Count(o => o.RoomId == room.Id && o.ObjectType == "loo"));
    }

    private static TilePosition FreeSpot(Simulation simulation, Room room, string objectType) =>
        Enumerable.Range(room.Area.X, room.Area.Width).SelectMany(x => Enumerable.Range(room.Area.Y, room.Area.Height).Select(y => new TilePosition(x, y)))
            .First(tile => ObjectPlacer.Check(simulation, objectType, tile, Direction.North, 0) == ObjectPlacementProblem.None);

    [Fact]
    public void ACopiedRoomGetsTheOriginalsDoorAndObjects()
    {
        var host = new WindowTestHost();
        var simulation = host.Simulation;
        host.Submit(new BuildRoomCommand(new RoomPlan("toilets", new TileRect(4, 4, 4, 4), new DoorPlacement(new(5, 4), EdgeOrientation.North))));
        var source = Assert.Single(simulation.Entities.OfType<Room>());
        foreach (var needed in new[] { "loo", "sink" })
        {
            var spot = Enumerable.Range(4, 4).SelectMany(x => Enumerable.Range(4, 4).Select(y => new TilePosition(x, y)))
                .First(tile => ObjectPlacer.Check(simulation, needed, tile, Direction.North, 0) == ObjectPlacementProblem.None);
            host.Submit(new PlaceObjectCommand(needed, spot, Direction.North));
        }

        var tool = new BuildRoomTool();
        tool.StartCopy(simulation, source);
        Assert.True(tool.IsCopying);
        Assert.Null(tool.Update(simulation, 0, Mouse(new(12, 4))));
        Assert.Equal(new TileRect(12, 4, 4, 4), tool.Blueprint?.Area);

        var command = tool.Update(simulation, 0, Mouse(new(12, 4), down: true, pressed: true));
        Assert.Equal(new DoorPlacement(new(13, 4), EdgeOrientation.North), Assert.IsType<BuildRoomCommand>(command).Plan.Door);
        host.Submit(command!);
        for (var i = 0; i < 10; i++)
        {
            if (tool.Update(simulation, 0, Mouse(new(12, 4))) is { } next)
            {
                host.Submit(next);
            }
        }

        var copy = simulation.Entities.OfType<Room>().Single(r => r.Id != source.Id);
        string Objects(Room room) => string.Join(",", simulation.Entities.OfType<RoomObject>().Where(o => o.RoomId == room.Id)
            .Select(o => $"{o.ObjectType}@{o.Tile.X - room.Area.X},{o.Tile.Y - room.Area.Y}").OrderBy(s => s, StringComparer.Ordinal));
        Assert.Equal(Objects(source), Objects(copy));
    }
    private static BuildInput Mouse(TilePosition at, bool down = false, bool pressed = false, bool released = false) =>
        new(at, 128, 128, down, pressed, released, false, false, false, false);

    private static BuildInput Back() => new(null, 0, 0, false, false, false, false, false, true, false);
}
