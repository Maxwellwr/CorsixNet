// Ported from CorsixTH CorsixTH/Src/th_pathfind.cpp (basic_pathfinder and the open heap).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Maps;

namespace CorsixNet.Sim.Pathfinding;

/// <summary>
/// A* search over the map's travel flags, without diagonal moves.
/// </summary>
/// <remarks>
/// A faithful port of CorsixTH's pathfinder, including its neighbour order (W, E, N, S)
/// and heap behaviour, so ties between equally short paths are broken the same way.
/// Node storage is reused between searches.
/// </remarks>
public sealed class Pathfinder
{
    private const int NoPrevious = -1;

    private int _width;
    private int _height;
    private int[] _previous = [];
    private int[] _cost = [];
    private int[] _distance = [];
    private int[] _guess = [];
    private int[] _openIndex = [];
    private bool[] _visited = [];
    private int[] _dirty = [];
    private int _dirtyCount;
    private readonly List<int> _openHeap = [];
    private int _destinationX;
    private int _destinationY;

    /// <summary>
    /// Finds a shortest walkable path from <paramref name="start"/> to <paramref name="end"/>.
    /// </summary>
    /// <param name="map">The map to search.</param>
    /// <param name="start">First tile of the path.</param>
    /// <param name="end">Last tile of the path; must be passable.</param>
    /// <param name="path">Receives the tiles from start to end, both included. Cleared first.</param>
    /// <returns>Whether a path exists.</returns>
    public bool TryFindPath(LevelMap map, TilePosition start, TilePosition end, List<TilePosition> path)
    {
        path.Clear();
        if (!map.Contains(start.X, start.Y) || !map.Contains(end.X, end.Y) || !map[end.X, end.Y].Has(TileFlags.Passable))
        {
            return false;
        }

        _destinationX = end.X;
        _destinationY = end.Y;
        var node = Init(map, start);
        var target = Index(end.X, end.Y);

        while (true)
        {
            if (node == target)
            {
                BuildPath(target, path);
                return true;
            }

            SearchNeighbours(map, node);

            if (_openHeap.Count == 0)
            {
                return false;
            }

            node = PopFromOpenHeap();
        }
    }

    /// <summary>Length in steps of a shortest path between two tiles, or null if there is none.</summary>
    public int? FindDistance(LevelMap map, TilePosition start, TilePosition end)
    {
        var path = new List<TilePosition>();
        return TryFindPath(map, start, end, path) ? path.Count - 1 : null;
    }

    /// <summary>
    /// Finds a tile to wait on near <paramref name="start"/>, as CorsixTH's <c>idle_tile_finder::find_idle_tile</c>:
    /// a passable hospital tile that is not a door's walkway, searching outwards without passing through
    /// doors. The search prefers tiles next to the previous one, so consecutive <paramref name="n"/> give
    /// a line of neighbouring tiles: the places of a queue.
    /// </summary>
    /// <param name="map">The map to search.</param>
    /// <param name="start">Where the queue starts.</param>
    /// <param name="n">Which suitable tile to return, counting from 0.</param>
    /// <param name="tile">The tile found.</param>
    /// <returns>Whether any suitable tile was found; with fewer than <paramref name="n"/> + 1, the last one is returned.</returns>
    public bool TryFindIdleTile(LevelMap map, TilePosition start, int n, out TilePosition tile)
    {
        tile = default;
        if (!map.Contains(start.X, start.Y))
        {
            return false;
        }

        _destinationX = start.X;
        _destinationY = start.Y;
        var node = Init(map, start);
        _guess[node] = 0;
        int? possible = null;
        while (true)
        {
            _visited[node] = true;
            var x = node % _width;
            var y = node / _width;
            var flags = map[x, y];
            if (!flags.Has(TileFlags.DoNotIdle) && !flags.Has(TileFlags.AvoidTile) && flags.Has(TileFlags.Passable | TileFlags.Hospital))
            {
                if (n == 0)
                {
                    tile = new TilePosition(x, y);
                    return true;
                }

                possible = node;
                n--;
            }

            var best = SearchIdleNeighbours(map, node, start);
            if (_openHeap.Count == 0)
            {
                break;
            }

            if (best >= 0)
            {
                // Promote the best neighbour to the front of the open list, so that sequential n
                // give neighbouring results.
                _guess[best] = -_distance[best];
                OpenHeapPromote(best);
            }

            node = PopFromOpenHeap();
        }

        if (possible is { } result)
        {
            tile = new TilePosition(result % _width, result / _width);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The neighbour step of <c>idle_tile_finder::try_node</c>: neighbours are recorded unless a door is in
    /// between, and of the neighbours reached so far the one nearest to the start is returned (−1 for none).
    /// CorsixTH compares Euclidean distances; squared distances order the same.
    /// </summary>
    private int SearchIdleNeighbours(LevelMap map, int node, TilePosition start)
    {
        var x = node % _width;
        var y = node / _width;
        var tile = map[x, y];
        var best = -1;
        var bestDistance = 0;

        void Try(int neighbour, bool door)
        {
            if (_visited[neighbour])
            {
                return;
            }

            if (!door)
            {
                RecordIdleNeighbour(map, node, neighbour);
            }

            if (_previous[neighbour] != neighbour)
            {
                var dx = (neighbour % _width) - start.X;
                var dy = (neighbour / _width) - start.Y;
                var distance = (dx * dx) + (dy * dy);
                if (best < 0 || distance < bestDistance)
                {
                    best = neighbour;
                    bestDistance = distance;
                }
            }
        }

        if (tile.Has(TileFlags.CanTravelWest))
        {
            Try(node - 1, tile.Has(TileFlags.DoorWest));
        }

        if (tile.Has(TileFlags.CanTravelEast))
        {
            Try(node + 1, map[x + 1, y].Has(TileFlags.DoorWest));
        }

        if (tile.Has(TileFlags.CanTravelNorth))
        {
            Try(node - _width, tile.Has(TileFlags.DoorNorth));
        }

        if (tile.Has(TileFlags.CanTravelSouth))
        {
            Try(node + _width, map[x, y + 1].Has(TileFlags.DoorNorth));
        }

        return best;
    }

    /// <summary><see cref="RecordNeighbourIfPassable"/> with the idle finder's guess of 0.</summary>
    private void RecordIdleNeighbour(LevelMap map, int node, int neighbour)
    {
        var flags = map[neighbour % _width, neighbour / _width];
        if (!flags.Has(TileFlags.Passable))
        {
            return;
        }

        var neighbourCost = NeighbourCost(flags);
        if (_previous[neighbour] == neighbour)
        {
            _previous[neighbour] = node;
            _cost[neighbour] = _cost[node] + neighbourCost;
            _distance[neighbour] = _distance[node] + 1;
            _guess[neighbour] = 0;
            _dirty[_dirtyCount++] = neighbour;
            PushToOpenHeap(neighbour);
        }
        else if (_cost[node] + 1 < _cost[neighbour])
        {
            _previous[neighbour] = node;
            _cost[neighbour] = _cost[node] + neighbourCost;
            _distance[neighbour] = _distance[node] + 1;
            OpenHeapPromote(neighbour);
        }
    }

    private int Index(int x, int y) => (y * _width) + x;

    private int Init(LevelMap map, TilePosition start)
    {
        AllocateNodes(map.Width, map.Height);
        var node = Index(start.X, start.Y);
        _previous[node] = NoPrevious;
        _cost[node] = 0;
        _distance[node] = 0;
        _guess[node] = GuessDistance(node);
        _visited[node] = true;
        _dirty[0] = node;
        _dirtyCount = 1;
        _openHeap.Clear();
        return node;
    }

    private void AllocateNodes(int width, int height)
    {
        if (width != _width || height != _height)
        {
            _width = width;
            _height = height;
            var count = width * height;
            _previous = new int[count];
            _cost = new int[count];
            _distance = new int[count];
            _guess = new int[count];
            _openIndex = new int[count];
            _visited = new bool[count];
            _dirty = new int[count];
            for (var i = 0; i < count; i++)
            {
                // A node that points to itself is not part of any path.
                _previous[i] = i;
            }
        }
        else
        {
            for (var i = 0; i < _dirtyCount; i++)
            {
                _previous[_dirty[i]] = _dirty[i];
                _visited[_dirty[i]] = false;
            }
        }

        _dirtyCount = 0;
    }

    /// <summary>Map edges need no check: the travel flags already forbid leaving the map.</summary>
    private void SearchNeighbours(LevelMap map, int node)
    {
        var x = node % _width;
        var y = node / _width;
        var tile = map[x, y];

        if (tile.Has(TileFlags.CanTravelWest))
        {
            RecordNeighbourIfPassable(map, node, node - 1);
        }

        if (tile.Has(TileFlags.CanTravelEast))
        {
            RecordNeighbourIfPassable(map, node, node + 1);
        }

        if (tile.Has(TileFlags.CanTravelNorth))
        {
            RecordNeighbourIfPassable(map, node, node - _width);
        }

        if (tile.Has(TileFlags.CanTravelSouth))
        {
            RecordNeighbourIfPassable(map, node, node + _width);
        }
    }

    private void RecordNeighbourIfPassable(LevelMap map, int node, int neighbour)
    {
        var flags = map[neighbour % _width, neighbour / _width];
        if (_visited[neighbour] || !flags.Has(TileFlags.Passable))
        {
            return;
        }

        var neighbourCost = NeighbourCost(flags);
        if (_previous[neighbour] == neighbour)
        {
            _previous[neighbour] = node;
            _cost[neighbour] = _cost[node] + neighbourCost;
            _distance[neighbour] = _distance[node] + 1;
            _guess[neighbour] = GuessDistance(neighbour);
            _dirty[_dirtyCount++] = neighbour;
            PushToOpenHeap(neighbour);
        }
        else if (_cost[node] + 1 < _cost[neighbour])
        {
            _previous[neighbour] = node;
            _cost[neighbour] = _cost[node] + neighbourCost;
            _distance[neighbour] = _distance[node] + 1;
            OpenHeapPromote(neighbour);
        }
    }

    /// <summary>
    /// What a step onto the tile costs: 128 on a tile to avoid, 1 otherwise. As in CorsixTH, an
    /// already reached node is only improved when its cost exceeds the new one by more than 1,
    /// whatever the step really costs (<c>record_neighbour_if_passable</c>).
    /// </summary>
    private static int NeighbourCost(MapTile tile) => tile.Has(TileFlags.AvoidTile) ? 128 : 1;

    /// <summary>Manhattan distance, as diagonal moves are not allowed.</summary>
    private int GuessDistance(int node)
    {
        return Math.Abs((node % _width) - _destinationX) + Math.Abs((node / _width) - _destinationY);
    }

    private int Value(int node) => _cost[node] + _guess[node];

    private void BuildPath(int target, List<TilePosition> path)
    {
        for (var node = target; node != NoPrevious; node = _previous[node])
        {
            path.Add(new TilePosition(node % _width, node / _width));
        }

        path.Reverse();
    }

    private void PushToOpenHeap(int node)
    {
        _openIndex[node] = _openHeap.Count;
        _openHeap.Add(node);
        OpenHeapPromote(node);
    }

    private void OpenHeapPromote(int node)
    {
        var i = _openIndex[node];
        while (i > 0)
        {
            var parentIndex = (i - 1) / 2;
            var parent = _openHeap[parentIndex];
            if (Value(parent) <= Value(node))
            {
                break;
            }

            _openIndex[parent] = i;
            _openHeap[i] = parent;
            _openHeap[parentIndex] = node;
            i = parentIndex;
        }

        _openIndex[node] = i;
    }

    private int PopFromOpenHeap()
    {
        var result = _openHeap[0];
        var node = _openHeap[^1];
        _openHeap.RemoveAt(_openHeap.Count - 1);
        if (_openHeap.Count == 0)
        {
            return result;
        }

        _openHeap[0] = node;
        var i = 0;
        var min = 0;
        var left = 1;
        var value = Value(node);
        while (left < _openHeap.Count)
        {
            min = i;
            var right = (i + 1) * 2;
            var minValue = value;
            var swap = -1;
            var test = _openHeap[left];
            if (Value(test) < minValue)
            {
                min = left;
                minValue = Value(test);
                swap = test;
            }

            if (right < _openHeap.Count)
            {
                test = _openHeap[right];
                if (Value(test) < minValue)
                {
                    min = right;
                    swap = test;
                }
            }

            if (min == i)
            {
                break;
            }

            _openIndex[swap] = i;
            _openHeap[i] = swap;
            _openHeap[min] = node;
            i = min;
            left = (i * 2) + 1;
        }

        _openIndex[node] = min;
        return result;
    }
}
