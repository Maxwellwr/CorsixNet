using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;
using static CorsixNet.Sim.Definitions.FootprintFlags;

namespace CorsixNet.Sim.Tests;

/// <summary>A corridor's side object standing where a room is built (CorsixTH's <c>World:resetSideObjects</c>).</summary>
public class SideObjectRoomTests
{
    private static readonly RoomDefinition Cupboard = new("cupboard", "Cupboard", 3, 19, new WallSet(82, 83, 90, 91));

    private static readonly ObjectDefinition Radiator = new("radiator", "Radiator", 44, new Dictionary<Direction, ObjectOrientation>
    {
        [Direction.North] = new([new(0, 0, OnlySide)]),
    }, CorridorObject: true);

    private static readonly GameContent Content = new([Cupboard], [], [Radiator]);

    // A 4x4 room at (4, 2)..(7, 5) with its door on the south side, opening onto (5, 6).
    private static readonly RoomPlan Plan = new("cupboard", new TileRect(4, 2, 4, 4), new DoorPlacement(new(5, 6), EdgeOrientation.North));

    [Fact]
    public void ARadiatorInTheNewRoomsAreaBecomesTheRoomsAndKeepsItsSide()
    {
        var simulation = NewSimulation();
        Run(simulation, new PlaceObjectCommand("radiator", new(5, 4), Direction.North));
        Assert.Equal(0, Assert.Single(simulation.Entities.OfType<RoomObject>()).RoomId);

        Run(simulation, new MarkBlueprintCommand("cupboard", Plan.Area));
        Run(simulation, new BuildRoomCommand(Plan));

        var room = Assert.Single(simulation.Entities.OfType<Room>());
        var radiator = Assert.Single(simulation.Entities.OfType<RoomObject>());
        Assert.Equal(room.Id, radiator.RoomId);
        Assert.Equal(new TilePosition(5, 4), radiator.Tile);
        Assert.Equal(radiator.Id, simulation.Map[5, 4].ObjectId);
        Assert.False(simulation.Map[5, 4].Has(TileFlags.BuildableNorth));
        Assert.False(simulation.Map[5, 4].Has(TileFlags.CanTravelNorth));
    }

    private static void Run(Simulation simulation, Command command)
    {
        simulation.Schedule(0, command);
        simulation.Step();
    }

    private static Simulation NewSimulation()
    {
        var map = TestMaps.FromRows([.. Enumerable.Repeat("............", 10)]);
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
        return new Simulation(map, seed: 7, Content);
    }
}
