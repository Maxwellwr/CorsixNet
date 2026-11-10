// Element drawing rules ported from CorsixTH CorsixTH/Src/th_gfx.cpp (animation_manager::draw_frame).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Formats;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Rendering;

/// <summary>Draws frames of the original game's animations.</summary>
internal sealed class AnimationRenderer : IDisposable
{
    private const byte FlipHorizontal = 1;
    private const byte FlipVertical = 2;
    private const byte Alpha50 = 4;
    private const byte Alpha75 = 8;

    /// <summary>Layer whose head variants 5–6 fall back to 1–2 when an animation lacks them (CorsixTH workaround).</summary>
    private const int HeadLayer = 5;

    private readonly SpriteAtlas _sprites;

    public AnimationRenderer(GraphicsDevice device, AnimationSet animations, SpriteSheet sprites, Palette palette, int scale = 1)
    {
        Animations = animations;
        _sprites = new SpriteAtlas(device, sprites, palette, scale);
    }

    public AnimationSet Animations { get; }

    /// <summary>Texture pixels per original pixel; see <see cref="SpriteAtlas.Scale"/>.</summary>
    public int Scale => _sprites.Scale;

    public void SetScale(int scale) => _sprites.SetScale(scale);

    /// <summary>
    /// Draws <paramref name="frame"/> with its anchor at (<paramref name="x"/>, <paramref name="y"/>).
    /// </summary>
    /// <param name="batch">A begun sprite batch.</param>
    /// <param name="frame">Frame number.</param>
    /// <param name="x">Anchor x: the horizontal centre of the tile.</param>
    /// <param name="y">Anchor y: the top corner of the tile.</param>
    /// <param name="layers">Chosen variant per layer class; elements of other variants are skipped.</param>
    /// <param name="flipHorizontally">Mirror the whole frame around the anchor.</param>
    public void DrawFrame(SpriteBatch batch, int frame, int x, int y, ReadOnlySpan<byte> layers, bool flipHorizontally)
    {
        DrawFrame(batch, frame, x, y, layers, flipHorizontally, Color.White);
    }

    /// <summary>Draws a frame tinted by <paramref name="tint"/> (premultiplied), for example semi-transparent for previews.</summary>
    public void DrawFrame(SpriteBatch batch, int frame, int x, int y, ReadOnlySpan<byte> layers, bool flipHorizontally, Color tint)
    {
        DrawFrame(batch, frame, x, y, layers, flipHorizontally, tint, int.MinValue, int.MaxValue);
    }

    /// <summary>
    /// Draws only the rows of a frame from <paramref name="top"/> down to before <paramref name="bottom"/> (world
    /// coordinates), as CorsixTH's morph clips each of its two animations (<c>animation::draw_morph</c>).
    /// </summary>
    public void DrawFrameRows(SpriteBatch batch, int frame, int x, int y, ReadOnlySpan<byte> layers, bool flipHorizontally, int top, int bottom)
    {
        DrawFrame(batch, frame, x, y, layers, flipHorizontally, Color.White, top, bottom);
    }

    /// <summary>
    /// <c>GetAnimationDurationAndExtent</c>: how many frames an animation has and the highest and lowest rows any of them
    /// covers, relative to the anchor (the bottom one past the last row, plus one as <c>get_frame_extent</c> counts it).
    /// </summary>
    public (int Frames, int MinY, int MaxY) Extent(int animation, ReadOnlySpan<byte> layers)
    {
        var first = Animations.FirstFrame(animation);
        var frames = Animations.Length(first);
        int minY = int.MaxValue, maxY = int.MinValue;
        for (var i = 0; i < frames; i++)
        {
            foreach (var element in Animations.Elements(Animations.Advance(first, i)))
            {
                var height = _sprites.HeightOf(element.Sprite);
                if ((element.LayerId != 0 && (element.Layer >= layers.Length ? 0 : layers[element.Layer]) != element.LayerId) || height == 0)
                {
                    continue;
                }

                minY = Math.Min(minY, element.Y);
                maxY = Math.Max(maxY, element.Y + height + 1);
            }
        }

        return (frames, minY, maxY);
    }

    private void DrawFrame(SpriteBatch batch, int frame, int x, int y, ReadOnlySpan<byte> layers, bool flipHorizontally, Color tint, int top, int bottom)
    {
        foreach (var element in Animations.Elements(frame))
        {
            if (!IsVisible(element, layers))
            {
                continue;
            }

            var width = _sprites.WidthOf(element.Sprite);
            if (width == 0)
            {
                continue;
            }

            var flags = flipHorizontally ? (byte)(element.Flags ^ FlipHorizontal) : element.Flags;
            if ((flags & (Alpha50 | Alpha75)) == (Alpha50 | Alpha75))
            {
                // Both transparency flags together mean "do not draw".
                continue;
            }

            var left = flipHorizontally ? x - element.X - width : x + element.X;
            var effects = SpriteEffects.None;
            if ((flags & FlipHorizontal) != 0)
            {
                effects |= SpriteEffects.FlipHorizontally;
            }

            if ((flags & FlipVertical) != 0)
            {
                effects |= SpriteEffects.FlipVertically;
            }

            var elementTint = (flags & Alpha50) != 0 ? tint * 0.5f
                : (flags & Alpha75) != 0 ? tint * 0.25f
                : tint;
            if (top == int.MinValue && bottom == int.MaxValue)
            {
                _sprites.Draw(batch, element.Sprite, new Vector2(left, y + element.Y), elementTint, effects);
            }
            else
            {
                _sprites.DrawRows(batch, element.Sprite, new Vector2(left, y + element.Y), elementTint, effects, top, bottom);
            }
        }
    }

    public void Dispose() => _sprites.Dispose();

    private static bool IsVisible(AnimationElement element, ReadOnlySpan<byte> layers)
    {
        if (element.LayerId == 0)
        {
            return true;
        }

        var chosen = element.Layer < layers.Length ? layers[element.Layer] : 0;
        return chosen == element.LayerId || (element.Layer == HeadLayer && chosen - 4 == element.LayerId);
    }
}
