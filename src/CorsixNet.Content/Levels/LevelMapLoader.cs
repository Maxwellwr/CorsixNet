// Tile interpretation ported from CorsixTH CorsixTH/Src/th_map.cpp (level_map::load_from_th_file).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Formats;
using CorsixNet.Sim;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;

namespace CorsixNet.Content.Levels;

/// <summary>A level map ready for the simulation, plus the objects its file places and the level's money settings.</summary>
public sealed record LoadedLevelMap(LevelMap Map, IReadOnlyList<MapObjectPlacement> Objects, LevelRules? Rules = null)
{
    /// <summary>
    /// Starts a simulation on this map and places the map's objects. Use a freshly loaded map:
    /// the simulation takes ownership of it and changes it.
    /// </summary>
    public Simulation StartSimulation(ulong seed)
    {
        var simulation = new Simulation(Map, seed, BaseGameContent.Create(), Rules);
        simulation.PlaceMapObjects(Objects);
        return simulation;
    }
}

/// <summary>Builds a <see cref="LevelMap"/> from a <see cref="MapFile"/>, following CorsixTH's rules.</summary>
public static class LevelMapLoader
{
    /// <summary>Floor blocks 71–73 (pond foliage) are too tall for the floor layer.</summary>
    private const int FirstPondFoliage = 71;

    private const int LastPondFoliage = 73;

    /// <summary>Floor drawn under pond foliage that was moved to the wall layer.</summary>
    private const int PondBase = 69;

    private const byte Impassable = 1 << 0;
    private const byte NotBuildable = 1 << 1;
    private const byte NotBuildableNorth = 1 << 2;
    private const byte NotBuildableEast = 1 << 3;
    private const byte NotBuildableSouth = 1 << 4;
    private const byte NotBuildableWest = 1 << 5;
    private const byte OutsideHospital = 1 << 4;

    public static LoadedLevelMap Load(MapFile file)
    {
        var map = new LevelMap(MapFile.Size, MapFile.Size, file.PlayerCount);
        var objects = new List<MapObjectPlacement>();
        var maxParcel = 0;

        for (var player = 0; player < file.PlayerCount; player++)
        {
            var (cameraX, cameraY) = file.CameraTiles[player];
            var (heliportX, heliportY) = file.HeliportTiles[player];
            map.SetCameraTile(player, new TilePosition(cameraX, cameraY));
            map.SetHeliportTile(player, new TilePosition(heliportX, heliportY));
        }

        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var raw = file[x, y];
                ref var tile = ref map[x, y];

                var travel = TileFlags.CanTravelAll;
                if (x == 0)
                {
                    travel &= ~TileFlags.CanTravelWest;
                }
                else if (x == map.Width - 1)
                {
                    travel &= ~TileFlags.CanTravelEast;
                }

                if (y == 0)
                {
                    travel &= ~TileFlags.CanTravelNorth;
                }
                else if (y == map.Height - 1)
                {
                    travel &= ~TileFlags.CanTravelSouth;
                }

                tile.Flags = travel;
                tile.Floor = MapFile.BlockSprite(raw.Floor);

                // Divider walls separate parcels of different owners and are only
                // shown in multiplayer; they are not built at load time.
                if (raw.NorthWall == 0 || IsDividerWall(raw.NorthWall))
                {
                    if (tile.Floor is >= FirstPondFoliage and <= LastPondFoliage)
                    {
                        tile.NorthWall = tile.Floor;
                        tile.Floor = PondBase;
                    }
                    else
                    {
                        tile.NorthWall = 0;
                    }
                }
                else
                {
                    tile.NorthWall = MapFile.BlockSprite(raw.NorthWall);
                    tile.Set(TileFlags.CanTravelNorth, false);
                    if (y != 0)
                    {
                        map[x, y - 1].Set(TileFlags.CanTravelSouth, false);
                    }
                }

                if (raw.WestWall == 0 || IsDividerWall(raw.WestWall))
                {
                    tile.WestWall = 0;
                }
                else
                {
                    tile.WestWall = MapFile.BlockSprite(raw.WestWall);
                    tile.Set(TileFlags.CanTravelWest, false);
                    if (x != 0)
                    {
                        map[x - 1, y].Set(TileFlags.CanTravelEast, false);
                    }
                }

                tile.Parcel = raw.Parcel;
                maxParcel = Math.Max(maxParcel, raw.Parcel);

                if ((raw.Flags & Impassable) == 0)
                {
                    tile.Set(TileFlags.Passable, true);
                    if ((raw.ExtraFlags & OutsideHospital) == 0)
                    {
                        // Edges next to an object are buildable regardless of the file's flags.
                        var noObject = raw.ObjectType == 0;
                        tile.Set(TileFlags.Hospital, true);
                        tile.Set(TileFlags.Buildable, (raw.Flags & NotBuildable) == 0);
                        tile.Set(TileFlags.BuildableNorth, (raw.Flags & NotBuildableNorth) == 0 || noObject);
                        tile.Set(TileFlags.BuildableEast, (raw.Flags & NotBuildableEast) == 0 || noObject);
                        tile.Set(TileFlags.BuildableSouth, (raw.Flags & NotBuildableSouth) == 0 || noObject);
                        tile.Set(TileFlags.BuildableWest, (raw.Flags & NotBuildableWest) == 0 || noObject);
                    }
                }

                if (raw.ObjectType != 0)
                {
                    objects.Add(new MapObjectPlacement(new TilePosition(x, y), raw.ObjectType, raw.ObjectDirection));
                }
            }
        }

        map.ParcelCount = maxParcel + 1;

        // As in CorsixTH's Map:load, parcel N starts out owned by player N if there is one;
        // the remaining parcels are for sale.
        for (var parcel = 1; parcel < map.ParcelCount; parcel++)
        {
            map.SetParcelOwner(parcel, parcel <= map.PlayerCount ? parcel : 0);
        }

        map.UpdateShadows();
        return new LoadedLevelMap(map, objects);
    }

    /// <summary>
    /// Loads an original level map, such as <c>LEVEL.L1</c>, from the game's LEVELS folder, with the
    /// level's money settings at the original's medium difficulty.
    /// </summary>
    public static LoadedLevelMap LoadOriginal(ThemeHospitalInstall install, string fileName)
    {
        var level = Load(MapFile.Parse(install.ReadDataFile("LEVELS", fileName)));
        return level with { Rules = LevelRulesLoader.LoadOriginal(install, fileName, BaseGameContent.Create()) };
    }

    /// <summary>
    /// Loads a map file from any path, such as a CorsixTH <c>.map</c> file. With the game at hand, the
    /// money settings are the medium difficulty's general ones; without it, everything is free.
    /// </summary>
    public static LoadedLevelMap LoadFile(string path, ThemeHospitalInstall? install = null)
    {
        var level = Load(MapFile.Parse(Rnc.DecompressIfNeeded(File.ReadAllBytes(path))));
        return install is null ? level : level with { Rules = LevelRulesLoader.LoadOriginal(install, path, BaseGameContent.Create()) };
    }

    private static bool IsDividerWall(byte blockCode) => blockCode >> 1 == 70;
}
