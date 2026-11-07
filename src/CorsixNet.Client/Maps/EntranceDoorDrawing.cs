using CorsixNet.Client.Rendering;
using CorsixNet.Sim.Entities;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Maps;

/// <summary>
/// Draws entrance doors sliding open and shut, as CorsixTH's <c>EntranceDoor:tick</c> does:
/// one animation frame per tick towards the last frame while open, back to the first while closed.
/// </summary>
internal sealed class EntranceDoorDrawing
{
    /// <summary>Idle animations from CorsixTH's <c>objects/doors/entrance_*_door.lua</c>.</summary>
    private const int RightNorth = 308;
    private const int RightWest = 312;
    private const int LeftNorth = 316;
    private const int LeftWest = 318;

    private static readonly byte[] NoLayers = [];

    private readonly AnimationRenderer _renderer;
    private readonly Dictionary<int, int[]> _framesByAnimation = [];
    private readonly Dictionary<int, (int FrameIndex, long Tick)> _state = [];

    public EntranceDoorDrawing(AnimationRenderer renderer)
    {
        _renderer = renderer;
    }

    /// <summary>Forgets door positions, for example when another level is loaded.</summary>
    public void Reset() => _state.Clear();

    public void Draw(SpriteBatch batch, EntranceDoor door, int anchorX, int anchorY, long tick)
    {
        var animation = (door.IsRight, door.Orientation) switch
        {
            (true, EdgeOrientation.North) => RightNorth,
            (true, _) => RightWest,
            (false, EdgeOrientation.North) => LeftNorth,
            _ => LeftWest,
        };

        var frames = Frames(animation);
        var target = door.IsOpen ? frames.Length - 1 : 0;
        if (!_state.TryGetValue(door.Id, out var state))
        {
            state = (target, tick);
        }

        var steps = (int)Math.Min(Math.Max(0, tick - state.Tick), frames.Length);
        var index = state.FrameIndex < target
            ? Math.Min(target, state.FrameIndex + steps)
            : Math.Max(target, state.FrameIndex - steps);
        _state[door.Id] = (index, tick);

        _renderer.DrawFrame(batch, frames[index], anchorX, anchorY, NoLayers, flipHorizontally: false);
    }

    /// <summary>The frames of an animation's loop, in order.</summary>
    private int[] Frames(int animation)
    {
        if (!_framesByAnimation.TryGetValue(animation, out var frames))
        {
            var set = _renderer.Animations;
            var first = set.FirstFrame(animation);
            frames = new int[set.Length(first)];
            for (var i = 0; i < frames.Length; i++)
            {
                frames[i] = set.Advance(first, i);
            }

            _framesByAnimation[animation] = frames;
        }

        return frames;
    }
}
