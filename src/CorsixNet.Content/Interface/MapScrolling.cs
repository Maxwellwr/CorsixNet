// Scrolling and zooming the map view, ported from CorsixTH CorsixTH/Lua/game_ui.lua (GameUI:updateKeyScroll,
// onMouseMove, onMouseWheel, onTick) and world.lua (World:adjustZoom).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

namespace CorsixNet.Content.Interface;

/// <summary>The player's scrolling settings, as in CorsixTH's <c>config.txt</c>.</summary>
/// <param name="ScrollSpeed"><c>scroll_speed</c>: how fast the arrow keys and the screen edges scroll.</param>
/// <param name="ShiftScrollSpeed"><c>shift_scroll_speed</c>: the same with Shift held.</param>
/// <param name="ZoomSpeed"><c>zoom_speed</c>: how far a turn of the mouse wheel zooms.</param>
/// <param name="Momentum"><c>scrolling_momentum</c>: how much of a zoom (or of a drag's remainder) carries on each tick.</param>
/// <param name="PreventEdgeScrolling"><c>prevent_edge_scrolling</c>: the mouse at the screen's edge does not scroll.</param>
public sealed record ScrollSettings(
    double ScrollSpeed = 2,
    double ShiftScrollSpeed = 4,
    double ZoomSpeed = 80,
    double Momentum = 0.8,
    bool PreventEdgeScrolling = false);

/// <summary>What the player does during one tick of the interface.</summary>
/// <param name="KeyX">The arrow keys held along x: −1, 0 or 1.</param>
/// <param name="KeyY">The arrow keys held along y: −1, 0 or 1.</param>
/// <param name="Shift">Whether Shift is held, for the faster speed.</param>
/// <param name="EdgeX">The pull of the screen's edge along x, from <see cref="MapScrolling.EdgePull"/>.</param>
/// <param name="EdgeY">The pull of the screen's edge along y.</param>
/// <param name="Dragging">Whether the map is being dragged with the mouse.</param>
public readonly record struct ScrollInput(int KeyX, int KeyY, bool Shift, int EdgeX, int EdgeY, bool Dragging);

/// <summary>What one tick does to the view: a scroll in map pixels, and a zoom delta for <see cref="MapScrolling.Zoomed"/>.</summary>
public readonly record struct ScrollStep(double X, double Y, double Zoom);

/// <summary>
/// CorsixTH's map scrolling. The interface ticks every 18 ms. Arrow keys scroll 10 map pixels a tick and the screen's
/// edges 7, both times <c>scroll_speed</c> / 4 (<c>shift_scroll_speed</c> with Shift) and times a factor that grows
/// from 1 to 2 by 0.02 a tick while scrolling goes on. Dragging moves the map with the mouse; the wheel adds momentum
/// to the zoom, which runs down by <c>scrolling_momentum</c> each tick.
/// </summary>
public sealed class MapScrolling(ScrollSettings settings)
{
    /// <summary>The interface's tick (<c>usertick_period_ms</c> in CorsixTH's <c>lua_sdl.h</c>).</summary>
    public const int TickMilliseconds = 18;

    /// <summary>Map pixels a tick that the arrow keys scroll before the speed settings (<c>key_scroll_speed</c>).</summary>
    private const double KeyScrollSpeed = 10;

    /// <summary>Map pixels a tick that the screen's edge scrolls before the speed settings (<c>scroll_power</c>).</summary>
    private const int EdgeScrollPower = 7;

    /// <summary>Momentum below this stops.</summary>
    private const double MomentumThreshold = 0.2;

    /// <summary>The wheel adds no more zoom momentum beyond this.</summary>
    private const double MaxZoomMomentum = 12;

    private double _scrollMultiplier = 1;
    private double _momentumX;
    private double _momentumY;
    private double _momentumZ;

    public ScrollSettings Settings { get; } = settings;

    /// <summary>
    /// The scroll power of the mouse at (<paramref name="x"/>, <paramref name="y"/>) in a window of the given size: 7
    /// towards each edge it is within 8 pixels of (1 pixel full screen), else 0; nothing with edge scrolling turned off
    /// or the mouse outside the window (<c>GameUI:onMouseMove</c>).
    /// </summary>
    public (int X, int Y) EdgePull(int x, int y, int width, int height, bool fullscreen)
    {
        var region = fullscreen ? 1 : 8;
        if (Settings.PreventEdgeScrolling || x < 0 || y < 0 || x >= width || y >= height)
        {
            return (0, 0);
        }

        var pullX = x < region ? -EdgeScrollPower : x >= width - region ? EdgeScrollPower : 0;
        var pullY = y < region ? -EdgeScrollPower : y >= height - region ? EdgeScrollPower : 0;
        return (pullX, pullY);
    }

    /// <summary>
    /// <c>GameUI:onMouseMove</c> while dragging: the mouse moved by (<paramref name="dx"/>, <paramref name="dy"/>)
    /// screen pixels; the map moves the other way by whole map pixels, the rest kept for later, and zooming stops.
    /// </summary>
    public (int X, int Y) Drag(double dx, double dy, double zoom)
    {
        _momentumX -= dx / zoom;
        _momentumY -= dy / zoom;
        var x = (int)Math.Floor(_momentumX + 0.5);
        var y = (int)Math.Floor(_momentumY + 0.5);
        _momentumZ = 0;
        _momentumX -= x;
        _momentumY -= y;
        return (x, y);
    }

    /// <summary><c>GameUI:onMouseWheel</c>: a turn of the wheel, up positive, adds to the zoom's momentum.</summary>
    public void Wheel(double clicks)
    {
        if (Math.Abs(_momentumZ) < MaxZoomMomentum)
        {
            _momentumZ += clicks;
        }
    }

    /// <summary>The scrolling part of <c>GameUI:onTick</c>.</summary>
    public ScrollStep Tick(ScrollInput input)
    {
        double x = 0, y = 0, zoom = 0;
        if (!input.Dragging)
        {
            if (Math.Abs(_momentumX) < MomentumThreshold && Math.Abs(_momentumY) < MomentumThreshold)
            {
                _momentumX = 0;
                _momentumY = 0;
            }
            else
            {
                _momentumX *= Settings.Momentum;
                _momentumY *= Settings.Momentum;
                x += _momentumX;
                y += _momentumY;
            }

            if (Math.Abs(_momentumZ) > MomentumThreshold)
            {
                zoom = _momentumZ;
            }

            _momentumZ *= Settings.Momentum;
        }

        // GameUI:updateKeyScroll: the keys' direction, at the key scroll speed.
        double keyX = 0, keyY = 0;
        if (input.KeyX != 0 || input.KeyY != 0)
        {
            var length = Math.Sqrt((input.KeyX * input.KeyX) + (input.KeyY * input.KeyY));
            keyX = input.KeyX / length * KeyScrollSpeed;
            keyY = input.KeyY / length * KeyScrollSpeed;
        }

        var keys = input.KeyX != 0 || input.KeyY != 0;
        var edge = input.EdgeX != 0 || input.EdgeY != 0;
        if (keys || edge)
        {
            _scrollMultiplier = Math.Min(_scrollMultiplier + 0.02, 2);

            // While dragging, the edge pushes the other way, which feels more natural.
            double dx = input.Dragging ? -input.EdgeX : input.EdgeX;
            double dy = input.Dragging ? -input.EdgeY : input.EdgeY;
            dx += keyX;
            dy += keyY;
            var multiplier = _scrollMultiplier * (input.Shift ? Settings.ShiftScrollSpeed : Settings.ScrollSpeed) * 0.25;
            x += dx * multiplier;
            y += dy * multiplier;
        }
        else
        {
            _scrollMultiplier = 1;
        }

        return new ScrollStep(x, y, zoom);
    }

    /// <summary>
    /// <c>World:adjustZoom</c>: the zoom after a zoom delta, on a screen <paramref name="screenWidth"/> pixels wide. The
    /// visible width shrinks by delta × <c>zoom_speed</c> × a bell curve of the zoom (at least 0.05), so zooming is
    /// slower far from 1; null when fewer than 200 map pixels would be visible.
    /// </summary>
    public double? Zoomed(double zoom, double delta, int screenWidth)
    {
        var width = screenWidth / zoom;
        var modifier = Math.Exp(-((zoom - 1) * (zoom - 1)) / 2) / Math.Sqrt(2 * Math.PI);
        if (modifier is < 0.05 or > 1)
        {
            modifier = 0.05;
        }

        width -= delta * Settings.ZoomSpeed * modifier;
        if (width < 200)
        {
            return null;
        }

        var factor = screenWidth / width;
        return Math.Abs(factor - 1) < 0.001 ? 1 : factor;
    }
}
