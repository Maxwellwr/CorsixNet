using CorsixNet.Client.Rendering;
using CorsixNet.Formats;
using CorsixNet.Sim.Maps;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Maps;

/// <summary>
/// Draws a <see cref="RoomBlueprint"/> tile by tile, in the map's drawing order: the floor mark as
/// CorsixTH's UI tile layer (after the tile's west wall), then the wall piece attached to the tile.
/// </summary>
/// <remarks>
/// Red parts use the blue sprites recoloured through remap table 6 of <c>QData/Ghost1.dat</c>, the
/// alternative palette CorsixTH's <c>UIEditRoom</c> gives the blueprint animations.
/// </remarks>
internal sealed class BlueprintDrawing : IDisposable
{
    private const int RedTable = 6;

    private static readonly byte[] NoLayers = [];
    private static readonly int[] Animations =
    [
        RoomBlueprint.WallAnimation,
        RoomBlueprint.TopCornerAnimation,
        RoomBlueprint.DoorAnimation,
        RoomBlueprint.WindowAnimation,
    ];

    private readonly SpriteAtlas _blocks;
    private readonly AnimationRenderer _blue;
    private readonly AnimationRenderer _red;

    /// <param name="device">Graphics device.</param>
    /// <param name="install">The game, for the remap tables.</param>
    /// <param name="blocks">The block sheet's atlas, for the floor tiles.</param>
    /// <param name="animations">The renderer for the animation sprites, for the blue walls.</param>
    /// <param name="sprites">The animation sprite sheet the renderer uses.</param>
    /// <param name="palette">The map palette.</param>
    public BlueprintDrawing(GraphicsDevice device, ThemeHospitalInstall install, SpriteAtlas blocks, AnimationRenderer animations, SpriteSheet sprites, Palette palette)
    {
        _blocks = blocks;
        _blue = animations;

        // Only the blueprint sprites get a red copy; the rest of the sheet stays empty.
        var set = animations.Animations;
        var used = new HashSet<int>();
        foreach (var animation in Animations)
        {
            var first = set.FirstFrame(animation);
            for (var i = 0; i < set.Length(first); i++)
            {
                foreach (var element in set.Elements(set.Advance(first, i)))
                {
                    used.Add(element.Sprite);
                }
            }
        }

        var table = PaletteRemaps.Parse(install.ReadDataFile("QDATA", "GHOST1.DAT"))[RedTable].ToArray();
        var red = SpriteSheet.FromImages(Enumerable.Range(0, sprites.Count)
            .Select(i => used.Contains(i) ? sprites[i].Remap(table) : IndexedImage.Empty));
        _red = new AnimationRenderer(device, set, red, palette, animations.Scale);
    }

    public void SetScale(int scale) => _red.SetScale(scale);

    /// <summary>Draws the blueprint's parts on <paramref name="tile"/>, whose top corner is at the anchor.</summary>
    public void DrawTile(SpriteBatch batch, RoomBlueprint blueprint, TilePosition tile, int anchorX, int anchorY)
    {
        if (blueprint.FloorAt(tile) is { } floor)
        {
            _blocks.Draw(batch, floor.Sprite, anchorX - 32, anchorY, floor.Translucent ? Color.White * 0.5f : Color.White);
        }

        if (blueprint.WallAt(tile) is { } wall)
        {
            var renderer = wall.Invalid ? _red : _blue;
            var frame = renderer.Animations.FirstFrame(wall.Animation);
            renderer.DrawFrame(batch, frame, anchorX + wall.OffsetX, anchorY + wall.OffsetY, NoLayers, wall.West);
        }
    }

    public void Dispose() => _red.Dispose();
}
