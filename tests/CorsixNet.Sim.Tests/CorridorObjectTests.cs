using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using static CorsixNet.Sim.Definitions.FootprintFlags;

namespace CorsixNet.Sim.Tests;

/// <summary>Side objects and the corridor path check for objects placed outside rooms.</summary>
public class CorridorObjectTests
{
    private static readonly ObjectDefinition Radiator = new("radiator", "Radiator", 44, Side(Direction.North, Direction.East, Direction.South, Direction.West), CorridorObject: true);
    private static readonly ObjectDefinition Bin = new("bin", "Bin", 50, Side(Direction.North, Direction.East), CorridorObject: true);

    private static readonly ObjectDefinition Bench = new("bench", "Bench", 4, new Dictionary<Direction, ObjectOrientation>
    {
        [Direction.North] = new([new(0, 0, CompleteCell), new(0, -1, UsePosition | Shareable)]),
        [Direction.East] = new([new(0, 0, CompleteCell), new(1, 0, UsePosition | Shareable)]),
    }, CorridorObject: true);

    private static readonly ObjectDefinition DrinksMachine = new("drinks_machine", "Drinks Machine", 7, new Dictionary<Direction, ObjectOrientation>
    {
        [Direction.North] = new([new(0, 0, CompleteCell), new(0, -1, UsePosition)]),
    }, CorridorObject: true);

    private static readonly GameContent Content = new([], [], [Radiator, Bin, Bench, DrinksMachine]);

    [Fact]
    public void ARadiatorAgainstAWallTakesThatSideOnly()
    {
        var simulation = NewSimulation();
        simulation.Map[3, 3].NorthWall = 100;
        simulation.Map.UpdatePathfinding();

        Place(simulation, "radiator", new(3, 3), Direction.North);

        var tile = simulation.Map[3, 3];
        Assert.Single(simulation.Entities.OfType<RoomObject>());
        Assert.False(tile.Has(TileFlags.BuildableNorth));
        Assert.True(tile.Has(TileFlags.BuildableEast));
        Assert.True(tile.Has(TileFlags.Passable));
        Assert.True(tile.Has(TileFlags.Buildable));
        Assert.False(tile.Has(TileFlags.BlockedNorth));
    }

    [Fact]
    public void OnlyOneObjectStandsOnATile()
    {
        var simulation = NewSimulation();
        Place(simulation, "radiator", new(3, 3), Direction.North);

        Assert.Equal(ObjectPlacementProblem.Blocked, Check(simulation, "radiator", new(3, 3), Direction.East));
        Assert.Equal(ObjectPlacementProblem.Blocked, Check(simulation, "bench", new(3, 3), Direction.North));
        Assert.Equal(ObjectPlacementProblem.None, Check(simulation, "radiator", new(4, 3), Direction.East));
    }

    [Fact]
    public void ARadiatorOnAnOpenEdgeClosesItEvenWhenTravelIsWorkedOutAgain()
    {
        var simulation = NewSimulation();

        Place(simulation, "radiator", new(3, 3), Direction.South);

        Assert.False(simulation.Map[3, 3].Has(TileFlags.CanTravelSouth));
        Assert.False(simulation.Map[3, 4].Has(TileFlags.CanTravelNorth));

        simulation.Map.UpdatePathfinding();
        Assert.False(simulation.Map[3, 3].Has(TileFlags.CanTravelSouth));
        Assert.False(simulation.Map[3, 4].Has(TileFlags.CanTravelNorth));
        Assert.True(simulation.Map[3, 3].Has(TileFlags.CanTravelEast));
    }

    [Fact]
    public void ASideObjectMayNotCutTheOnlyWayAcrossAnEdge()
    {
        // A corridor one tile wide running north to south.
        var simulation = NewSimulation(
            "##.##",
            "##.##",
            "##.##",
            "##.##");

        Assert.Equal(ObjectPlacementProblem.WouldBlockAccess, Check(simulation, "radiator", new(2, 1), Direction.South));
        Assert.Equal(ObjectPlacementProblem.None, Check(simulation, "radiator", new(2, 1), Direction.East));
    }

    [Fact]
    public void TheBinFacingEastStandsAgainstTheWestSide()
    {
        var simulation = NewSimulation();

        Place(simulation, "bin", new(3, 3), Direction.East);

        Assert.False(simulation.Map[3, 3].Has(TileFlags.BuildableWest));
        Assert.True(simulation.Map[3, 3].Has(TileFlags.BuildableEast));
        Assert.False(simulation.Map[3, 3].Has(TileFlags.CanTravelWest));
    }

    [Fact]
    public void ACorridorObjectMayNotCutTheCorridor()
    {
        var simulation = NewSimulation(
            "#######",
            ".......",
            "#######",
            "#######");
        simulation.Map[3, 2].Set(TileFlags.Passable, true);
        simulation.Map[3, 3].Set(TileFlags.Passable, true);

        // A bench in the corridor would cut it in two; at the end of the side passage it does not.
        Assert.Equal(ObjectPlacementProblem.WouldBlockAccess, Check(simulation, "bench", new(1, 1), Direction.East));
        Assert.Equal(ObjectPlacementProblem.None, Check(simulation, "bench", new(3, 3), Direction.North));
    }

    [Fact]
    public void NoObjectGoesOnATileSomeoneUsesAnother()
    {
        var simulation = NewSimulation();
        Place(simulation, "drinks_machine", new(3, 4), Direction.North);

        // (3, 3) is where people stand to use the machine.
        Assert.Equal(ObjectPlacementProblem.Blocked, Check(simulation, "radiator", new(3, 3), Direction.North));

        // The bench's use position may be shared, so a radiator may go there.
        Place(simulation, "bench", new(6, 4), Direction.North);
        Assert.Equal(ObjectPlacementProblem.None, Check(simulation, "radiator", new(6, 3), Direction.North));
    }

    private static Dictionary<Direction, ObjectOrientation> Side(params Direction[] facings)
    {
        return facings.ToDictionary(facing => facing, _ => new ObjectOrientation([new(0, 0, OnlySide)]));
    }

    private static ObjectPlacementProblem Check(Simulation simulation, string type, TilePosition tile, Direction orientation)
    {
        return ObjectPlacer.Check(simulation, type, tile, orientation, 0);
    }

    private static void Place(Simulation simulation, string type, TilePosition tile, Direction orientation)
    {
        Assert.Equal(ObjectPlacementProblem.None, Check(simulation, type, tile, orientation));
        simulation.Schedule(0, new PlaceObjectCommand(type, tile, orientation));
        simulation.Step();
    }

    private static Simulation NewSimulation(params string[] rows)
    {
        if (rows.Length == 0)
        {
            rows = [.. Enumerable.Repeat("..........", 8)];
        }

        var map = TestMaps.FromRows(rows);
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
        return new Simulation(map, seed: 3, Content);
    }
}
