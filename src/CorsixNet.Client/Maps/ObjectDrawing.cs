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
    /// <summary>
    /// Idle animations from CorsixTH's <c>objects/*.lua</c>; south and west mirror east and north. Where an object has
    /// only north and south ones (the pharmacy cabinet), east mirrors south, which is the north one.
    /// </summary>
    private static readonly Dictionary<string, (int North, int East, bool EastMirrorsNorth)> IdleAnimations = new(StringComparer.Ordinal)
    {
        ["desk"] = (48, 50, false),
        ["cabinet"] = (80, 82, false),
        ["chair"] = (686, 688, false),
        ["reception_desk"] = (2062, 2064, false),
        ["pharmacy_cabinet"] = (1578, 1578, true),
        ["crash_trolley"] = (3838, 3838, true),
        ["screen"] = (1022, 1022, false),
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
    public void Draw(SpriteBatch batch, string objectType, TilePosition tile, Direction orientation, long tick, Color tint, byte? hangingClothes = null)
    {
        var anchorX = 32 * (tile.X - tile.Y);
        var anchorY = 16 * (tile.X + tile.Y);
        if (!IdleAnimations.TryGetValue(objectType, out var animations))
        {
            return;
        }

        // Directions without their own animation mirror the opposite one (CorsixTH's orient_mirror).
        var (animation, flip) = orientation switch
        {
            Direction.North => (animations.North, false),
            Direction.East => (animations.East, animations.EastMirrorsNorth),
            Direction.South => (animations.East, !animations.EastMirrorsNorth),
            _ => (animations.North, true),
        };
        var layers = NoLayers;
        if (hangingClothes is { } clothes)
        {
            animation = ScreenWithClothes;
            layers = [0, clothes];
        }

        var set = _renderer.Animations;
        var first = set.FirstFrame(animation);
        var frame = set.Advance(first, (int)(tick % set.Length(first)));
        _renderer.DrawFrame(batch, frame, anchorX, anchorY, layers, flip, tint);
    }
}
