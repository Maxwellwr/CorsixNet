using Microsoft.Xna.Framework;

namespace CorsixNet.Client.Maps;

/// <summary>
/// View onto the isometric map. World coordinates are map pixels at 1:1 scale:
/// tile (x, y) has its top corner at (32·(x − y), 16·(x + y)).
/// </summary>
internal sealed class Camera
{
    public const float MinZoom = 0.5f;
    public const float MaxZoom = 4f;

    /// <summary>World position shown at the centre of the screen.</summary>
    public Vector2 Center { get; set; }

    public float Zoom { get; private set; } = 1f;

    /// <summary>Top-left world position of a view of the given size, rounded to whole world pixels.</summary>
    public Point TopLeft(Point viewSize)
    {
        return new Point(
            (int)MathF.Floor(Center.X - (viewSize.X / (2 * Zoom))),
            (int)MathF.Floor(Center.Y - (viewSize.Y / (2 * Zoom))));
    }

    /// <summary>Size in world pixels of the area visible in a view of the given size.</summary>
    public Point WorldSize(Point viewSize)
    {
        return new Point((int)MathF.Ceiling(viewSize.X / Zoom) + 1, (int)MathF.Ceiling(viewSize.Y / Zoom) + 1);
    }

    public Vector2 ScreenToWorld(Point screen, Point viewSize)
    {
        var topLeft = TopLeft(viewSize);
        return new Vector2(topLeft.X + (screen.X / Zoom), topLeft.Y + (screen.Y / Zoom));
    }

    /// <summary>Changes the zoom while keeping the world point under <paramref name="anchor"/> in place.</summary>
    public void ZoomAt(float zoom, Point anchor, Point viewSize)
    {
        var before = ScreenToWorld(anchor, viewSize);
        Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        var after = ScreenToWorld(anchor, viewSize);
        Center += before - after;
    }

    /// <summary>World position of the centre of tile (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static Vector2 TileCenter(int x, int y) => new(32 * (x - y), (16 * (x + y)) + 16);

    /// <summary>Where in its tile a world position lies, along x and y, each from 0 to 255.</summary>
    public static (int X, int Y) WorldToTileFraction(Vector2 world)
    {
        var u = world.Y / 16f;
        var v = world.X / 32f;
        var x = (u + v) / 2;
        var y = (u - v) / 2;
        return ((int)((x - MathF.Floor(x)) * 256), (int)((y - MathF.Floor(y)) * 256));
    }

    /// <summary>The tile containing a world position, which may lie outside the map.</summary>
    public static Point WorldToTile(Vector2 world)
    {
        var u = world.Y / 16f;
        var v = world.X / 32f;
        return new Point((int)MathF.Floor((u + v) / 2), (int)MathF.Floor((u - v) / 2));
    }
}
