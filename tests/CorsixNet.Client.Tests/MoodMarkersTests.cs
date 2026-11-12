using CorsixNet.Client.Maps;

namespace CorsixNet.Client.Tests;

/// <summary>Mood icon markers (CorsixTH's graphics.lua setMarkerRaw and th_gfx.cpp get_primary_marker).</summary>
public class MoodMarkersTests
{
    [Fact]
    public void KeyframesAreInterpolatedAndTheLastOneHeld()
    {
        // bed.lua: {1, -1} on frame 0, {0.4, -1} from frame 4; 16 down to the tile's centre.
        Assert.Equal((64, 16), MoodMarkers.Offset(patient: true, 4686, frame: 0, length: 20, flip: false));
        Assert.Equal((54, 11), MoodMarkers.Offset(patient: true, 4686, frame: 2, length: 20, flip: false));
        Assert.Equal((22, -5), MoodMarkers.Offset(patient: true, 4686, frame: 19, length: 20, flip: false));
    }

    [Fact]
    public void AMirroredAnimationMirrorsItsMarker()
    {
        // comfortable_chair.lua's psychiatrist sitting: {34, -12} pixels.
        Assert.Equal((34, 4), MoodMarkers.Offset(patient: false, 2544, frame: 0, length: 1, flip: false));
        Assert.Equal((-34, 4), MoodMarkers.Offset(patient: false, 2544, frame: 0, length: 1, flip: true));
    }

    [Fact]
    public void WithoutAMarkerTheIconHangsOverTheTileCentre()
    {
        Assert.Equal((0, 16), MoodMarkers.Offset(patient: true, 678, frame: 0, length: 2, flip: false));

        // Patient and staff markers are apart: the chair's marker is the psychiatrist's.
        Assert.Equal((0, 16), MoodMarkers.Offset(patient: true, 2544, frame: 0, length: 1, flip: false));
    }
}
