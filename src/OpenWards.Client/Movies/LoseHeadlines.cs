// The losing movies' newspaper headlines, ported from CorsixTH CorsixTH/Lua/movie_player.lua (loseMovieOverlay,
// MoviePlayer:init's lose fonts) and CorsixTH/Lua/languages/original_strings.lua (newspaper).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenWards.Client.Ui;
using OpenWards.Content;

namespace OpenWards.Client.Movies;

/// <summary>
/// The headline printed on the newspaper at the end of a losing movie: one of the movie's headlines (section 38 of the
/// original's strings), in Font39V with CorsixTH's <c>lose.pl8</c> (Font40V when too wide), across the paper at
/// double the movie's size.
/// </summary>
internal static class LoseHeadlines
{
    /// <summary>The movies are drawn at 640 by 480 when the headline goes on, twice their own size.</summary>
    public const int Width = 640;

    public const int Height = 480;

    /// <summary>The headlines of each losing movie, by the string's number in section 38 (<c>_S.newspaper</c>).</summary>
    private static readonly int[][] Strings =
    [
        [1, 2, 3, 4, 5],
        [7, 8, 9, 10, 11, 12],
        [14, 15, 16, 17],
        [19, 20, 21, 22],
        [24, 25, 26, 27, 28, 29],
        [36, 37, 38, 39, 40],
    ];

    /// <summary>How long before each movie's end the headline appears (<c>start_movie_pts</c>, found by trying).</summary>
    private static readonly int[] MillisecondsBeforeEnd = [1760, 860, 1600, 1000, 860, 780];

    /// <summary>The headlines of losing movie <paramref name="number"/> (from 1); none without the original's strings.</summary>
    public static IReadOnlyList<string> For(int number, OriginalTexts? texts)
    {
        if (texts is null || number < 1 || number > Strings.Length)
        {
            return [];
        }

        return [.. Strings[number - 1].Select(index => texts.String(38, index)).OfType<string>().Where(text => text.Length > 0)];
    }

    /// <summary>How long before the end of movie <paramref name="number"/> the headline shows.</summary>
    public static TimeSpan Before(int number) =>
        TimeSpan.FromMilliseconds(number >= 1 && number <= MillisecondsBeforeEnd.Length ? MillisecondsBeforeEnd[number - 1] : 1000);

    /// <summary>
    /// The headline drawn on a clear 640 by 480 picture, centred, its top at 132 (138 in the narrow font, used when the
    /// standard one is wider than 590 pixels; a TrueType font is centred in the 52 pixels of the headline's area). A
    /// headline still wider than the paper's 618 pixels, as a translation's in a TrueType font can be, is drawn on a
    /// larger picture that is shrunk with the movie, so that it fits.
    /// </summary>
    public static Texture2D? Render(GraphicsDevice device, OriginalGraphics? graphics, string headline)
    {
        if (graphics?.Font("FONT39V", OriginalGraphics.LosePalette) is not { } font)
        {
            return null;
        }

        var narrowTop = false;
        var (width, height) = font.Measure(headline);
        var trueType = font.IsTrueTypeFor(headline);
        if (!trueType && width > 590 && graphics.Font("FONT40V", OriginalGraphics.LosePalette) is { } narrow)
        {
            font = narrow;
            narrowTop = true;
            (width, height) = font.Measure(headline);
        }

        var scale = Math.Max(1.0, (width + 8) / 618.0);
        var top = trueType ? 132 + ((52 - (height / scale)) / 2) : narrowTop ? 138 : 132;
        var canvasWidth = (int)Math.Ceiling(Width * scale);
        var canvasHeight = (int)Math.Ceiling(Height * scale);
        var target = new RenderTarget2D(device, canvasWidth, canvasHeight);
        var previous = device.GetRenderTargets();
        device.SetRenderTarget(target);
        device.Clear(Color.Transparent);
        using (var batch = new SpriteBatch(device))
        {
            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
            font.Draw(batch, headline, new Vector2((canvasWidth - width) / 2, (int)(top * scale)), tint: trueType ? Color.Black : null);
            batch.End();
        }

        device.SetRenderTargets(previous);
        return target;
    }
}
