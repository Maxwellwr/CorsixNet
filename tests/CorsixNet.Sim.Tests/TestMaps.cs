using CorsixNet.Sim.Maps;

namespace CorsixNet.Sim.Tests;

/// <summary>Builds small maps from text: '.' passable floor, '#' impassable.</summary>
internal static class TestMaps
{
    public static LevelMap FromRows(params string[] rows)
    {
        var map = new LevelMap(rows[0].Length, rows.Length, playerCount: 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                ref var tile = ref map[x, y];
                tile.Floor = 1;
                tile.Set(TileFlags.Passable, rows[y][x] == '.');

                var travel = TileFlags.CanTravelAll;
                if (x == 0)
                {
                    travel &= ~TileFlags.CanTravelWest;
                }

                if (x == map.Width - 1)
                {
                    travel &= ~TileFlags.CanTravelEast;
                }

                if (y == 0)
                {
                    travel &= ~TileFlags.CanTravelNorth;
                }

                if (y == map.Height - 1)
                {
                    travel &= ~TileFlags.CanTravelSouth;
                }

                tile.Set(travel, true);
            }
        }

        return map;
    }

    /// <summary>Puts a wall on the west edge of (x, y), blocking travel across it both ways.</summary>
    public static void AddWestWall(LevelMap map, int x, int y)
    {
        map[x, y].WestWall = 100;
        map[x, y].Set(TileFlags.CanTravelWest, false);
        map[x - 1, y].Set(TileFlags.CanTravelEast, false);
    }
}
