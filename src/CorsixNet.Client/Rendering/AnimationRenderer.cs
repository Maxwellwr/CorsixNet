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

    /// <summary>Game ticks between the starts of the jellyitis wobble (<c>jelly_effect_period</c>).</summary>
    public const int JellyPeriod = 540;

    /// <summary>Game ticks the wobble lasts (<c>jelly_effect_duration</c>).</summary>
    public const int JellyDuration = 90;

    /// <summary>
    /// Draws a frame with the jellyitis wobble (CorsixTH's <c>animation_effect::jelly</c> in <c>sprite_sheet::draw_sprite</c>):
    /// for 90 of every 540 ticks each patient sprite is drawn a few rows at a time, shifted sideways along a sine wave that
    /// moves down the sprite, ramping up to 2 pixels and back. Sprites outside the patient's layers (layer 0, id 0) stay
    /// still.
    /// </summary>
    public void DrawFrameJelly(SpriteBatch batch, int frame, int x, int y, ReadOnlySpan<byte> layers, bool flipHorizontally, long effectTicks)
    {
        var jellyTick = (int)(effectTicks % JellyPeriod);
        if (jellyTick >= JellyDuration)
        {
            DrawFrame(batch, frame, x, y, layers, flipHorizontally);
            return;
        }

        var scale = Math.Min(jellyTick, Math.Min(JellyDuration - jellyTick, 2));
        foreach (var element in Animations.Elements(frame))
        {
            if (!IsVisible(element, layers) || _sprites.WidthOf(element.Sprite) == 0)
            {
                continue;
            }

            var flags = flipHorizontally ? (byte)(element.Flags ^ FlipHorizontal) : element.Flags;
            if ((flags & (Alpha50 | Alpha75)) == (Alpha50 | Alpha75))
            {
                continue;
            }

            var width = _sprites.WidthOf(element.Sprite);
            var height = _sprites.HeightOf(element.Sprite);
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

            var tint = (flags & Alpha50) != 0 ? Color.White * 0.5f : (flags & Alpha75) != 0 ? Color.White * 0.25f : Color.White;
            var top = y + element.Y;
            if (element.Layer == 0 && element.LayerId == 0)
            {
                _sprites.Draw(batch, element.Sprite, new Vector2(left, top), tint, effects);
                continue;
            }

            // Rows with the same (rounded) offset are drawn together.
            var y1 = 0;
            var offset = 0;
            for (var y2 = 0; y2 <= height; y2++)
            {
                var next = (int)Math.Round(Math.Sin(((y2 / 16.0) + ((effectTicks % 50) / 50.0)) * 2 * Math.PI) * scale);
                if (next != offset || y2 == height)
                {
                    if (y2 > y1)
                    {
                        _sprites.DrawRows(batch, element.Sprite, new Vector2(left + offset, top), tint, effects, top + y1, top + y2);
                    }

                    y1 = y2;
                    offset = next;
                }
            }
        }
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
