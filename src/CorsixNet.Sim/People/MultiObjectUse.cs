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
/// Prolonged use, which no ported room needs yet, is left out: the in-use phase plays once. The patient's change of
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

    /// <summary>Which of the object's animation variants the use plays (CorsixTH's <c>random_anim</c>).</summary>
    public int Variant { get; private set; }

    /// <summary>The value both users' layer 3 takes during the use, or −1 to leave it (CorsixTH's <c>setLayer3</c>).</summary>
    private int UseLayer3 { get; init; } = -1;

    private byte OldLayer3 { get; set; }

    private byte OldLayer3UseWith { get; set; }

    /// <summary>
    /// Starts using <paramref name="placed"/> with <paramref name="useWith"/> (<c>action_multi_use_object_start</c>);
    /// null when the object has no animations for the two.
    /// </summary>
    internal static MultiObjectUse? Start(Simulation simulation, Humanoid humanoid, RoomObject placed, Humanoid useWith, int layer3 = -1)
    {
        if (simulation.Content.FindObject(placed.ObjectType)?.MultiUse(placed.Orientation, humanoid.Type, useWith.Type) is not var (variants, _))
        {
            return null;
        }

        var use = new MultiObjectUse(placed.Id, useWith.Id)
        {
            UseLayer3 = layer3,
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
            End(simulation, humanoid);
            return ObjectUseEvent.Done;
        }

        var next = Phase + 1;
        if (next == animations.Begin.Count)
        {
            // Entering the in-use phase: the other user already goes to where they end up.
            var secondary = layout.SecondaryUsePosition ?? layout.Use;
            useWith.PlaceAt(new TilePosition(placed.Tile.X + secondary.X, placed.Tile.Y + secondary.Y));
        }

        if (next >= animations.Begin.Count + 1 + animations.Finish.Count)
        {
            End(simulation, humanoid);
            return ObjectUseEvent.Done;
        }

        EnterPhase(simulation, humanoid, next);
        return ObjectUseEvent.None;
    }

    /// <summary>The end of the use: layers back, the other user visible again, and the user at the use position.</summary>
    private void End(Simulation simulation, Humanoid humanoid)
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
        TilePosition tile;
        if (layout.AnimateFromUsePosition)
        {
            tile = new TilePosition(placed.Tile.X + layout.Use.X, placed.Tile.Y + layout.Use.Y);
        }
        else
        {
            tile = new TilePosition(placed.Tile.X + layout.RenderAttach.X, placed.Tile.Y + layout.RenderAttach.Y);
        }

        humanoid.SetActionAnimation(simulation, current.Primary, flip, tile, tile, placed.Id);
        if (current.Secondary is { } secondary)
        {
            useWith.SetActionAnimation(simulation, secondary, flip, useWith.Tile, useWith.Tile, 0);
            useWith.IsHidden = false;
        }
        else
        {
            useWith.IsHidden = true;
        }

        Timer = current.Length;
    }

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
    }

    internal static MultiObjectUse Read(SimReader reader)
    {
        return new MultiObjectUse(reader.ReadInt32(), reader.ReadInt32())
        {
            Phase = reader.ReadInt32(),
            Timer = reader.ReadInt32(),
            Variant = reader.ReadInt32(),
            UseLayer3 = reader.ReadInt32(),
            OldLayer3 = reader.ReadByte(),
            OldLayer3UseWith = reader.ReadByte(),
        };
    }

    internal static void WriteOptional(SimWriter writer, MultiObjectUse? use)
    {
        writer.WriteBool(use is not null);
        use?.Write(writer);
    }

    internal static MultiObjectUse? ReadOptional(SimReader reader) => reader.ReadBool() ? Read(reader) : null;
}
