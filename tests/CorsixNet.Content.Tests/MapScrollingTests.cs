using CorsixNet.Content.Interface;

namespace CorsixNet.Content.Tests;

public class MapScrollingTests
{
    [Fact]
    public void KeysScrollFasterTheLongerTheyAreHeld()
    {
        var scrolling = new MapScrolling(new ScrollSettings());

        // 10 map pixels a tick, times 1.02 on the first tick, times scroll_speed 2 / 4.
        var first = scrolling.Tick(new ScrollInput(1, 0, false, 0, 0, false));
        Assert.Equal(5.1, first.X, 10);
        Assert.Equal(0, first.Y);

        for (var i = 0; i < 100; i++)
        {
            scrolling.Tick(new ScrollInput(1, 0, false, 0, 0, false));
        }

        // At most twice as fast; diagonally the same speed; with Shift, shift_scroll_speed 4 / 4.
        var diagonal = scrolling.Tick(new ScrollInput(1, 1, false, 0, 0, false));
        Assert.Equal(10 / Math.Sqrt(2), diagonal.X, 10);
        Assert.Equal(10 / Math.Sqrt(2), diagonal.Y, 10);
        Assert.Equal(20, scrolling.Tick(new ScrollInput(-1, 0, true, 0, 0, false)).X * -1, 10);

        // Letting go starts again from the bottom.
        Assert.Equal(default, scrolling.Tick(default));
        Assert.Equal(5.1, scrolling.Tick(new ScrollInput(0, 1, false, 0, 0, false)).Y, 10);
    }

    [Fact]
    public void TheScreenEdgesPullUnlessTurnedOff()
    {
        var scrolling = new MapScrolling(new ScrollSettings());

        Assert.Equal((-7, 0), scrolling.EdgePull(7, 300, 800, 600, fullscreen: false));
        Assert.Equal((0, 0), scrolling.EdgePull(8, 300, 800, 600, fullscreen: false));
        Assert.Equal((7, 7), scrolling.EdgePull(792, 599, 800, 600, fullscreen: false));
        Assert.Equal((0, 0), scrolling.EdgePull(7, 300, 800, 600, fullscreen: true));
        Assert.Equal((0, -7), scrolling.EdgePull(400, 0, 800, 600, fullscreen: true));
        Assert.Equal((0, 0), scrolling.EdgePull(-1, 300, 800, 600, fullscreen: false));
        Assert.Equal((0, 0), new MapScrolling(new ScrollSettings(PreventEdgeScrolling: true)).EdgePull(0, 0, 800, 600, false));

        Assert.Equal(-7 * 1.02 * 0.5, scrolling.Tick(new ScrollInput(0, 0, false, -7, 0, false)).X, 10);

        // While dragging the edge pushes the other way.
        Assert.Equal(7 * 1.04 * 0.5, scrolling.Tick(new ScrollInput(0, 0, false, -7, 0, true)).X, 10);
    }

    [Fact]
    public void DraggingMovesWholePixelsAndKeepsTheRest()
    {
        var scrolling = new MapScrolling(new ScrollSettings());

        Assert.Equal((-5, 2), scrolling.Drag(10, -3, 2));
        Assert.Equal((0, 0), scrolling.Drag(0.6, 0, 2)); // −0.3 kept, then −0.6 rounds to −1, leaving 0.4
        Assert.Equal((-1, 0), scrolling.Drag(0.6, 0, 2));

        // The rest left over runs down with the momentum once the button is let go.
        Assert.Equal(0.4 * 0.8, scrolling.Tick(default).X, 10);
    }

    [Fact]
    public void TheWheelZoomsWithMomentum()
    {
        var scrolling = new MapScrolling(new ScrollSettings());
        scrolling.Wheel(1);

        var deltas = new List<double>();
        for (var i = 0; i < 10; i++)
        {
            deltas.Add(scrolling.Tick(default).Zoom);
        }

        Assert.Equal([1, 0.8, 0.64, 0.512, 0.4096, 0.32768, 0.262144, 0.209715, 0, 0], deltas.Select(d => Math.Round(d, 6)));

        // A drag stops the zoom; the wheel adds no more than up to 12.
        scrolling.Wheel(1);
        scrolling.Drag(0, 0, 1);
        Assert.Equal(0, scrolling.Tick(default).Zoom);
        for (var i = 0; i < 20; i++)
        {
            scrolling.Wheel(5);
        }

        Assert.Equal(15, scrolling.Tick(default).Zoom);
    }

    [Fact]
    public void AZoomShrinksTheVisibleWidth()
    {
        var scrolling = new MapScrolling(new ScrollSettings());
        var modifier = 1 / Math.Sqrt(2 * Math.PI);

        Assert.Equal(1000 / (1000 - (80 * modifier)), scrolling.Zoomed(1, 1, 1000)!.Value, 10);
        Assert.Equal(1000 / (1000 + (80 * modifier)), scrolling.Zoomed(1, -1, 1000)!.Value, 10);

        // Far from 1 the bell curve is below 0.05 and 0.05 is used.
        Assert.Equal(1000 / ((1000 / 4.0) - (80 * 0.05)), scrolling.Zoomed(4, 1, 1000)!.Value, 10);

        // Fewer than 200 map pixels may not be shown.
        Assert.Null(scrolling.Zoomed(4.95, 1, 1000));
        Assert.Equal(1, scrolling.Zoomed(1, 0, 1000));
    }
}
