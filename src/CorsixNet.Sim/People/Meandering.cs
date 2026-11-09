// Idle staff wandering about, ported from CorsixTH CorsixTH/Lua/humanoid_actions/meander.lua (meander_action_start)
// and the IdleAction it alternates with.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.People;

/// <summary>
/// CorsixTH's <c>MeanderAction</c> for a member of staff with nothing to do: they walk to one of the 24 nearest idle
/// tiles, picked at random, then stand there for 5 to 40 ticks, and so on. With nowhere to walk to they stand still
/// for good (an <c>IdleAction</c> without a count), until they are sent somewhere.
/// </summary>
internal sealed class Meandering
{
    private const int Forever = -1;

    /// <summary>Whether the next round idles rather than walks (<c>action.can_idle</c>).</summary>
    private bool _canIdle;

    /// <summary>Ticks left standing, or <see cref="Forever"/>.</summary>
    private int _idleTicks;

    /// <summary>Called on each tick that the humanoid is idle and not walking.</summary>
    internal void Update(Simulation simulation, Humanoid humanoid)
    {
        if (_idleTicks == Forever)
        {
            return;
        }

        if (_idleTicks > 0)
        {
            _idleTicks--;
            return;
        }

        var start = humanoid.Tile;
        if (!simulation.Pathfinder.TryFindIdleTile(simulation.Map, start, simulation.Random.NextInclusive(1, 24), out var tile) || tile == start)
        {
            _idleTicks = Forever;
            return;
        }

        if (_canIdle)
        {
            _canIdle = false;
            _idleTicks = simulation.Random.NextInclusive(5, 40);
        }
        else
        {
            _canIdle = true;
            humanoid.WalkTo(simulation, tile);
        }
    }

    /// <summary>The humanoid has something to do: the next time they are idle, they start with a walk.</summary>
    internal void Reset()
    {
        _canIdle = false;
        _idleTicks = 0;
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteBool(_canIdle);
        writer.WriteInt32(_idleTicks);
    }

    internal static Meandering Read(SimReader reader)
    {
        var meandering = new Meandering { _canIdle = reader.ReadBool(), _idleTicks = reader.ReadInt32() };
        return meandering._idleTicks >= Forever ? meandering : throw new InvalidDataException($"Bad idle time {meandering._idleTicks}.");
    }
}
