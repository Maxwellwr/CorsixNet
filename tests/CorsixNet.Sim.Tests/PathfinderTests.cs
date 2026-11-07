using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Pathfinding;

namespace CorsixNet.Sim.Tests;

public class PathfinderTests
{
    private readonly Pathfinder _pathfinder = new();
    private readonly List<TilePosition> _path = [];

    [Fact]
    public void FindsStraightPath()
    {
        var map = TestMaps.FromRows(".....");

        Assert.True(_pathfinder.TryFindPath(map, new(0, 0), new(4, 0), _path));
        Assert.Equal([new(0, 0), new(1, 0), new(2, 0), new(3, 0), new(4, 0)], _path);
    }

    [Fact]
    public void PathToStartIsJustTheStart()
    {
        var map = TestMaps.FromRows("...");

        Assert.True(_pathfinder.TryFindPath(map, new(1, 0), new(1, 0), _path));
        Assert.Equal([new TilePosition(1, 0)], _path);
    }

    [Fact]
    public void GoesAroundObstacles()
    {
        var map = TestMaps.FromRows(
            ".....",
            ".###.",
            ".....");

        Assert.True(_pathfinder.TryFindPath(map, new(2, 0), new(2, 2), _path));

        Assert.Equal(7, _path.Count);
        Assert.DoesNotContain(_path, tile => tile.Y == 1 && tile.X is >= 1 and <= 3);
        AssertContiguous(_path);
    }

    [Fact]
    public void RespectsWalls()
    {
        var map = TestMaps.FromRows(
            "...",
            "...");
        TestMaps.AddWestWall(map, 1, 0);
        TestMaps.AddWestWall(map, 1, 1);

        Assert.False(_pathfinder.TryFindPath(map, new(0, 0), new(2, 0), _path));
        Assert.Empty(_path);
    }

    [Fact]
    public void RejectsImpassableOrOutsideTarget()
    {
        var map = TestMaps.FromRows(".#");

        Assert.False(_pathfinder.TryFindPath(map, new(0, 0), new(1, 0), _path));
        Assert.False(_pathfinder.TryFindPath(map, new(0, 0), new(5, 0), _path));
    }

    [Fact]
    public void ReusesBuffersBetweenSearches()
    {
        var map = TestMaps.FromRows(
            "....",
            "....",
            "....");

        Assert.True(_pathfinder.TryFindPath(map, new(0, 0), new(3, 2), _path));
        var first = _path.ToList();
        Assert.True(_pathfinder.TryFindPath(map, new(3, 2), new(0, 0), _path));
        Assert.True(_pathfinder.TryFindPath(map, new(0, 0), new(3, 2), _path));

        Assert.Equal(first, _path);
        Assert.Equal(6, _path.Count);
    }

    [Fact]
    public void BreaksTiesDeterministically()
    {
        // All six shortest paths tie. Tracing CorsixTH's algorithm by hand (neighbours in
        // W, E, N, S order, the same binary heap operations) gives this one.
        var map = TestMaps.FromRows(
            "...",
            "...",
            "...");

        Assert.True(_pathfinder.TryFindPath(map, new(0, 0), new(2, 2), _path));

        Assert.Equal([new(0, 0), new(1, 0), new(1, 1), new(1, 2), new(2, 2)], _path);
    }

    private static void AssertContiguous(List<TilePosition> path)
    {
        for (var i = 1; i < path.Count; i++)
        {
            Assert.Equal(1, Math.Abs(path[i].X - path[i - 1].X) + Math.Abs(path[i].Y - path[i - 1].Y));
        }
    }
}
