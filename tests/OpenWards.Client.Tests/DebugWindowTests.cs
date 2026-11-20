using OpenWards.Client.Maps;
using Xunit;

namespace OpenWards.Client.Tests;

public sealed class DebugWindowTests
{
    [Fact]
    public void Segments_share_a_line_while_they_fit()
    {
        var lines = MapViewScreen.PackSegments(["aa", "bb", "cc"], 10, text => text.Length);

        Assert.Equal(["aa   bb", "cc"], lines);
    }

    [Fact]
    public void A_long_segment_is_wrapped_at_its_spaces()
    {
        var lines = MapViewScreen.PackSegments(["x", "one two three"], 9, text => text.Length);

        Assert.Equal(["x", "one two", "three"], lines);
    }

    [Fact]
    public void No_segments_make_no_lines()
    {
        Assert.Empty(MapViewScreen.PackSegments([], 10, text => text.Length));
    }
}
