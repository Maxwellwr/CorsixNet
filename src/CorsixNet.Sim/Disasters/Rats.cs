// Rat holes and rats, ported from CorsixTH CorsixTH/Lua/hospital.lua (dailyUpdateRatholes, dailyUpdateRats,
// Hospital:spawnRat, getWallsAround, addRathole, removeRatholeXY, getOwnedTileCount), CorsixTH/Src/th_lua_map.cpp
// (l_map_get_litter_fraction) and CorsixTH/Lua/entities/rat.lua (Rat:onClick).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Sim.Disasters;

/// <summary>
/// Rats, which come with litter. Each day a hospital wants a rat hole for every half percent of its corridor tiles
/// that have litter, and tries a few random corridor walls for one more. Then, unless it has a rat for every 200 of its
/// tiles already, a rat comes with a chance of 5 % plus half the littered fraction, running from one hole to another;
/// a clean hospital gets just the two holes that takes. Building over a hole closes it, and the player can shoot a rat,
/// which leaves a dead rat on the floor.
/// </summary>
public static class Rats
{
    /// <summary>Rat holes wanted per littered fraction of the corridors (<c>getLitterFraction * 200</c>).</summary>
    private const int HolesPerLitterFraction = 200;

    /// <summary>A hospital's tiles per rat at a time (<c>tiles_per_rat</c>).</summary>
    private const int TilesPerRat = 200;

    /// <summary>The daily chance of a rat in a clean hospital (<c>base_rat_spawn_chance</c>).</summary>
    private const decimal BaseSpawnChance = 0.05m;

    /// <summary>The extra chance at full litter (<c>litter_rat_spawn_chance</c>).</summary>
    private const decimal LitterSpawnChance = 0.5m;

    /// <summary>Steps of CorsixTH's <c>math.random()</c> here.</summary>
    private const int RandomSteps = 1_000_000;

    /// <summary>The end of a hospital's day, after the heating (<c>Hospital:onEndDay</c>).</summary>
    internal static void OnEndDay(Simulation simulation, Hospital hospital)
    {
        UpdateRatholes(simulation, hospital, 0);
        UpdateRats(simulation, hospital);
    }

    /// <summary>
    /// <c>getLitterFraction</c>: of the corridor tiles (outside rooms) on the hospital's parcels, the fraction with litter.
    /// </summary>
    public static decimal LitterFraction(Simulation simulation, int player)
    {
        var map = simulation.Map;
        var littered = new bool[map.Width * map.Height];
        foreach (var entity in simulation.Entities)
        {
            if (entity is Litter { IsRemoved: false } litter && map.Contains(litter.Tile.X, litter.Tile.Y))
            {
                littered[(litter.Tile.Y * map.Width) + litter.Tile.X] = true;
            }
        }

        var tiles = 0;
        var withLitter = 0;
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var cell = map[x, y];
                if (cell.Parcel == 0 || cell.Parcel >= map.ParcelCount || map.GetParcelOwner(cell.Parcel) != player + 1 || cell.RoomId != 0)
                {
                    continue;
                }

                tiles++;
                if (littered[(y * map.Width) + x])
                {
                    withLitter++;
                }
            }
        }

        return tiles == 0 ? 0m : withLitter / (decimal)tiles;
    }

    /// <summary>The player shoots a rat (<c>Rat:onClick</c>): it is gone, and in their hospital a dead rat lies where it was.</summary>
    internal static void Shoot(Simulation simulation, int player, Rat rat)
    {
        rat.Shoot();
        var tile = rat.Phase == RatPhase.Running && rat.Timer * 2 <= Rat.TicksPerTile ? rat.To : rat.From;
        if (Epidemics.IsInHospital(simulation, player, tile))
        {
            // CorsixTH makes the dead rat without Litter.resolveTileRank, so it may lie on other litter.
            simulation.AddEntity(id => new Litter(id, player, LitterType.DeadRat, tile, isMirrored: false));
        }
    }

    /// <summary>
    /// <c>World:prepareFootprintTilesForBuild</c> and <c>prepareRectangleTilesForBuild</c>'s
    /// <c>Hospital:removeRatholeXY</c>: the holes of a tile close when something is built there.
    /// </summary>
    internal static void RemoveRatholesAt(Simulation simulation, TilePosition tile)
    {
        foreach (var hospital in simulation.Hospitals)
        {
            hospital.RemoveRatholesAt(tile);
        }
    }

    /// <summary>
    /// <c>Hospital:removeRatholesAroundRoom</c>, from <c>Room:deactivate</c>: the holes in the room's walls close, those of
    /// the corridor tiles along its four sides in the wall towards the room.
    /// </summary>
    internal static void RemoveRatholesAroundRoom(Simulation simulation, Room room)
    {
        var area = room.Area;
        bool Facing(Rathole hole) =>
            (hole.Wall == Direction.South && hole.Tile.Y == area.Y - 1 && hole.Tile.X >= area.X && hole.Tile.X < area.Right)
            || (hole.Wall == Direction.North && hole.Tile.Y == area.Bottom && hole.Tile.X >= area.X && hole.Tile.X < area.Right)
            || (hole.Wall == Direction.East && hole.Tile.X == area.X - 1 && hole.Tile.Y >= area.Y && hole.Tile.Y < area.Bottom)
            || (hole.Wall == Direction.West && hole.Tile.X == area.Right && hole.Tile.Y >= area.Y && hole.Tile.Y < area.Bottom);
        foreach (var hospital in simulation.Hospitals)
        {
            hospital.RemoveRatholes(Facing);
        }
    }

    /// <summary>
    /// <c>Hospital:getWallsAround</c>: the walls of a tile, west, north, east and south, the east and south ones being
    /// the neighbours' west and north walls.
    /// </summary>
    internal static List<Rathole> WallsAround(LevelMap map, TilePosition tile)
    {
        var walls = new List<Rathole>();
        var cell = map[tile.X, tile.Y];
        if (cell.WestWall != 0)
        {
            walls.Add(new Rathole(tile, Direction.West, cell.Parcel));
        }

        if (cell.NorthWall != 0)
        {
            walls.Add(new Rathole(tile, Direction.North, cell.Parcel));
        }

        if (tile.X + 1 < map.Width && map[tile.X + 1, tile.Y].WestWall != 0)
        {
            walls.Add(new Rathole(tile, Direction.East, cell.Parcel));
        }

        if (tile.Y + 1 < map.Height && map[tile.X, tile.Y + 1].NorthWall != 0)
        {
            walls.Add(new Rathole(tile, Direction.South, cell.Parcel));
        }

        return walls;
    }

    /// <summary>
    /// <c>dailyUpdateRatholes</c>: with fewer holes than wanted, up to 6 + 2 × the shortfall (at most 12) random tiles are
    /// tried; the first corridor floor tile of the hospital with a wall gets a hole in a random one of its walls, if that
    /// one has none yet.
    /// </summary>
    private static void UpdateRatholes(Simulation simulation, Hospital hospital, int minimumHoles)
    {
        var map = simulation.Map;
        var fraction = LitterFraction(simulation, hospital.Player);
        var wanted = Math.Max(minimumHoles, (int)Math.Floor((fraction * HolesPerLitterFraction) + 0.5m));
        if (hospital.Ratholes.Count >= wanted)
        {
            return;
        }

        var tries = Math.Min(12, 6 + (2 * (wanted - hospital.Ratholes.Count)));
        for (var i = 0; i < tries; i++)
        {
            var tile = new TilePosition(simulation.Random.NextInclusive(1, map.Width) - 1, simulation.Random.NextInclusive(1, map.Height) - 1);
            var cell = map[tile.X, tile.Y];
            if (!Epidemics.IsInHospital(simulation, hospital.Player, tile) || cell.RoomId != 0 || !cell.Has(TileFlags.Buildable))
            {
                continue;
            }

            var walls = WallsAround(map, tile);
            if (walls.Count == 0)
            {
                continue;
            }

            var wall = walls[simulation.Random.NextInclusive(1, walls.Count) - 1];
            if (!hospital.Ratholes.Any(hole => hole.Tile == wall.Tile && hole.Wall == wall.Wall))
            {
                hospital.AddRathole(wall);
                return;
            }
        }
    }

    /// <summary><c>dailyUpdateRats</c>.</summary>
    private static void UpdateRats(Simulation simulation, Hospital hospital)
    {
        var map = simulation.Map;
        var ownedTiles = 0;
        for (var parcel = 1; parcel < map.ParcelCount; parcel++)
        {
            if (map.GetParcelOwner(parcel) == hospital.Player + 1)
            {
                ownedTiles += map.ParcelTileCount(parcel);
            }
        }

        // CorsixTH counts every rat in the world; here those of the hospital.
        var maxRats = Math.Max(1, ownedTiles / TilesPerRat);
        if (simulation.Entities.Count(entity => entity is Rat { IsGone: false } rat && rat.Player == hospital.Player) >= maxRats)
        {
            return;
        }

        var chance = BaseSpawnChance + (LitterFraction(simulation, hospital.Player) * LitterSpawnChance);
        if (simulation.Random.Next(RandomSteps) >= chance * RandomSteps)
        {
            return;
        }

        // Clean hospitals normally have no rat holes: only enough are added for a rat to run between two.
        for (var i = 0; i < 2 && !HasRatholeRoute(hospital); i++)
        {
            UpdateRatholes(simulation, hospital, hospital.Ratholes.Count + 1);
        }

        if (HasRatholeRoute(hospital))
        {
            // Hospital:spawnRat: out of a random hole.
            var origin = hospital.Ratholes[simulation.Random.NextInclusive(1, hospital.Ratholes.Count) - 1];
            Rat.Spawn(simulation, hospital.Player, origin, hospital.Ratholes);
        }
    }

    /// <summary><c>hasRatholeRoute</c>: two holes on different tiles.</summary>
    private static bool HasRatholeRoute(Hospital hospital) =>
        hospital.Ratholes.Count > 1 && hospital.Ratholes.Skip(1).Any(hole => hole.Tile != hospital.Ratholes[0].Tile);
}
