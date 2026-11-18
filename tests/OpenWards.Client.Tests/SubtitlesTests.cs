using OpenWards.Client.Ui;

namespace OpenWards.Client.Tests;

/// <summary>The announcer's subtitles (CorsixTH's dialogs/subtitles.lua).</summary>
public sealed class SubtitlesTests
{
    [Fact]
    public void WithoutATranslationNothingShows()
    {
        var subtitles = new Subtitles();
        subtitles.Queue(null, "EPID001.wav", 0);
        Assert.Empty(subtitles.Shown);
        Assert.Null(Subtitles.TextOf(null, "EPID001.wav"));
    }
}
