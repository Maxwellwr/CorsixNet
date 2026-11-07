using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Objects;

/// <summary>An object that a map file places on a tile.</summary>
/// <param name="Tile">Tile the object stands on.</param>
/// <param name="ObjectType">Object type number from the map file (CorsixTH calls it "thob").</param>
/// <param name="Direction">Direction byte from the map file; even means north, odd means west.</param>
public readonly record struct MapObjectPlacement(TilePosition Tile, byte ObjectType, byte Direction)
{
    public void Write(SimWriter writer)
    {
        writer.WriteTile(Tile);
        writer.WriteByte(ObjectType);
        writer.WriteByte(Direction);
    }

    public static MapObjectPlacement Read(SimReader reader) => new(reader.ReadTile(), reader.ReadByte(), reader.ReadByte());
}
