using CorsixNet.Client.Rendering;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
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
        [HumanoidType.StrippedFemalePatient] = (834, 836, 842, 844),
        [HumanoidType.Vip] = (266, 268, 274, 276),
        [HumanoidType.Inspector] = (266, 268, 274, 276),
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

    /// <summary>Draws the humanoid relative to the anchor of its <see cref="DrawTile"/>.</summary>
    public void Draw(SpriteBatch batch, Humanoid humanoid, int anchorX, int anchorY, long tick)
    {
        if (humanoid.ActionAnimation is { } action)
        {
            DrawAction(batch, humanoid, action, tick);
            return;
        }

        var animations = WalkAnimations.GetValueOrDefault(humanoid.Type, WalkAnimations[HumanoidType.StandardMalePatient]);
        var p = humanoid.StepProgress;
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

        _renderer.DrawFrame(batch, frame, anchorX + offsetX, anchorY + offsetY, layers, flip);
    }

    /// <summary>
    /// Draws the animation of using an object, from the tile the simulation gives (the object's, or the use
    /// position's), one frame per tick from when it started, as CorsixTH's use object action sets it.
    /// </summary>
    private void DrawAction(SpriteBatch batch, Humanoid humanoid, AnimationRef action, long tick)
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
        _renderer.DrawFrame(batch, frame, 32 * (tile.X - tile.Y), 16 * (tile.X + tile.Y), layers, humanoid.ActionAnimationFlip);
    }
}
