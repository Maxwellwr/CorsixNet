// Tile temperatures, ported from CorsixTH CorsixTH/Src/th_map.cpp (level_map::update_temperatures,
// thermal_neighbour, merge_temperatures, the 8192 start of map_tile::aiTemperature in th_map.h) and the outside
// temperatures and radiator heat passed to it by CorsixTH/Lua/world.lua (World:onTick, outside_temperatures).
// Copyright (c) 2009 Peter "Corsix" Cawley and the CorsixTH contributors. Licensed under the MIT license; see
// THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Maps;

/// <summary>
/// How warm each tile is, from 0 to 65535 for 0 to 50 °C (CorsixTH's <c>aiTemperature</c>). Every hour each tile
/// takes a quarter of its neighbours' average; outside tiles then drift 1 % towards the month's air temperature,
/// tiles with a radiator half-way to the radiators' heat, and other hospital tiles lose 0.1 %.
/// </summary>
/// <remarks>
/// A neighbour across a wall counts a quarter as much, unless both tiles are inside or both outside and both in
/// rooms or both not: CorsixTH compares whether the tiles are in a room, not which room, so neighbouring rooms
/// share heat through their wall. Unlike CorsixTH, whose neighbour test goes by the index into the map, the first
/// and last tiles of a row are not neighbours of the next and previous rows.
/// </remarks>
public sealed class TileTemperatures
{
    /// <summary>Every tile's temperature at the start, about 6 °C (<c>aiTemperature[2]{8192, 8192}</c>).</summary>
    public const ushort Start = 8192;

    /// <summary>The temperature that stands for 50 °C.</summary>
    public const ushort Max = ushort.MaxValue;

    /// <summary>The outside temperature of each month in °C (CorsixTH's <c>outside_temperatures</c> × 50).</summary>
    private static readonly decimal[] OutsideCelsius = [4.1m, 4.4m, 6.3m, 8.65m, 11.95m, 15m, 16.95m, 16.55m, 14.15m, 10.5m, 6.8m, 4.75m];

    private ushort[] _current;
    private ushort[] _next;

    public TileTemperatures(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _current = new ushort[width * height];
        _next = new ushort[width * height];
        Array.Fill(_current, Start);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The tile's temperature, 0 to <see cref="Max"/>.</summary>
    public ushort this[int x, int y] => _current[(y * Width) + x];

    /// <summary>The tile's temperature in °C, as CorsixTH's debug text shows it (<c>getCellTemperature</c> × 50).</summary>
    public decimal Celsius(int x, int y) => this[x, y] * 50m / Max;

    /// <summary>The month's outside temperature (<c>outside_temperatures[monthOfYear]</c>, as <c>l_check_temp</c> converts it).</summary>
    /// <param name="month">The month of the year, 1 to 12.</param>
    public static ushort OutsideTemperature(int month) => (ushort)(OutsideCelsius[month - 1] / 50m * Max);

    /// <summary>
    /// The radiators' temperature at a heat in tenths (<c>0.25 + radiator_heat * 0.3</c>, as <c>l_check_temp</c>
    /// converts it): 25 % with the boiler stuck at no heat, 28 % at the lowest setting, 55 % at full.
    /// </summary>
    public static ushort RadiatorTemperature(int heatTenths) => (ushort)((0.25m + (heatTenths * 0.03m)) * Max);

    /// <summary>
    /// One hour of heat (<c>level_map::update_temperatures</c>).
    /// </summary>
    /// <param name="map">The map, for walls, rooms and which tiles are inside.</param>
    /// <param name="air">The outside temperature.</param>
    /// <param name="radiators">Per tile (row by row), the temperature of a radiator on it, or 0 for none.</param>
    internal void Update(LevelMap map, ushort air, ReadOnlySpan<ushort> radiators)
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var index = (y * Width) + x;
                ref var tile = ref map[x, y];
                uint sum = 0;
                uint count = 0;
                count += Neighbour(map, ref sum, tile, tile.Has(TileFlags.CanTravelNorth), x, y - 1);
                count += Neighbour(map, ref sum, tile, tile.Has(TileFlags.CanTravelSouth), x, y + 1);
                count += Neighbour(map, ref sum, tile, tile.Has(TileFlags.CanTravelEast), x + 1, y);
                count += Neighbour(map, ref sum, tile, tile.Has(TileFlags.CanTravelWest), x - 1, y);

                uint merge;
                uint ratio;
                if (tile.Has(TileFlags.Hospital))
                {
                    if (radiators[index] != 0)
                    {
                        // Half-way to the radiators' heat, which is never below the air.
                        merge = Math.Max(radiators[index], air);
                        ratio = 2;
                    }
                    else
                    {
                        // Generally lose 0.1 %.
                        merge = 0;
                        ratio = 1000;
                    }
                }
                else
                {
                    // 1 % towards the air.
                    merge = air;
                    ratio = 100;
                }

                // A quarter towards the neighbours' average.
                var temperature = _current[index];
                if (count != 0)
                {
                    temperature = Merge(temperature, sum / count, 4);
                }

                _next[index] = Merge(temperature, merge, ratio);
            }
        }

        (_current, _next) = (_next, _current);
    }

    /// <summary><c>merge_temperatures</c>: <paramref name="ratio"/> − 1 parts of the tile's temperature to 1 of the other.</summary>
    private static ushort Merge(uint temperature, uint other, uint ratio) => (ushort)(((temperature * (ratio - 1)) + other) / ratio);

    /// <summary><c>thermal_neighbour</c>: adds the neighbour's temperature, weighted, and returns its weight.</summary>
    private uint Neighbour(LevelMap map, ref uint sum, in MapTile tile, bool canTravel, int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return 0;
        }

        var temperature = (uint)_current[(y * Width) + x];
        ref var neighbour = ref map[x, y];
        var weight = canTravel
            || (neighbour.Has(TileFlags.Hospital) == tile.Has(TileFlags.Hospital) && neighbour.Has(TileFlags.Room) == tile.Has(TileFlags.Room))
            ? 4u : 1u;
        sum += temperature * weight;
        return weight;
    }

    internal void Write(SimWriter writer)
    {
        foreach (var temperature in _current)
        {
            writer.WriteUInt16(temperature);
        }
    }

    internal static TileTemperatures Read(SimReader reader, int width, int height)
    {
        var temperatures = new TileTemperatures(width, height);
        for (var i = 0; i < temperatures._current.Length; i++)
        {
            temperatures._current[i] = reader.ReadUInt16();
        }

        return temperatures;
    }
}
