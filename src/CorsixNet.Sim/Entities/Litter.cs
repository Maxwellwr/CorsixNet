// Ported from CorsixTH CorsixTH/Lua/objects/litter.lua (Litter) and CorsixTH/Lua/world.lua (World:newLitter,
// World:removeAllLitter).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Entities;

/// <summary>The kinds of litter (CorsixTH's <c>litter_types</c>); the first four are what patients drop, by number.</summary>
public enum LitterType : byte
{
    SodaCan = 1,
    Banana = 2,
    Paper = 3,
    Bottle = 4,
    Pee = 5,
    DeadRat = 6,
    Puke = 7,
    SootFloor = 8,
    SootWall = 9,
    SootWindow = 10,
}

/// <summary>
/// Something small on the floor or a wall (CorsixTH's <c>Litter</c>): rubbish patients drop, and in CorsixTH also pee,
/// puke, dead rats and soot. A tile holds at most one, the higher ranked winning.
/// </summary>
public sealed class Litter : Entity
{
    /// <param name="id">The entity id.</param>
    /// <param name="player">The hospital it belongs to.</param>
    /// <param name="type">What it is.</param>
    /// <param name="tile">Where it lies.</param>
    /// <param name="isMirrored">Whether its sprite is drawn mirrored.</param>
    public Litter(int id, int player, LitterType type, TilePosition tile, bool isMirrored)
        : base(id)
    {
        Player = player;
        Type = type;
        Tile = tile;
        IsMirrored = isMirrored;
    }

    public override EntityKind Kind => EntityKind.Litter;

    /// <summary>The hospital it belongs to.</summary>
    public int Player { get; }

    /// <summary>What it is.</summary>
    public LitterType Type { get; }

    /// <summary>Where it lies.</summary>
    public TilePosition Tile { get; }

    /// <summary>Whether its sprite is drawn mirrored.</summary>
    public bool IsMirrored { get; }

    /// <summary>Swept up or displaced; it leaves the world at the end of the tick.</summary>
    public bool IsRemoved { get; private set; }

    /// <summary>Its animation (<c>litter_types</c>).</summary>
    public int Animation => Type switch
    {
        LitterType.SodaCan => 1894,
        LitterType.Banana => 1896,
        LitterType.Paper => 1898,
        LitterType.Bottle => 1900,
        LitterType.Pee => 2248,
        LitterType.DeadRat => 2242,
        LitterType.Puke => 2060,
        LitterType.SootFloor => 3416,
        LitterType.SootWall => 3408,
        _ => 3412,
    };

    /// <summary><c>Litter:getRank</c>: litter of a higher rank replaces a lower one on the same tile.</summary>
    public int Rank => RankOf(Type);

    /// <summary><c>Litter:vomitInducing</c>: puke, pee and dead rats.</summary>
    public bool IsVomitInducing => Type is LitterType.Puke or LitterType.Pee or LitterType.DeadRat;

    /// <summary><c>Litter:anyLitter</c>: the rubbish patients drop.</summary>
    public bool IsRubbish => Type is LitterType.SodaCan or LitterType.Banana or LitterType.Paper or LitterType.Bottle;

    /// <summary><c>Litter:isCleanable</c>: everything but soot.</summary>
    public bool IsCleanable => IsVomitInducing || IsRubbish;

    public override void Update(Simulation simulation)
    {
        // CorsixTH's litter does not tick.
    }

    /// <summary>
    /// <c>World:newLitter</c>: litter on a tile, unless litter of the same or a higher rank lies there already, which a
    /// lower ranked one makes way for.
    /// </summary>
    /// <returns>The new litter, or null if it was outclassed.</returns>
    internal static Litter? Drop(Simulation simulation, int player, LitterType type, TilePosition tile, bool isMirrored)
    {
        // Litter.resolveTileRank: the first litter on the tile decides.
        if (At(simulation, tile).FirstOrDefault() is { } existing)
        {
            if (existing.Rank >= RankOf(type))
            {
                return null;
            }

            existing.IsRemoved = true;
        }

        return simulation.AddEntity(id => new Litter(id, player, type, tile, isMirrored));
    }

    /// <summary><c>Litter:remove</c>: swept up by a handyman.</summary>
    internal void Remove() => IsRemoved = true;

    /// <summary><c>World:removeAllLitter</c>: the cleanable litter of a tile goes, as when something is built there.</summary>
    internal static void RemoveAllAt(Simulation simulation, TilePosition tile)
    {
        foreach (var litter in At(simulation, tile))
        {
            if (litter.IsCleanable)
            {
                litter.IsRemoved = true;
            }
        }
    }

    /// <summary>The litter lying on a tile, oldest first.</summary>
    internal static IEnumerable<Litter> At(Simulation simulation, TilePosition tile) =>
        simulation.Entities.OfType<Litter>().Where(litter => !litter.IsRemoved && litter.Tile == tile);

    /// <summary><c>Litter.getRankForType</c>.</summary>
    private static int RankOf(LitterType type) => type switch
    {
        LitterType.Puke => 4,
        LitterType.DeadRat => 3,
        LitterType.Pee => 2,
        LitterType.SootFloor or LitterType.SootWall or LitterType.SootWindow => 99,
        _ => 1,
    };

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteInt32(Player);
        writer.WriteByte((byte)Type);
        writer.WriteTile(Tile);
        writer.WriteBool(IsMirrored);
    }

    internal static Litter ReadState(int id, SimReader reader)
    {
        var player = reader.ReadInt32();
        var type = reader.ReadByte();
        if (type is < (byte)LitterType.SodaCan or > (byte)LitterType.SootWindow)
        {
            throw new InvalidDataException($"Unknown litter type {type}.");
        }

        return new Litter(id, player, (LitterType)type, reader.ReadTile(), reader.ReadBool());
    }
}
