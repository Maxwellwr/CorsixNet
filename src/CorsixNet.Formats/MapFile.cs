// Block lookup table and layout ported from CorsixTH CorsixTH/Src/th_map.cpp.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Buffers.Binary;

namespace CorsixNet.Formats;

/// <summary>One tile of a level map file, as stored.</summary>
/// <param name="ObjectDirection">Direction (or other parameter) of the object placed on the tile.</param>
/// <param name="ObjectType">Type of object placed on the tile, or 0.</param>
/// <param name="Floor">Floor block code (see <see cref="MapFile.BlockSprite"/>).</param>
/// <param name="NorthWall">North wall block code, or 0.</param>
/// <param name="WestWall">West wall block code, or 0.</param>
/// <param name="Flags">Tile flags: bit 0 impassable, bit 1 not buildable, bits 2–5 not buildable on the N/E/S/W side.</param>
/// <param name="Unknown">Byte 6, purpose unknown.</param>
/// <param name="ExtraFlags">More flags: bit 4 marks tiles outside the hospital.</param>
/// <param name="Parcel">Number of the land parcel the tile belongs to; 0 is outside any parcel.</param>
public readonly record struct MapFileTile(
    byte ObjectDirection,
    byte ObjectType,
    byte Floor,
    byte NorthWall,
    byte WestWall,
    byte Flags,
    byte Unknown,
    byte ExtraFlags,
    ushort Parcel);

/// <summary>
/// A Theme Hospital level map: the original <c>LEVELS/LEVEL.L*</c> files and CorsixTH <c>.map</c> files.
/// </summary>
/// <remarks>
/// Layout (after RNC decompression): player count at offset 0; 128×128 tiles of 8 bytes from offset 34;
/// a 16-bit parcel number per tile from offset 131106; up to four 16-bit camera tile indices at 163876
/// and heliport tile indices at 163884. Based on CorsixTH's <c>level_map::load_from_th_file</c>.
/// </remarks>
public sealed class MapFile
{
    /// <summary>Width and height of every map, in tiles.</summary>
    public const int Size = 128;

    /// <summary>Maximum number of players a map can describe.</summary>
    public const int MaxPlayers = 4;

    /// <summary>Smallest valid file size.</summary>
    public const int MinimumLength = 163948;

    private const int TileOffset = 34;
    private const int TileLength = 8;
    private const int ParcelOffset = 131106;
    private const int CameraOffset = 163876;
    private const int HeliportOffset = 163884;

    /// <summary>
    /// Maps block codes in map files to sprite numbers in <c>DATA/VBLK-0</c>.
    /// Ported from CorsixTH (<c>gs_iTHMapBlockLUT</c>), which corrects the published format description.
    /// </summary>
    private static readonly byte[] BlockSprites =
    [
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C,
        0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24,
        0x25, 0x26, 0x27, 0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x2D, 0x2E, 0x2F, 0x30,
        0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x3B, 0x3C,
        0x3D, 0x3E, 0x3F, 0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
        0x49, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F, 0x50, 0x52, 0x53, 0x54, 0x55,
        0x56, 0x57, 0x58, 0x59, 0x5A, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F, 0x60, 0x61,
        0x62, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6A, 0x6B, 0x6C, 0x6D,
        0x6E, 0x6F, 0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79,
        0x7A, 0x7B, 0x7C, 0x7D, 0x7E, 0x7F, 0x80, 0x81, 0x84, 0x85, 0x88, 0x89,
        0x8C, 0x8D, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x8E, 0x8F, 0x00, 0x00,
        0x00, 0x00, 0x8E, 0x8F, 0xD5, 0xD6, 0x9C, 0xCC, 0xCD, 0xCE, 0xCF, 0xD0,
        0xD1, 0xD2, 0xD3, 0xD4, 0xB3, 0xAF, 0xB0, 0xB1, 0xB2, 0xB3, 0xB4, 0xB5,
        0xB6, 0xB7, 0xB8, 0xB9, 0xB3, 0xB3, 0xB4, 0xB4, 0xBA, 0xBB, 0xBC, 0xBD,
        0xBE, 0xBF, 0xC0, 0xC1, 0xC2, 0xC3, 0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9,
        0xCA, 0xCB, 0x00, 0x82, 0x83, 0x86, 0x87, 0x8A, 0x8B, 0x92, 0x93, 0x94,
        0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0x9B, 0x00, 0x9D, 0x9E, 0x9F, 0xA0,
        0xA1, 0xA2, 0xA3, 0xA4, 0xD7, 0xD8, 0xD9, 0xDA, 0xDB, 0xDC, 0xDD, 0xDE,
        0xDF, 0xE0, 0xE1, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
    ];

    private readonly MapFileTile[] _tiles;
    private readonly (int X, int Y)[] _cameras;
    private readonly (int X, int Y)[] _heliports;

    private MapFile(int playerCount, MapFileTile[] tiles, (int X, int Y)[] cameras, (int X, int Y)[] heliports)
    {
        PlayerCount = playerCount;
        _tiles = tiles;
        _cameras = cameras;
        _heliports = heliports;
    }

    /// <summary>Number of players, between 1 and <see cref="MaxPlayers"/>.</summary>
    public int PlayerCount { get; }

    public MapFileTile this[int x, int y] => _tiles[(y * Size) + x];

    /// <summary>Tile the camera starts on, for each player.</summary>
    public IReadOnlyList<(int X, int Y)> CameraTiles => _cameras;

    /// <summary>Tile where the VIP helicopter lands, for each player.</summary>
    public IReadOnlyList<(int X, int Y)> HeliportTiles => _heliports;

    /// <summary>Sprite number in <c>DATA/VBLK-0</c> for a block code; 0 means no sprite.</summary>
    public static byte BlockSprite(byte blockCode) => BlockSprites[blockCode];

    /// <summary>Parses decompressed map data.</summary>
    /// <exception cref="InvalidDataException">The data is too short to be a map.</exception>
    public static MapFile Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < MinimumLength)
        {
            throw new InvalidDataException($"Map data has {data.Length} bytes; at least {MinimumLength} expected.");
        }

        var playerCount = Math.Clamp((int)data[0], 1, MaxPlayers);

        var tiles = new MapFileTile[Size * Size];
        for (var i = 0; i < tiles.Length; i++)
        {
            var raw = data.Slice(TileOffset + (i * TileLength), TileLength);
            var parcel = BinaryPrimitives.ReadUInt16LittleEndian(data[(ParcelOffset + (i * 2))..]);
            tiles[i] = new MapFileTile(raw[0], raw[1], raw[2], raw[3], raw[4], raw[5], raw[6], raw[7], parcel);
        }

        var cameras = new (int, int)[playerCount];
        var heliports = new (int, int)[playerCount];
        for (var player = 0; player < playerCount; player++)
        {
            cameras[player] = ReadTileIndex(data[(CameraOffset + (player * 2))..]);
            heliports[player] = ReadTileIndex(data[(HeliportOffset + (player * 2))..]);
        }

        return new MapFile(playerCount, tiles, cameras, heliports);
    }

    private static (int X, int Y) ReadTileIndex(ReadOnlySpan<byte> data)
    {
        int index = BinaryPrimitives.ReadUInt16LittleEndian(data);
        return (index % Size, index / Size);
    }
}
