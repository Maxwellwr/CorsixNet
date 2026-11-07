using System.Buffers.Binary;
using CorsixNet.Formats;

namespace CorsixNet.Content.Tests;

/// <summary>Builds synthetic map file data for tests.</summary>
internal sealed class MapFileBuilder
{
    private const int TileOffset = 34;
    private const int ParcelOffset = 131106;
    private const int CameraOffset = 163876;
    private const int HeliportOffset = 163884;

    private readonly byte[] _data = new byte[MapFile.MinimumLength];

    public MapFileBuilder(int playerCount = 1)
    {
        _data[0] = (byte)playerCount;

        // Default every tile to plain passable floor inside the hospital.
        for (var y = 0; y < MapFile.Size; y++)
        {
            for (var x = 0; x < MapFile.Size; x++)
            {
                Tile(x, y, floor: 1);
            }
        }
    }

    public MapFileBuilder Tile(
        int x,
        int y,
        byte floor = 1,
        byte northWall = 0,
        byte westWall = 0,
        byte flags = 0,
        byte extraFlags = 0,
        byte objectType = 0,
        byte objectDirection = 0,
        ushort parcel = 0)
    {
        var index = (y * MapFile.Size) + x;
        var tile = _data.AsSpan(TileOffset + (index * 8), 8);
        tile[0] = objectDirection;
        tile[1] = objectType;
        tile[2] = floor;
        tile[3] = northWall;
        tile[4] = westWall;
        tile[5] = flags;
        tile[7] = extraFlags;
        BinaryPrimitives.WriteUInt16LittleEndian(_data.AsSpan(ParcelOffset + (index * 2)), parcel);
        return this;
    }

    public MapFileBuilder Camera(int player, int x, int y)
    {
        WriteIndex(CameraOffset + (player * 2), x, y);
        return this;
    }

    public MapFileBuilder Heliport(int player, int x, int y)
    {
        WriteIndex(HeliportOffset + (player * 2), x, y);
        return this;
    }

    public byte[] Build() => (byte[])_data.Clone();

    private void WriteIndex(int offset, int x, int y)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(_data.AsSpan(offset), (ushort)((y * MapFile.Size) + x));
    }
}
