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

    /// <summary>
    /// The level played in free build mode (<see cref="LevelRules.WithFreeBuild"/>), if it may be: CorsixTH's
    /// <c>App:loadLevel</c> allows it only for levels outside a campaign, so an original campaign level stays as it is.
    /// </summary>
    public LoadedLevelMap WithFreeBuild() =>
        Rules?.LevelNumber is not null ? this : this with { Rules = (Rules ?? LevelRules.Free).WithFreeBuild() };
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

    /// <summary>
    /// <c>level_map::load_from_th_file</c> and <c>Map:load</c>: the map's tiles, flags, parcels and objects. For the map
    /// editor (<paramref name="editing"/>) the parcels get no owners, so no divider walls go up and nothing turns to grass.
    /// </summary>
    public static LoadedLevelMap Load(MapFile file, bool editing = false)
    {
        var map = new LevelMap(MapFile.Size, MapFile.Size, file.PlayerCount);
        var objects = new List<MapObjectPlacement>();
        var maxParcel = 0;
        var dividerWalls = new List<(int X, int Y, bool North, ushort Sprite)>();

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

                // Divider walls separate parcels of different owners: they are kept with the original tiles
                // and go up when the owners are set below.
                if (IsDividerWall(raw.NorthWall))
                {
                    dividerWalls.Add((x, y, true, MapFile.BlockSprite(raw.NorthWall)));
                }

                if (IsDividerWall(raw.WestWall))
                {
                    dividerWalls.Add((x, y, false, MapFile.BlockSprite(raw.WestWall)));
                }

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

        // CorsixTH's level_map::load keeps the tiles, divider walls included, as original_cells.
        map.KeepOriginalTiles();
        foreach (var (x, y, north, sprite) in dividerWalls)
        {
            ref var original = ref map.OriginalTile(x, y);
            if (north)
            {
                original.NorthWall = sprite;
            }
            else
            {
                original.WestWall = sprite;
            }
        }

        // As in CorsixTH's Map:load, parcel N starts out owned by player N if there is one; the remaining parcels
        // are for sale, and show as grass until they are bought.
        for (var parcel = 1; parcel < map.ParcelCount && !editing; parcel++)
        {
            map.SetParcelOwner(parcel, parcel <= map.PlayerCount ? parcel : 0);
        }

        map.UpdateShadows();
        return new LoadedLevelMap(map, objects);
    }

    /// <summary>
    /// Loads an original level map, such as <c>LEVEL.L1</c>, from the game's LEVELS folder, with the
    /// level's money settings at the given difficulty (medium by default).
    /// </summary>
    public static LoadedLevelMap LoadOriginal(ThemeHospitalInstall install, string fileName, Difficulty difficulty = Difficulty.Medium)
    {
        var level = Load(MapFile.Parse(install.ReadDataFile("LEVELS", fileName)));
        return level with { Rules = LevelRulesLoader.LoadOriginal(install, fileName, BaseGameContent.Create(), difficulty) };
    }

    /// <summary>
    /// Loads a map file from any path, such as a CorsixTH <c>.map</c> file. With the game at hand, the
    /// money settings are the medium difficulty's general ones; without it, everything is free. A CorsixTH
    /// <c>.level</c> file is loaded as a custom level (<see cref="LoadCustom"/>).
    /// </summary>
    public static LoadedLevelMap LoadFile(string path, ThemeHospitalInstall? install = null)
    {
        if (string.Equals(Path.GetExtension(path), ".level", StringComparison.OrdinalIgnoreCase))
        {
            return LoadCustom(path, install).Level;
        }

        var level = Load(MapFile.Parse(Rnc.DecompressIfNeeded(File.ReadAllBytes(path))));
        return install is null ? level : level with { Rules = LevelRulesLoader.LoadOriginal(install, path, BaseGameContent.Create()) };
    }

    /// <summary>
    /// Loads a CorsixTH custom level (<c>App:readLevelFile</c> and <c>Map:load</c>): the map its <c>%MapFile</c> names,
    /// looked for beside the level file (in any case of letters), or an original map such as <c>LEVEL.L5</c> from the
    /// game, with the money settings of the level file (<see cref="LevelRulesLoader.LoadCustom"/>).
    /// </summary>
    /// <exception cref="InvalidDataException">The level file names no map file, or its settings are wrong.</exception>
    /// <exception cref="FileNotFoundException">The map file is not there.</exception>
    public static (LoadedLevelMap Level, CustomLevelInfo Info) LoadCustom(string levelPath, ThemeHospitalInstall? install = null)
    {
        var data = File.ReadAllBytes(levelPath);
        var info = CustomLevelInfo.Parse(System.Text.Encoding.Latin1.GetString(data));
        if (info.MapFile is not { } mapFile)
        {
            throw new InvalidDataException($"The level {levelPath} names no map file.");
        }

        byte[] mapData;
        if (LevelRulesLoader.LevelNumber(mapFile) is not null)
        {
            mapData = (install ?? throw new InvalidOperationException($"The level's map {mapFile} is the original game's."))
                .ReadDataFile("LEVELS", mapFile);
        }
        else
        {
            mapData = Rnc.DecompressIfNeeded(File.ReadAllBytes(FindBeside(levelPath, mapFile)));
        }

        var level = Load(MapFile.Parse(mapData));
        var rules = LevelRulesLoader.LoadCustom(LevelConfig.Parse(data), install, BaseGameContent.Create());
        return (level with { Rules = rules }, info);
    }

    /// <summary>
    /// Reads a CorsixTH campaign (<c>App:loadCampaign</c>): the campaign file and the paths of its level files, which lie
    /// beside it (in any case of letters), in the order they are played.
    /// </summary>
    /// <exception cref="InvalidDataException">The campaign file is wrong or names no levels.</exception>
    /// <exception cref="FileNotFoundException">A level file is not there.</exception>
    public static (CustomCampaign Campaign, IReadOnlyList<string> LevelPaths) LoadCampaign(string campaignPath)
    {
        var campaign = CustomCampaign.Parse(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(campaignPath)));
        if (campaign.Levels.Count == 0)
        {
            throw new InvalidDataException($"The campaign {campaignPath} names no levels.");
        }

        return (campaign, campaign.Levels.Select(level => FindBeside(campaignPath, level)).ToList());
    }

    /// <summary>
    /// A file named by a level or campaign file (CorsixTH's <c>App:_checkOrFind</c>, reduced to the folders at hand): the
    /// path as given if it exists beside the naming file, else the file of that name, in any case of letters, beside the
    /// naming file, then in the <c>Levels</c> and <c>Campaigns</c> folders next to that folder (CorsixTH's own
    /// <c>level_dir</c> and <c>campaign_dir</c>, where its example campaign finds its levels).
    /// </summary>
    private static string FindBeside(string namingPath, string fileName)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(namingPath)) ?? ".";
        var name = fileName.Replace('\\', '/');
        var direct = Path.Combine(folder, name);
        if (File.Exists(direct))
        {
            return direct;
        }

        var bare = name[(name.LastIndexOf('/') + 1)..];
        var parent = Path.GetDirectoryName(folder);
        IEnumerable<string> siblings = parent is null || !Directory.Exists(parent)
            ? []
            : Directory.EnumerateDirectories(parent)
                .Where(dir => Path.GetFileName(dir).Equals("Levels", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(dir).Equals("Campaigns", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal);
        foreach (var dir in siblings.Prepend(folder))
        {
            var found = Directory.EnumerateFiles(dir)
                .Order(StringComparer.Ordinal)
                .FirstOrDefault(file => string.Equals(Path.GetFileName(file), bare, StringComparison.OrdinalIgnoreCase));
            if (found is not null)
            {
                return found;
            }
        }

        throw new FileNotFoundException($"{fileName} is not beside {Path.GetFileName(namingPath)}.", direct);
    }

    private static bool IsDividerWall(byte blockCode) => blockCode >> 1 == 70;
}
