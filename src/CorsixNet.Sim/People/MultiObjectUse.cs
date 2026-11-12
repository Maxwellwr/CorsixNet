// Ported from CorsixTH CorsixTH/Lua/humanoid_actions/multi_use_object.lua.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.People;

/// <summary>
/// Two people using an object together, as CorsixTH's multi-use object action: the one using it (such as the nurse
/// at the pharmacy cabinet) plays the object's phases one after the other, and the other one (the patient) plays the
/// matching secondary animation, or is hidden while the first animation shows them too. Each phase lasts as long as
/// the longer of the two animations.
/// </summary>
/// <remarks>
/// Prolonged use plays the in-use phase <see cref="InUseRounds"/> more times, each a loop (the blood machine's). The patient's change of
/// looks during the use (CorsixTH's morphs, for invisible patients) is left to when the client can draw them.
/// </remarks>
public sealed class MultiObjectUse
{
    /// <summary>
    /// Layers 0 to 4 describe the patient: a member of staff using the object takes them on, a patient using it with a
    /// member of staff takes on their layer 5 (<c>copy_layers</c>).
    /// </summary>
    private const int PatientLayers = 5;

    private const int Layer3 = 3;

    private MultiObjectUse(int objectId, int useWithId)
    {
        ObjectId = objectId;
        UseWithId = useWithId;
    }

    /// <summary>Entity id of the object used.</summary>
    public int ObjectId { get; }

    /// <summary>Entity id of the other user (CorsixTH's <c>use_with</c>).</summary>
    public int UseWithId { get; }

    /// <summary>Index of the current phase among <see cref="MultiUseAnimations.Phases"/>.</summary>
    public int Phase { get; private set; }

    /// <summary>Ticks left of the current phase.</summary>
    public int Timer { get; private set; }

    /// <summary>How many more times the in-use phase plays (a prolonged use's loops), 0 to play it once.</summary>
    public int InUseRounds { get; private set; }

    /// <summary>Which of the object's animation variants the use plays (CorsixTH's <c>random_anim</c>).</summary>
    public int Variant { get; private set; }

    /// <summary>
    /// The phases, numbered as CorsixTH does (−5 to −1 the begin phases, 0 the in-use one, 1 to 5 the finish phases), in
    /// which the other user is hidden when the phase has no animation for them; outside them they stand visible
    /// (<c>setInvisiblePhaseSpan</c>). Null hides them in every such phase.
    /// </summary>
    private (int From, int To)? InvisiblePhases { get; init; }

    /// <summary>The value both users' layer 3 takes during the use, or −1 to leave it (CorsixTH's <c>setLayer3</c>).</summary>
    private int UseLayer3 { get; init; } = -1;

    private byte OldLayer3 { get; set; }

    private byte OldLayer3UseWith { get; set; }

    /// <summary>
    /// Starts using <paramref name="placed"/> with <paramref name="useWith"/> (<c>action_multi_use_object_start</c>);
    /// null when the object has no animations for the two.
    /// </summary>
    internal static MultiObjectUse? Start(Simulation simulation, Humanoid humanoid, RoomObject placed, Humanoid useWith, int layer3 = -1, int inUseRounds = 0,
        (int From, int To)? invisiblePhases = null)
    {
        if (simulation.Content.FindObject(placed.ObjectType)?.MultiUse(placed.Orientation, humanoid.Type, useWith.Type) is not var (variants, _))
        {
            return null;
        }

        var use = new MultiObjectUse(placed.Id, useWith.Id)
        {
            UseLayer3 = layer3,
            InUseRounds = inUseRounds,
            InvisiblePhases = invisiblePhases,
            Variant = variants.Count > 1 ? simulation.Random.Next(variants.Count) : 0,
        };
        if (humanoid.Profile is not null)
        {
            for (var layer = 0; layer < PatientLayers; layer++)
            {
                humanoid.SetLayer(layer, useWith.Layers[layer]);
            }
        }
        else if (useWith.Profile is not null)
        {
            humanoid.SetLayer(Humanoid.StaffLayer, useWith.Layers[Humanoid.StaffLayer]);
        }

        if (layer3 >= 0)
        {
            use.OldLayer3 = humanoid.Layers[Layer3];
            use.OldLayer3UseWith = useWith.Layers[Layer3];
            humanoid.SetLayer(Layer3, (byte)layer3);
            useWith.SetLayer(Layer3, (byte)layer3);
        }

        use.EnterPhase(simulation, humanoid, 0);
        return use;
    }

    /// <summary>Advances the use by a tick; <see cref="ObjectUseEvent.Done"/> once the last phase is over.</summary>
    internal ObjectUseEvent Update(Simulation simulation, Humanoid humanoid)
    {
        Timer--;
        if (Timer > 0)
        {
            return ObjectUseEvent.None;
        }

        if (Resolve(simulation, humanoid) is not var (placed, layout, animations, _, useWith))
        {
            return End(simulation, humanoid);
        }

        if (Phase == animations.Begin.Count && InUseRounds > 0)
        {
            // A prolonged use: the in-use animation again.
            InUseRounds--;
            EnterPhase(simulation, humanoid, Phase);
            return ObjectUseEvent.Loop;
        }

        var next = Phase + 1;
        if (next == animations.Begin.Count)
        {
            // Entering the in-use phase: the other user already goes to where they end up, facing the way they end up.
            var secondary = layout.FinishSecondaryUsePosition ?? layout.SecondaryUsePosition ?? layout.Use;
            useWith.PlaceAt(new TilePosition(placed.Tile.X + secondary.X, placed.Tile.Y + secondary.Y));
            if (layout.FinishSecondaryFacing is { } facing)
            {
                useWith.Face(facing);
            }
        }

        if (next >= animations.Begin.Count + 1 + animations.Finish.Count)
        {
            return End(simulation, humanoid);
        }

        EnterPhase(simulation, humanoid, next);
        return ObjectUseEvent.None;
    }

    /// <summary>
    /// The end of the use: layers back, the other user visible again, and the user at the use position. A machine wears
    /// (<c>machineUsed</c>); if it explodes, the users are dead and the use is not reported as done.
    /// </summary>
    private ObjectUseEvent End(Simulation simulation, Humanoid humanoid)
    {
        var useWith = simulation.FindEntity(UseWithId) as Humanoid;
        if (UseLayer3 >= 0)
        {
            humanoid.SetLayer(Layer3, OldLayer3);
            useWith?.SetLayer(Layer3, OldLayer3UseWith);
        }

        if (useWith is not null)
        {
            useWith.IsHidden = false;
            useWith.ClearActionAnimation();
        }

        humanoid.ClearActionAnimation();
        if (simulation.FindEntity(ObjectId) is RoomObject placed && simulation.Content.FindObject(placed.ObjectType)?.Orientations.GetValueOrDefault(placed.Orientation) is { } layout)
        {
            var use = layout.FinishUsePosition ?? layout.Use;
            humanoid.PlaceAt(new TilePosition(placed.Tile.X + use.X, placed.Tile.Y + use.Y));
        }

        if (simulation.FindEntity(ObjectId) is RoomObject { Machine: not null } machine)
        {
            Objects.Machines.Used(simulation, machine, humanoid);
            if (humanoid.IsDespawning)
            {
                return ObjectUseEvent.None;
            }
        }

        return ObjectUseEvent.Done;
    }

    /// <summary><c>action_multi_use_phase</c>: both users' animations for the phase.</summary>
    private void EnterPhase(Simulation simulation, Humanoid humanoid, int phase)
    {
        Phase = phase;
        if (Resolve(simulation, humanoid) is not var (placed, layout, animations, flip, useWith))
        {
            Timer = 1;
            return;
        }

        var current = animations.Phases.ElementAt(phase);
        TilePosition tile, drawTile;
        if (layout.AnimateFromUsePosition)
        {
            tile = drawTile = new TilePosition(placed.Tile.X + layout.Use.X, placed.Tile.Y + layout.Use.Y);
        }
        else
        {
            // Drawn from the object's own tile, in the pass of its render attach tile, as CorsixTH's animation_offset
            // makes up for the attach tile (and as a single use of the object is drawn).
            tile = placed.Tile;
            drawTile = new TilePosition(placed.Tile.X + layout.RenderAttach.X, placed.Tile.Y + layout.RenderAttach.Y);
        }

        humanoid.SetActionAnimation(simulation, current.Primary, flip, tile, drawTile, placed.Id);
        if (current.Secondary is { } secondary)
        {
            useWith.SetActionAnimation(simulation, secondary, flip, useWith.Tile, useWith.Tile, 0);
            useWith.IsHidden = false;
            if (current.SecondaryMorph is { } into)
            {
                // {"morph", A, B} for the one with the user (pharmacy_cabinet.lua's invisible and transparent patients).
                useWith.SetMorph(into, flip, 1);
            }
        }
        else if (InvisiblePhases is var (from, to) && (CorsixPhase(animations, phase) < from || CorsixPhase(animations, phase) > to))
        {
            useWith.ClearActionAnimation();
            useWith.IsHidden = false;
        }
        else
        {
            useWith.IsHidden = true;
        }

        Timer = current.Length;
    }

    /// <summary>CorsixTH's number of a phase: −5 for <c>begin_use</c> up to 5 for <c>finish_use_5</c>.</summary>
    private static int CorsixPhase(MultiUseAnimations animations, int phase) =>
        phase < animations.Begin.Count ? phase - 5 : phase - animations.Begin.Count;

    private (RoomObject Placed, ObjectOrientation Layout, MultiUseAnimations Animations, bool Flip, Humanoid UseWith)? Resolve(Simulation simulation, Humanoid humanoid)
    {
        if (simulation.FindEntity(ObjectId) is not RoomObject placed || simulation.FindEntity(UseWithId) is not Humanoid useWith
            || simulation.Content.FindObject(placed.ObjectType) is not { } definition
            || definition.Orientations.GetValueOrDefault(placed.Orientation) is not { } layout
            || definition.MultiUse(placed.Orientation, humanoid.Type, useWith.Type) is not var (variants, flip))
        {
            return null;
        }

        return (placed, layout, variants[Math.Min(Variant, variants.Count - 1)], flip, useWith);
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(ObjectId);
        writer.WriteInt32(UseWithId);
        writer.WriteInt32(Phase);
        writer.WriteInt32(Timer);
        writer.WriteInt32(Variant);
        writer.WriteInt32(UseLayer3);
        writer.WriteByte(OldLayer3);
        writer.WriteByte(OldLayer3UseWith);
        writer.WriteInt32(InUseRounds);
        writer.WriteBool(InvisiblePhases is not null);
        if (InvisiblePhases is var (from, to))
        {
            writer.WriteInt32(from);
            writer.WriteInt32(to);
        }
    }

    internal static MultiObjectUse Read(SimReader reader)
    {
        var objectId = reader.ReadInt32();
        var useWithId = reader.ReadInt32();
        var phase = reader.ReadInt32();
        var timer = reader.ReadInt32();
        var variant = reader.ReadInt32();
        var layer3 = reader.ReadInt32();
        var oldLayer3 = reader.ReadByte();
        var oldLayer3UseWith = reader.ReadByte();
        var rounds = reader.Version is 0 or >= 63 ? reader.ReadInt32() : 0;
        (int, int)? invisible = (reader.Version is 0 or >= 69) && reader.ReadBool() ? (reader.ReadInt32(), reader.ReadInt32()) : null;
        return new MultiObjectUse(objectId, useWithId)
        {
            Phase = phase,
            Timer = timer,
            Variant = variant,
            UseLayer3 = layer3,
            OldLayer3 = oldLayer3,
            OldLayer3UseWith = oldLayer3UseWith,
            InUseRounds = rounds,
            InvisiblePhases = invisible,
        };
    }

    internal static void WriteOptional(SimWriter writer, MultiObjectUse? use)
    {
        writer.WriteBool(use is not null);
        use?.Write(writer);
    }

    internal static MultiObjectUse? ReadOptional(SimReader reader) => reader.ReadBool() ? Read(reader) : null;
}
