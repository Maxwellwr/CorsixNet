using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;

namespace CorsixNet.Sim.Tests;

/// <summary>Rooms may not cut the hospital into parts (CorsixTH's <c>UIEditRoom:checkReachability</c>).</summary>
public class RoomReachabilityTests
{
    private static readonly RoomDefinition Cupboard = new("cupboard", "Cupboard", 3, 19, new WallSet(82, 83, 90, 91));
    private static readonly GameContent Content = new([Cupboard], [], []);

    // A corridor four tiles wide running east to west between walls, with a hall to the south.
    private static LevelMap Map()
    {
        var map = TestMaps.FromRows(
            "############",
            "............",
            "............",
            "............",
            "............",
            "#####..#####",
            "#####..#####",
            "#####..#####");
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Parcel = 1;
                if (map[x, y].Has(TileFlags.Passable))
                {
                    map[x, y].Set(TileFlags.Buildable | TileFlags.Hospital | TileFlags.BuildableNorth | TileFlags.BuildableEast | TileFlags.BuildableSouth | TileFlags.BuildableWest, true);
                }
            }
        }

        map.UpdatePathfinding();
        return map;
    }

    [Fact]
    public void ARoomAcrossTheCorridorCutsItInTwoAndIsRefused()
    {
        var map = Map();

        // Four tiles high, the corridor's whole width: west and east would no longer meet.
        var across = new TileRect(4, 1, 3, 4);
        Assert.Equal(RoomPlanProblem.None, RoomBuilder.CheckArea(map, Cupboard, across, 0));
        Assert.Equal(RoomPlanProblem.BlocksOffParts, RoomBuilder.CheckReachability(map, across));
        Assert.Equal(RoomPlanProblem.BlocksOffParts,
            RoomBuilder.Check(map, Content, new RoomPlan("cupboard", across, new DoorPlacement(new(5, 1), EdgeOrientation.North)), 0));

        // The map is as it was.
        Assert.True(map[5, 2].Has(TileFlags.Passable));
    }

    [Fact]
    public void ARoomAgainstTheCorridorsWallLeavesAWayRound()
    {
        var map = Map();
        var against = new TileRect(1, 1, 3, 3);

        Assert.Equal(RoomPlanProblem.None, RoomBuilder.CheckReachability(map, against));
        Assert.Equal(RoomPlanProblem.None,
            RoomBuilder.Check(map, Content, new RoomPlan("cupboard", against, new DoorPlacement(new(2, 4), EdgeOrientation.North)), 0));
    }
}
