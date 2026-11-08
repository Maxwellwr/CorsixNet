// Ported from CorsixTH CorsixTH/Lua/humanoid_actions/use_object.lua.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.People;

/// <summary>Where someone using an object is.</summary>
public enum ObjectUsePhase : byte
{
    /// <summary>Stepping from the use position onto the object (chairs).</summary>
    WalkIn,

    Begin,
    InUse,
    Finish,

    /// <summary>Stepping back off the object.</summary>
    WalkOut,

    Done,
}

/// <summary>What happened in one tick of using an object.</summary>
public enum ObjectUseEvent
{
    None,

    /// <summary>One round of the in-use animation ended and the use goes on (CorsixTH's loop callback).</summary>
    Loop,

    /// <summary>The use is over; the humanoid stands at the use position (or the finish position) again.</summary>
    Done,
}

/// <summary>
/// Someone using an object, as CorsixTH's use object action: an optional step onto the object, the
/// begin animation, the in-use animation, the finish animation and the step back. Each animation lasts
/// as many ticks as it has frames. A use is prolonged when the object has begin, in-use and finish
/// animations: the in-use animation then repeats until it is interrupted. Otherwise it plays
/// once.
/// </summary>
public sealed class ObjectUse
{
    private ObjectUse(int objectId, TilePosition oldTile)
    {
        ObjectId = objectId;
        OldTile = oldTile;
    }

    /// <summary>Entity id of the object used.</summary>
    public int ObjectId { get; }

    public ObjectUsePhase Phase { get; private set; }

    /// <summary>Ticks left of the current animation.</summary>
    public int Timer { get; private set; }

    /// <summary>Whether the user has been told to stop after the current round.</summary>
    public bool StopRequested { get; private set; }

    /// <summary>Where the user stands when the use ends.</summary>
    public TilePosition OldTile { get; }

    /// <summary>Whether the user is on or at the object, using it (CorsixTH's <c>user_of</c>).</summary>
    public bool IsUsing => Phase is ObjectUsePhase.Begin or ObjectUsePhase.InUse or ObjectUsePhase.Finish;

    /// <summary>Starts using <paramref name="placed"/>; the humanoid stands at its use position.</summary>
    internal static ObjectUse Start(Simulation simulation, Humanoid humanoid, RoomObject placed)
    {
        var layout = Layout(simulation, placed);
        var old = layout?.FinishUsePosition is { } finish
            ? new TilePosition(placed.Tile.X + finish.X, placed.Tile.Y + finish.Y)
            : humanoid.Tile;
        var use = new ObjectUse(placed.Id, old);
        if (simulation.Content.FindObject(placed.ObjectType)?.WalkInToUse == true)
        {
            use.Phase = ObjectUsePhase.WalkIn;
            humanoid.RawStepTo(placed.Tile);
        }
        else
        {
            use.EnterPhase(simulation, humanoid, ObjectUsePhase.Begin);
        }

        return use;
    }

    /// <summary>
    /// Ends a prolonged use, as interrupting CorsixTH's use object action does: the in-use animation is cut
    /// short and the finish animation starts on the next tick.
    /// </summary>
    internal void Interrupt()
    {
        StopRequested = true;
        if (Phase == ObjectUsePhase.InUse)
        {
            Timer = 1;
        }
    }

    /// <summary>Advances the use by a tick; call while the humanoid is not walking.</summary>
    internal ObjectUseEvent Update(Simulation simulation, Humanoid humanoid)
    {
        switch (Phase)
        {
            case ObjectUsePhase.WalkIn:
                EnterPhase(simulation, humanoid, ObjectUsePhase.Begin);
                return ObjectUseEvent.None;
            case ObjectUsePhase.WalkOut:
                Finish(humanoid);
                return ObjectUseEvent.Done;
            case ObjectUsePhase.Done:
                return ObjectUseEvent.Done;
        }

        Timer--;
        if (Timer > 0)
        {
            return ObjectUseEvent.None;
        }

        if (Phase == ObjectUsePhase.InUse && IsProlonged(simulation, humanoid) && !StopRequested)
        {
            EnterPhase(simulation, humanoid, ObjectUsePhase.InUse);
            return ObjectUseEvent.Loop;
        }

        EnterPhase(simulation, humanoid, Phase + 1);
        return Phase == ObjectUsePhase.Done ? ObjectUseEvent.Done : ObjectUseEvent.None;
    }

    private void EnterPhase(Simulation simulation, Humanoid humanoid, ObjectUsePhase phase)
    {
        var placed = simulation.FindEntity(ObjectId) as RoomObject;
        var usage = placed is null ? null : simulation.Content.FindObject(placed.ObjectType)?.Usage(placed.Orientation, humanoid.Type);
        while (true)
        {
            Phase = phase;
            if (phase == ObjectUsePhase.WalkOut)
            {
                if (placed is not null && simulation.Content.FindObject(placed.ObjectType)?.WalkInToUse == true)
                {
                    humanoid.RawStepTo(OldTile);
                    return;
                }

                phase = ObjectUsePhase.Done;
                continue;
            }

            if (phase == ObjectUsePhase.Done)
            {
                Finish(humanoid);
                return;
            }

            if (placed is null || usage is not { } found || PickAnimation(simulation, found.Animations, phase) is not { } animation)
            {
                phase++;
                continue;
            }

            var layout = Layout(simulation, placed);
            TilePosition tile, drawTile;
            if (layout?.AnimateFromUsePosition == true)
            {
                tile = drawTile = OldTile;
            }
            else
            {
                var attach = layout?.RenderAttach ?? default;
                tile = placed.Tile;
                drawTile = new TilePosition(placed.Tile.X + attach.X, placed.Tile.Y + attach.Y);
            }

            humanoid.SetActionAnimation(simulation, animation, found.Flip, tile, drawTile, placed.Id);
            Timer = animation.Length;
            return;
        }
    }

    private void Finish(Humanoid humanoid)
    {
        Phase = ObjectUsePhase.Done;
        humanoid.ClearActionAnimation();
        humanoid.PlaceAt(OldTile);
    }

    private static AnimationRef? PickAnimation(Simulation simulation, UseAnimations animations, ObjectUsePhase phase)
    {
        return phase switch
        {
            ObjectUsePhase.Begin => animations.Begin,
            ObjectUsePhase.InUse when animations.InUse.Count == 1 => animations.InUse[0],
            ObjectUsePhase.InUse when animations.InUse.Count > 1 => animations.InUse[simulation.Random.Next(animations.InUse.Count)],
            ObjectUsePhase.Finish => animations.Finish,
            _ => null,
        };
    }

    /// <summary>CorsixTH prolongs a use when the object has begin, in-use and finish animations.</summary>
    private bool IsProlonged(Simulation simulation, Humanoid humanoid)
    {
        return simulation.FindEntity(ObjectId) is RoomObject placed
            && simulation.Content.FindObject(placed.ObjectType)?.Usage(placed.Orientation, humanoid.Type) is { } usage
            && usage.Animations is { Begin: not null, Finish: not null, InUse.Count: > 0 };
    }

    private static ObjectOrientation? Layout(Simulation simulation, RoomObject placed)
    {
        return simulation.Content.FindObject(placed.ObjectType)?.Orientations.GetValueOrDefault(placed.Orientation);
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(ObjectId);
        writer.WriteTile(OldTile);
        writer.WriteByte((byte)Phase);
        writer.WriteInt32(Timer);
        writer.WriteBool(StopRequested);
    }

    internal static ObjectUse Read(SimReader reader)
    {
        return new ObjectUse(reader.ReadInt32(), reader.ReadTile())
        {
            Phase = (ObjectUsePhase)reader.ReadByte(),
            Timer = reader.ReadInt32(),
            StopRequested = reader.ReadBool(),
        };
    }

    internal static void WriteOptional(SimWriter writer, ObjectUse? use)
    {
        writer.WriteBool(use is not null);
        use?.Write(writer);
    }

    internal static ObjectUse? ReadOptional(SimReader reader) => reader.ReadBool() ? Read(reader) : null;
}
