// Drawing order ported from CorsixTH CorsixTH/Src/th_map.cpp (level_map::draw).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Client.Rendering;
using CorsixNet.Sim.Maps;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Maps;

/// <summary>
/// Draws floors, walls and wall shadows of a <see cref="LevelMap"/> in world coordinates.
/// </summary>
/// <remarks>
/// Follows CorsixTH's <c>level_map::draw</c>. Tiles with equal x + y form a scanline (one screen row).
/// Pass 1 draws every floor and floor shadow, so that nothing drawn later is covered by a floor.
/// Pass 2 goes scanline by scanline: north walls right to left, then west walls left to right.
/// </remarks>
internal sealed class MapRenderer
{
    private const int ShadowFullSprite = 74;
    private const int ShadowHalfSprite = 75;
    private const int WallShadowSprite = 156;

    /// <summary>Tallest block sprite plus margin, for culling walls that rise into view from below.</summary>
    private const int MaxBlockHeight = 160;

    /// <summary>CorsixTH draws shadows with 75% transparency.</summary>
    private static readonly Color ShadowTint = Color.White * 0.25f;

    private readonly SpriteAtlas _blocks;

    public MapRenderer(SpriteAtlas blocks)
    {
        _blocks = blocks;
    }

    /// <summary>Draws the part of the map inside <paramref name="view"/> (world pixels). The batch must be begun.</summary>
    /// <param name="batch">A begun sprite batch.</param>
    /// <param name="map">The map to draw.</param>
    /// <param name="view">Visible area in world pixels.</param>
    /// <param name="drawEntities">
    /// Called for each tile after its west wall, with the tile and its anchor (the top corner of
    /// the floor diamond), to draw the entities standing there.
    /// </param>
    public void Draw(SpriteBatch batch, LevelMap map, Rectangle view, Action<TilePosition, int, int>? drawEntities = null)
    {
        var (firstScanline, lastScanline) = VisibleScanlines(map, view);

        for (var s = firstScanline; s <= lastScanline; s++)
        {
            var (firstX, lastX) = VisibleColumns(map, view, s);
            for (var x = firstX; x <= lastX; x++)
            {
                DrawFloor(batch, map, x, s - x);
            }
        }

        for (var s = firstScanline; s <= lastScanline; s++)
        {
            var (firstX, lastX) = VisibleColumns(map, view, s);
            for (var x = lastX; x >= firstX; x--)
            {
                DrawNorthWall(batch, map, x, s - x);
            }

            for (var x = firstX; x <= lastX; x++)
            {
                var y = s - x;
                DrawLayer(batch, map[x, y].WestWall, x, y);
                drawEntities?.Invoke(new TilePosition(x, y), 32 * (x - y), 16 * (x + y));
            }
        }
    }

    /// <summary>Scanlines whose tiles or walls can overlap <paramref name="view"/>.</summary>
    internal static (int First, int Last) VisibleScanlines(LevelMap map, Rectangle view)
    {
        // A tile's sprites span from (tile top + 32 - sprite height) to (tile top + 32).
        var first = Math.Max(0, FloorDiv(view.Top - 32, 16));
        var last = Math.Min(map.Width + map.Height - 2, FloorDiv(view.Bottom + MaxBlockHeight, 16));
        return (first, last);
    }

    /// <summary>Range of x for tiles on scanline <paramref name="s"/> that can overlap <paramref name="view"/> horizontally.</summary>
    internal static (int First, int Last) VisibleColumns(LevelMap map, Rectangle view, int s)
    {
        // Screen x of the tile centre is 32 * (2x - s); tiles are 64 pixels wide.
        var first = Math.Max(Math.Max(0, s - (map.Height - 1)), FloorDiv(view.Left - 32 + (32 * s), 64));
        var last = Math.Min(Math.Min(map.Width - 1, s), FloorDiv(view.Right + 32 + (32 * s), 64) + 1);
        return (first, last);
    }

    private void DrawFloor(SpriteBatch batch, LevelMap map, int x, int y)
    {
        var tile = map[x, y];
        var (left, top) = TileOrigin(x, y);

        var height = _blocks.HeightOf(tile.Floor);
        _blocks.Draw(batch, tile.Floor, left, top - height + 32, Color.White);

        if (tile.Has(TileFlags.ShadowFull))
        {
            _blocks.Draw(batch, ShadowFullSprite, left, top, ShadowTint);
        }
        else if (tile.Has(TileFlags.ShadowHalf))
        {
            _blocks.Draw(batch, ShadowHalfSprite, left, top, ShadowTint);
        }
    }

    private void DrawNorthWall(SpriteBatch batch, LevelMap map, int x, int y)
    {
        var tile = map[x, y];
        var height = DrawLayer(batch, tile.NorthWall, x, y);
        if (height > 0 && tile.Has(TileFlags.ShadowWall))
        {
            var (left, top) = TileOrigin(x, y);
            var clip = new Rectangle(left, top - height + 32 + 4, 64, 86 - 4);
            _blocks.DrawClipped(batch, WallShadowSprite, left, top - 56, clip, ShadowTint);
        }
    }

    /// <summary>Draws a wall sprite standing on the tile; returns its height, or 0 if there is none.</summary>
    private int DrawLayer(SpriteBatch batch, ushort sprite, int x, int y)
    {
        if (sprite == 0)
        {
            return 0;
        }

        var height = _blocks.HeightOf(sprite);
        if (height > 0)
        {
            var (left, top) = TileOrigin(x, y);
            _blocks.Draw(batch, sprite, left, top - height + 32, Color.White);
        }

        return height;
    }

    /// <summary>Top-left corner of the 64x32 floor diamond's bounding box.</summary>
    private static (int Left, int Top) TileOrigin(int x, int y) => ((32 * (x - y)) - 32, 16 * (x + y));

    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);
}
