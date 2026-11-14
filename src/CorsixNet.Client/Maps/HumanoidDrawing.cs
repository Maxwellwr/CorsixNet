using CorsixNet.Client.Rendering;
using CorsixNet.Sim;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Maps;

/// <summary>
/// Turns a humanoid's simulation state into an animation frame and a screen position,
/// following CorsixTH's walk action (<c>humanoid_actions/walk.lua</c>).
/// </summary>
/// <remarks>
/// CorsixTH advances every animation by one frame per tick and restarts it only when a
/// different animation (or flip) is set. Walk cycles are 6 frames long while a step takes
/// 8 ticks, so the cycle runs on across steps in the same direction. This class remembers
/// when each humanoid's current animation started to reproduce that.
/// </remarks>
internal sealed class HumanoidDrawing
{
    /// <summary>Animation numbers from CorsixTH's <c>entities/humanoid.lua</c> (walk_anims).</summary>
    private static readonly Dictionary<HumanoidType, (int WalkNorth, int WalkEast, int IdleNorth, int IdleEast)> WalkAnimations = new()
    {
        [HumanoidType.StandardMalePatient] = (16, 18, 24, 26),
        [HumanoidType.StandardFemalePatient] = (0, 2, 8, 10),
        [HumanoidType.Doctor] = (32, 34, 40, 42),
        [HumanoidType.Nurse] = (1206, 1208, 1650, 1652),
        [HumanoidType.Handyman] = (1858, 1860, 1866, 1868),
        [HumanoidType.Receptionist] = (3668, 3670, 3676, 3678),
        [HumanoidType.StrippedMalePatient] = (818, 820, 826, 828),
        [HumanoidType.StrippedMalePatient2] = (818, 820, 826, 828),
        [HumanoidType.StrippedMalePatient3] = (818, 820, 826, 828),
        [HumanoidType.StrippedFemalePatient] = (834, 836, 842, 844),
        [HumanoidType.StrippedFemalePatient2] = (834, 836, 842, 844),
        [HumanoidType.StrippedFemalePatient3] = (834, 836, 842, 844),
        [HumanoidType.Surgeon] = (2288, 2290, 2296, 2298),
        [HumanoidType.SlackMalePatient] = (1484, 1486, 1492, 1494),
        [HumanoidType.SlackFemalePatient] = (0, 2, 8, 10),
        [HumanoidType.ChewbaccaPatient] = (858, 860, 866, 868),
        [HumanoidType.TransparentMalePatient] = (1064, 1066, 1072, 1074),
        [HumanoidType.TransparentFemalePatient] = (3012, 3014, 3020, 3022),
        [HumanoidType.InvisiblePatient] = (1642, 1644, 1840, 1842),
        [HumanoidType.ElvisPatient] = (978, 980, 986, 988),
        [HumanoidType.AlternateMalePatient] = (2704, 2706, 2712, 2714),
        [HumanoidType.AlienMalePatient] = (3598, 3600, 3606, 3608),
        [HumanoidType.AlienFemalePatient] = (3598, 3600, 3606, 3608),
        [HumanoidType.GownedMalePatient] = (406, 408, 414, 416),
        [HumanoidType.GownedFemalePatient] = (2876, 2878, 2884, 2886),
        [HumanoidType.Vip] = (266, 268, 274, 276),
        [HumanoidType.Inspector] = (266, 268, 274, 276),
        [HumanoidType.GrimReaper] = (994, 996, 1002, 1004),
    };

    private readonly AnimationRenderer _renderer;
    private readonly Dictionary<int, (int Animation, bool Flip, long StartTick)> _current = [];

    public HumanoidDrawing(AnimationRenderer renderer)
    {
        _renderer = renderer;
    }

    /// <summary>
    /// The tile whose drawing pass draws the humanoid. While walking east or south this is the
    /// tile being entered, otherwise the tile being left, so that walls overlap correctly.
    /// </summary>
    public static TilePosition DrawTile(Humanoid humanoid)
    {
        if (humanoid.ActionAnimation is not null)
        {
            return humanoid.ActionDrawTile;
        }

        return humanoid.IsStepping && humanoid.Facing is Direction.East or Direction.South
            ? humanoid.StepTarget
            : humanoid.Tile;
    }

    /// <summary>Forgets animation timing, for example when another level is loaded.</summary>
    public void Reset() => _current.Clear();

    /// <summary>
    /// Draws the humanoid relative to the anchor of its <see cref="DrawTile"/>, with its mood icon above it if it has one
    /// (<see cref="HumanoidMoods"/>).
    /// </summary>
    public void Draw(SpriteBatch batch, Humanoid humanoid, int anchorX, int anchorY, long tick, int? mood = null)
    {
        var drawn = DrawHumanoid(batch, humanoid, anchorX, anchorY, tick);
        _drawn[humanoid.Id] = (drawn, ++_drawOrder);
        FrameDrawn?.Invoke(humanoid.Id, _renderer.Animations.Advance(_renderer.Animations.FirstFrame(drawn.Animation), drawn.Frame), drawn.X, drawn.Y);
        if (mood is { } icon)
        {
            // Entity:setMoodInfo: the icon's animation is a child of the humanoid's (draw_child), placed by the marker of
            // the humanoid's frame (a patient's primary one, anyone else's secondary one) and 96 pixels up.
            var (markerX, markerY) = MoodMarkers.Offset(humanoid.Behaviour is PatientBehaviour, drawn.Animation, drawn.Frame, drawn.Length, drawn.Flip);
            var set = _renderer.Animations;
            var first = set.FirstFrame(icon);
            var frame = set.Advance(first, (int)(tick % set.Length(first)));
            _renderer.DrawFrame(batch, frame, drawn.X + markerX + HumanoidMoods.OffsetX, drawn.Y + markerY + HumanoidMoods.OffsetY, NoLayers, flipHorizontally: false);
        }
    }

    /// <summary>
    /// UIPlaceStaff's animation: the first frame of the humanoid's idle animation facing east (<c>getIdleAnimation</c>),
    /// with layer 5 from their profile, at a tile's anchor.
    /// </summary>
    public void DrawPlacing(SpriteBatch batch, Humanoid humanoid, int anchorX, int anchorY, Color tint)
    {
        var idle = WalkAnimations.GetValueOrDefault(humanoid.Type, WalkAnimations[HumanoidType.StandardMalePatient]).IdleEast;
        Span<byte> layers = stackalloc byte[Humanoid.LayerCount];
        for (var i = 0; i < layers.Length; i++)
        {
            layers[i] = humanoid.Layers[i];
        }

        if (humanoid.Profile is { } profile)
        {
            layers[Humanoid.StaffLayer] = profile.Layer5;
        }

        _renderer.DrawFrame(batch, _renderer.Animations.FirstFrame(idle), anchorX, anchorY, layers, flipHorizontally: false, tint);
    }

    /// <summary>Told of each humanoid drawn: its id, the frame and where (for the frames' sounds).</summary>
    public Action<int, int, int, int>? FrameDrawn { get; set; }

    /// <summary>Forgets where humanoids were drawn, at the start of drawing the map.</summary>
    public void BeginFrame()
    {
        _drawn.Clear();
        _drawOrder = 0;
    }

    /// <summary>
    /// The humanoid whose sprite, as last drawn, is under the world point: of several, the one drawn last, which is in
    /// front (GameUI's <c>hitTestObjects</c>, by the pixels of the frames as CorsixTH tests them).
    /// </summary>
    public int? HumanoidAt(Simulation simulation, Point world)
    {
        int? best = null;
        var bestOrder = -1;
        Span<byte> layers = stackalloc byte[Humanoid.LayerCount];
        foreach (var (id, (drawn, order)) in _drawn)
        {
            if (order < bestOrder || simulation.FindEntity(id) is not Humanoid humanoid)
            {
                continue;
            }

            for (var i = 0; i < layers.Length; i++)
            {
                layers[i] = humanoid.Layers[i];
            }

            var set = _renderer.Animations;
            var frame = set.Advance(set.FirstFrame(drawn.Animation), drawn.Frame);
            if (_renderer.HitTest(frame, drawn.X, drawn.Y, layers, drawn.Flip, world.X, world.Y))
            {
                best = id;
                bestOrder = order;
            }
        }

        return best;
    }

    private readonly Dictionary<int, (Drawn Drawn, int Order)> _drawn = [];
    private int _drawOrder;

    /// <summary>Where a humanoid was drawn, and which frame of which animation.</summary>
    private readonly record struct Drawn(int X, int Y, int Animation, int Frame, int Length, bool Flip);

    private static readonly byte[] NoLayers = new byte[Humanoid.LayerCount];

    /// <summary>Draws the humanoid and returns where and what, as its mood icon follows it.</summary>
    private Drawn DrawHumanoid(SpriteBatch batch, Humanoid humanoid, int anchorX, int anchorY, long tick)
    {
        if (humanoid.ActionAnimation is { } action)
        {
            return DrawAction(batch, humanoid, action, tick);
        }

        var animations = WalkAnimations.GetValueOrDefault(humanoid.Type, WalkAnimations[HumanoidType.StandardMalePatient]);
        // A fast step moves twice as far each tick (walk.lua's factor 2).
        var p = humanoid.StepProgress * (Humanoid.TicksPerStep / humanoid.StepTicks);
        int animation;
        bool flip;
        int offsetX = 0, offsetY = 0;

        if (humanoid.IsStepping)
        {
            // Pixel offsets move 4 across and 2 down per tick; see walk.lua's setTilePositionSpeed.
            (animation, flip, offsetX, offsetY) = humanoid.Facing switch
            {
                Direction.East => (animations.WalkEast, false, -32 + (4 * p), -16 + (2 * p)),
                Direction.West => (animations.WalkNorth, true, -4 * p, -2 * p),
                Direction.South => (animations.WalkEast, true, 32 - (4 * p), -16 + (2 * p)),
                _ => (animations.WalkNorth, false, 4 * p, -2 * p),
            };
        }
        else
        {
            (animation, flip) = humanoid.Facing switch
            {
                Direction.East => (animations.IdleEast, false),
                Direction.West => (animations.IdleNorth, true),
                Direction.South => (animations.IdleEast, true),
                _ => (animations.IdleNorth, false),
            };
        }

        if (!_current.TryGetValue(humanoid.Id, out var state) || state.Animation != animation || state.Flip != flip)
        {
            // A step in progress started its animation when the step began.
            state = (animation, flip, tick - (humanoid.IsStepping ? p : 0));
            _current[humanoid.Id] = state;
        }

        var set = _renderer.Animations;
        var first = set.FirstFrame(animation);
        var elapsed = Math.Max(0, tick - state.StartTick);
        var frame = set.Advance(first, (int)(elapsed % set.Length(first)));

        Span<byte> layers = stackalloc byte[Humanoid.LayerCount];
        for (var i = 0; i < layers.Length; i++)
        {
            layers[i] = humanoid.Layers[i];
        }

        DrawFrame(batch, humanoid, frame, anchorX + offsetX, anchorY + offsetY, layers, flip, tick);
        return new Drawn(anchorX + offsetX, anchorY + offsetY, animation, (int)(elapsed % set.Length(first)), set.Length(first), flip);
    }

    /// <summary>
    /// Draws the animation of using an object, from the tile the simulation gives (the object's, or the use
    /// position's), one frame per tick from when it started, as CorsixTH's use object action sets it.
    /// </summary>
    private Drawn DrawAction(SpriteBatch batch, Humanoid humanoid, AnimationRef action, long tick)
    {
        _current.Remove(humanoid.Id);
        var set = _renderer.Animations;
        var first = set.FirstFrame(action.Id);
        var elapsed = Math.Max(0, tick - humanoid.ActionAnimationStart);
        var frame = set.Advance(first, (int)(elapsed % set.Length(first)));
        Span<byte> layers = stackalloc byte[Humanoid.LayerCount];
        for (var i = 0; i < layers.Length; i++)
        {
            layers[i] = humanoid.Layers[i];
        }

        var tile = humanoid.ActionAnimationTile;
        int x = 32 * (tile.X - tile.Y), y = 16 * (tile.X + tile.Y);

        // die.lua: the flying ghost moves up 4 pixels a tick.
        if (humanoid.Behaviour is PatientBehaviour { DyingStage: DyingStage.Flying })
        {
            y -= (int)(4 * elapsed);
        }
        if (humanoid.MorphAnimation is { } into)
        {
            DrawMorph(batch, humanoid, action, into, frame, elapsed, x, y, layers);
            return new Drawn(x, y, action.Id, (int)(elapsed % set.Length(first)), set.Length(first), humanoid.ActionAnimationFlip);
        }

        DrawFrame(batch, humanoid, frame, x, y, layers, humanoid.ActionAnimationFlip, tick);
        return new Drawn(x, y, action.Id, (int)(elapsed % set.Length(first)), set.Length(first), humanoid.ActionAnimationFlip);
    }

    /// <summary>
    /// Draws a humanoid's frame, wobbling while they have jellyitis and have not been to the jelly vat (jellyitis.lua's
    /// <c>AnimationEffect.Jelly</c>, which <c>Patient:treatDisease</c> turns off). Each patient wobbles from its own point
    /// in the cycle, as CorsixTH's random <c>patient_effect_offset</c>.
    /// </summary>
    private void DrawFrame(SpriteBatch batch, Humanoid humanoid, int frame, int x, int y, ReadOnlySpan<byte> layers, bool flip, long tick)
    {
        if (humanoid.Behaviour is PatientBehaviour { DiseaseId: "jellyitis", IsCured: false, CureRoomsVisited: 0 })
        {
            _renderer.DrawFrameJelly(batch, frame, x, y, layers, flip, tick + ((humanoid.Id * 7919L) % AnimationRenderer.JellyPeriod));
            return;
        }

        _renderer.DrawFrame(batch, frame, x, y, layers, flip);
    }

    /// <summary>
    /// CorsixTH's morph (<c>animation::set_morph_target</c>, <c>tick</c> and <c>draw_morph</c>): a line starts at the
    /// bottom of both animations and rises a little each tick, reaching their top after the shorter one's frames times the
    /// rounds; the animation is drawn above it and the one it turns into below it.
    /// </summary>
    private void DrawMorph(SpriteBatch batch, Humanoid humanoid, AnimationRef action, AnimationRef into, int frame, long elapsed, int x, int y, ReadOnlySpan<byte> layers)
    {
        var set = _renderer.Animations;
        var (originalFrames, originalMinY, originalMaxY) = _renderer.Extent(action.Id, layers);
        var (morphFrames, morphMinY, morphMaxY) = _renderer.Extent(into.Id, layers);
        var duration = Math.Max(1, Math.Min(morphFrames, originalFrames) * Math.Max(1, humanoid.MorphRounds));
        var top = Math.Min(originalMinY, morphMinY);
        var bottom = Math.Max(originalMaxY, morphMaxY);
        var speed = (top - bottom - duration + 1) / duration;
        var line = (int)Math.Max(top, bottom + (Math.Min(elapsed, duration + 1) * speed));

        _renderer.DrawFrameRows(batch, frame, x, y, layers, humanoid.ActionAnimationFlip, y + top, y + line);
        // The animation turned into is not ticked: its first frame shows throughout.
        _renderer.DrawFrameRows(batch, set.FirstFrame(into.Id), x, y, layers, humanoid.MorphFlip, y + line, y + bottom);
    }
}
