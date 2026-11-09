using CorsixNet.Client.Rendering;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Maps;

/// <summary>Draws placed objects with their idle animations, as CorsixTH's <c>Object:initOrientation</c> does.</summary>
/// <remarks>
/// An object's animation is drawn from the anchor of the tile it was placed on, but during the drawing
/// pass of its render attach tile, which decides what it overlaps. CorsixTH achieves the same by
/// attaching the animation to the render attach tile and shifting it back by that tile's screen offset
/// (the animation_offset computed in <c>entities/object.lua</c>).
/// </remarks>
internal sealed class ObjectDrawing
{
    private const Direction N = Direction.North;
    private const Direction E = Direction.East;
    private const Direction S = Direction.South;
    private const Direction W = Direction.West;

    /// <summary>
    /// Idle animations from CorsixTH's <c>objects/*.lua</c> (<c>idle_animations</c>), by facing. A facing without its own
    /// animation draws the mirrored animation of its mirror facing (north and west, east and south: <c>orient_mirror</c>).
    /// </summary>
    private static readonly Dictionary<string, Dictionary<Direction, int>> IdleAnimations = new(StringComparer.Ordinal)
    {
        ["desk"] = new() { [N] = 48, [E] = 50 },
        ["cabinet"] = new() { [N] = 80, [E] = 82 },
        ["chair"] = new() { [N] = 686, [E] = 688 },
        ["reception_desk"] = new() { [N] = 2062, [E] = 2064 },
        ["pharmacy_cabinet"] = new() { [N] = 1578, [S] = 1578 },
        ["crash_trolley"] = new() { [N] = 3838, [S] = 3838 },
        ["screen"] = new() { [N] = 1022, [E] = 1022 },
        ["bench"] = new() { [N] = 112, [E] = 114 },
        ["drinks_machine"] = new() { [S] = 170, [W] = 172, [N] = 174, [E] = 176 },
        ["loo"] = new() { [N] = 1760, [S] = 1760 },
        ["sink"] = new() { [N] = 1748, [S] = 1748 },
        ["extinguisher"] = new() { [N] = 178, [E] = 468, [S] = 470 },
        ["radiator"] = new() { [N] = 750, [E] = 752 },
        ["bin"] = new() { [N] = 1752, [S] = 1752 },
        ["plant"] = new() { [N] = 1950, [S] = 1950, [E] = 1950, [W] = 1950 },
    };

    /// <summary>The screen with a patient's clothes hanging on it (screen.lua's second idle animation, 1204).</summary>
    private const int ScreenWithClothes = 1204;

    private static readonly byte[] NoLayers = [];

    private readonly AnimationRenderer _renderer;

    public ObjectDrawing(AnimationRenderer renderer)
    {
        _renderer = renderer;
    }

    /// <summary>The tile whose drawing pass draws the object (its render attach position); only the drawing order depends on it.</summary>
    public static TilePosition DrawTile(GameContent content, string objectType, TilePosition tile, Direction orientation)
    {
        var offset = content.FindObject(objectType)?.Orientations.GetValueOrDefault(orientation)?.RenderAttach ?? default;
        return new TilePosition(tile.X + offset.X, tile.Y + offset.Y);
    }

    /// <summary>Draws an object placed on <paramref name="tile"/>.</summary>
    /// <param name="hangingClothes">For a screen, the clothes (layer 1) hanging on it, or null.</param>
    /// <param name="frameOffset">
    /// For a plant, its state: CorsixTH draws it still, as the idle animation's first frame plus the state
    /// (<c>Plant:setNextState</c>). Null animates the object as usual.
    /// </param>
    public void Draw(SpriteBatch batch, string objectType, TilePosition tile, Direction orientation, long tick, Color tint, byte? hangingClothes = null, int? frameOffset = null)
    {
        var anchorX = 32 * (tile.X - tile.Y);
        var anchorY = 16 * (tile.X + tile.Y);
        if (!IdleAnimations.TryGetValue(objectType, out var animations))
        {
            return;
        }

        // Directions without their own animation mirror the opposite one (CorsixTH's orient_mirror).
        var flip = false;
        if (!animations.TryGetValue(orientation, out var animation))
        {
            var mirror = orientation switch
            {
                Direction.North => Direction.West,
                Direction.West => Direction.North,
                Direction.East => Direction.South,
                _ => Direction.East,
            };
            if (!animations.TryGetValue(mirror, out animation))
            {
                return;
            }

            flip = true;
        }

        var layers = NoLayers;
        if (hangingClothes is { } clothes)
        {
            animation = ScreenWithClothes;
            layers = [0, clothes];
        }

        var set = _renderer.Animations;
        var first = set.FirstFrame(animation);
        var frame = frameOffset is { } offset ? first + offset : set.Advance(first, (int)(tick % set.Length(first)));
        _renderer.DrawFrame(batch, frame, anchorX, anchorY, layers, flip, tint);
    }
}
