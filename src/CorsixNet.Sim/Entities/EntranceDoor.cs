// Behaviour ported from CorsixTH CorsixTH/Lua/objects/doors/entrance_right_door.lua (EntranceDoor).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Entities;

/// <summary>Which edge of its tile an object stands on.</summary>
public enum EdgeOrientation : byte
{
    North,
    West,
}

/// <summary>
/// One half of the hospital's sliding entrance door, placed by the map file.
/// </summary>
/// <remarks>
/// The right half is the master: people stepping onto its tile or the tile in front of it are
/// counted, and the count is passed on to the left half, so both open together. The door is
/// open while the count is positive. How far the door has slid open is presentation and is
/// left to the client.
/// </remarks>
public sealed class EntranceDoor : Entity
{
    /// <summary>Map object type of the left half.</summary>
    public const byte LeftObjectType = 58;

    /// <summary>Map object type of the right half.</summary>
    public const byte RightObjectType = 59;

    public EntranceDoor(int id, TilePosition tile, EdgeOrientation orientation, bool isRight)
        : base(id)
    {
        Tile = tile;
        Orientation = orientation;
        IsRight = isRight;
    }

    public override EntityKind Kind => EntityKind.EntranceDoor;

    public TilePosition Tile { get; }

    public EdgeOrientation Orientation { get; }

    /// <summary>True for the right half, which is the master of the pair.</summary>
    public bool IsRight { get; }

    /// <summary>Id of the other half, or 0 if it has none.</summary>
    public int PartnerId { get; internal set; }

    /// <summary>
    /// People counted on the door's tiles. As in CorsixTH, someone who appears on a door tile
    /// without walking onto it is not counted, so the count can drop below zero.
    /// </summary>
    public int OccupantCount { get; private set; }

    public bool IsOpen => OccupantCount > 0;

    /// <summary>Tiles whose occupants this door counts: only the master counts, its own tile and the one in front.</summary>
    public IEnumerable<TilePosition> ObservedTiles
    {
        get
        {
            if (!IsRight)
            {
                yield break;
            }

            yield return Tile;
            yield return Orientation == EdgeOrientation.North ? Tile with { Y = Tile.Y - 1 } : Tile with { X = Tile.X - 1 };
        }
    }

    /// <summary>Tiles kept clear of building around the door, besides the door tile itself.</summary>
    public IEnumerable<TilePosition> KeepClearTiles
    {
        get
        {
            if (Orientation == EdgeOrientation.North)
            {
                yield return Tile with { Y = Tile.Y - 1 };
                if (IsRight)
                {
                    yield return Tile with { X = Tile.X + 1 };
                    yield return new TilePosition(Tile.X + 1, Tile.Y - 1);
                }
            }
            else
            {
                yield return Tile with { X = Tile.X - 1 };
                if (IsRight)
                {
                    yield return Tile with { Y = Tile.Y + 1 };
                    yield return new TilePosition(Tile.X - 1, Tile.Y + 1);
                }
            }
        }
    }

    /// <summary>Doors change only when people move, not on their own.</summary>
    public override void Update(Simulation simulation)
    {
    }

    /// <summary>Marks the door's tiles on the map: the door edge is tall, nearby tiles are not buildable.</summary>
    internal void Place(LevelMap map)
    {
        ref var tile = ref map[Tile.X, Tile.Y];
        tile.Set(Orientation == EdgeOrientation.North ? TileFlags.TallNorth : TileFlags.TallWest, true);
        tile.Set(TileFlags.Buildable, false);
        foreach (var position in KeepClearTiles)
        {
            if (map.Contains(position.X, position.Y))
            {
                map[position.X, position.Y].Set(TileFlags.Buildable, false);
            }
        }
    }

    internal void OnOccupantChange(Simulation simulation, int delta)
    {
        OccupantCount += delta;
        if (IsRight && simulation.FindEntity(PartnerId) is EntranceDoor left)
        {
            left.OnOccupantChange(simulation, delta);
        }
    }

    internal static EntranceDoor ReadState(int id, SimReader reader)
    {
        return new EntranceDoor(id, reader.ReadTile(), (EdgeOrientation)reader.ReadByte(), reader.ReadBool())
        {
            PartnerId = reader.ReadInt32(),
            OccupantCount = reader.ReadInt32(),
        };
    }

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteTile(Tile);
        writer.WriteByte((byte)Orientation);
        writer.WriteBool(IsRight);
        writer.WriteInt32(PartnerId);
        writer.WriteInt32(OccupantCount);
    }
}
