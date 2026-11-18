using Microsoft.Xna.Framework;
using OpenWards.Client.Movies;

namespace OpenWards.Client.Tests;

/// <summary>The movie player's sizing (CorsixTH's movie_player.lua calculateSize).</summary>
public class MoviePlayerTests
{
    [Fact]
    public void AMovieFillsTheScreenAtItsAspectRatioAnd320By200IsShownAs4By3()
    {
        // 320 by 240 on a 4:3 screen fills it.
        Assert.Equal(new Rectangle(0, 0, 640, 480), MoviePlayer.Fit(320, 240, new Rectangle(0, 0, 640, 480)));

        // On a wider screen it is as high as the screen, in the middle.
        Assert.Equal(new Rectangle(107, 0, 1066, 800), MoviePlayer.Fit(320, 240, new Rectangle(0, 0, 1280, 800)));

        // 320 by 200 was meant to be stretched to 4:3.
        Assert.Equal(new Rectangle(107, 0, 1066, 800), MoviePlayer.Fit(320, 200, new Rectangle(0, 0, 1280, 800)));

        // On a taller screen it is as wide as the screen.
        Assert.Equal(new Rectangle(0, 100, 600, 450), MoviePlayer.Fit(640, 480, new Rectangle(0, 0, 600, 650)));
    }
}

/// <summary>The losing movies' headlines (CorsixTH's loseMovieOverlay).</summary>
public class LoseHeadlinesTests
{
    [Fact]
    public void EachLosingMovieShowsItsHeadlineSoLongBeforeItsEnd()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(1760), LoseHeadlines.Before(1));
        Assert.Equal(TimeSpan.FromMilliseconds(780), LoseHeadlines.Before(6));
        Assert.Empty(LoseHeadlines.For(1, null));
        Assert.Empty(LoseHeadlines.For(7, null));
    }
}
