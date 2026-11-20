using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenWards.Client.Rendering;

/// <summary>
/// The window sizes the graphics settings offer: the screen modes the monitor reports, as CorsixTH's own list is a fixed
/// one for a screen of ordinary size (graphics_settings.lua) and offered sizes this screen cannot show.
/// </summary>
internal static partial class DisplayModes
{
    /// <summary>
    /// The monitor's modes, in real pixels, that a window can take: no larger than the desktop's free area less the
    /// window's frame, nor smaller than the game's smallest window at the display scale. The fallback list when the
    /// monitor reports none.
    /// </summary>
    public static IReadOnlyList<(int Width, int Height)> Available()
    {
        var adapter = GraphicsAdapter.DefaultAdapter;
        List<(int Width, int Height)> reported;
        try
        {
            reported = [.. adapter.SupportedDisplayModes.Select(mode => (mode.Width, mode.Height))];
        }
        catch (Exception e) when (e is InvalidOperationException or NullReferenceException)
        {
            reported = [];
        }

        var (work, frame) = WorkArea();
        return WindowSizes(reported, (work.Width - frame.Width, work.Height - frame.Height), ScreenCanvas.SystemDpiScale());
    }

    /// <summary>
    /// The desktop's area free of the taskbar, in real pixels, and how much a window's frame and title bar add to its
    /// inside (<see cref="Rectangle.X"/> and <see cref="Rectangle.Y"/> the frame's left and top, negative). Off Windows,
    /// the whole desktop and no frame.
    /// </summary>
    public static (Rectangle Work, Rectangle Frame) WorkArea()
    {
        var desktop = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        var whole = new Rectangle(0, 0, desktop.Width, desktop.Height);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
        {
            return (whole, Rectangle.Empty);
        }

        try
        {
            var area = default(Rect);
            var work = SystemParametersInfoW(SpiGetWorkArea, 0, ref area, 0) && area.Right > area.Left && area.Bottom > area.Top
                ? new Rectangle(area.Left, area.Top, area.Right - area.Left, area.Bottom - area.Top)
                : whole;
            var frame = default(Rect);
            return AdjustWindowRectExForDpi(ref frame, OverlappedWindow, false, 0, (uint)Math.Round(ScreenCanvas.SystemDpiScale() * 96))
                ? (work, new Rectangle(frame.Left, frame.Top, frame.Right - frame.Left, frame.Bottom - frame.Top))
                : (work, Rectangle.Empty);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return (whole, Rectangle.Empty);
        }
    }

    /// <summary>
    /// Where a window's inside of <paramref name="client"/> pixels goes for the whole window, frame and all, to stand in
    /// the middle of <paramref name="work"/>; its title bar is kept on the screen.
    /// </summary>
    internal static Point CentredPosition(Point client, Rectangle work, Rectangle frame)
    {
        var x = work.X + ((work.Width - client.X - frame.Width) / 2) - frame.X;
        var y = work.Y + ((work.Height - client.Y - frame.Height) / 2) - frame.Y;
        return new Point(Math.Max(work.X - frame.X, x), Math.Max(work.Y - frame.Y, y));
    }

    private const uint SpiGetWorkArea = 0x0030;
    private const uint OverlappedWindow = 0x00CF0000;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoW(uint action, uint parameter, ref Rect value, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustWindowRectExForDpi(ref Rect rect, uint style, [MarshalAs(UnmanagedType.Bool)] bool menu, uint extendedStyle, uint dpi);

    /// <summary>
    /// The sizes from <paramref name="modes"/> no larger than <paramref name="desktop"/> and no smaller than the game's
    /// smallest window (in points, so in pixels times <paramref name="density"/>), each once, smallest first;
    /// <paramref name="fallback"/> (likewise trimmed) when none are left.
    /// </summary>
    internal static List<(int Width, int Height)> WindowSizes(IEnumerable<(int Width, int Height)> modes, (int Width, int Height) desktop, double density = 1, IEnumerable<(int Width, int Height)>? fallback = null)
    {
        var smallest = ToPixels((OpenWardsGame.MinWidth, OpenWardsGame.MinHeight), density);
        List<(int Width, int Height)> Fit(IEnumerable<(int Width, int Height)> sizes) =>
        [
            .. sizes
                .Where(size => size.Width >= smallest.Width && size.Height >= smallest.Height)
                .Where(size => desktop.Width <= 0 || desktop.Height <= 0 || (size.Width <= desktop.Width && size.Height <= desktop.Height))
                .Distinct()
                .OrderBy(size => size.Width * size.Height)
                .ThenBy(size => size.Width),
        ];

        var fitting = Fit(modes);
        return fitting.Count > 0 ? fitting : Fit(fallback ?? Ui.Windows.SettingsDialog.WindowSizes);
    }

    /// <summary>A size in real pixels as the system's points the settings keep it in (<c>width</c>, <c>height</c>).</summary>
    public static (int Width, int Height) ToPoints((int Width, int Height) pixels, double density) =>
        ((int)Math.Round(pixels.Width / density), (int)Math.Round(pixels.Height / density));

    /// <summary>A size in the system's points as real pixels.</summary>
    public static (int Width, int Height) ToPixels((int Width, int Height) points, double density) =>
        ((int)Math.Round(points.Width * density), (int)Math.Round(points.Height * density));
}
