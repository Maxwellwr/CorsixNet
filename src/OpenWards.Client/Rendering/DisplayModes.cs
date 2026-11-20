using Microsoft.Xna.Framework.Graphics;

namespace OpenWards.Client.Rendering;

/// <summary>
/// The window sizes the graphics settings offer: the screen modes the monitor reports, as CorsixTH's own list is a fixed
/// one for a screen of ordinary size (graphics_settings.lua) and offered sizes this screen cannot show.
/// </summary>
internal static class DisplayModes
{
    /// <summary>The monitor's modes, in real pixels, that fit on the desktop; the fallback list when it reports none.</summary>
    public static IReadOnlyList<(int Width, int Height)> Available()
    {
        var adapter = GraphicsAdapter.DefaultAdapter;
        var desktop = adapter.CurrentDisplayMode;
        List<(int Width, int Height)> reported;
        try
        {
            reported = [.. adapter.SupportedDisplayModes.Select(mode => (mode.Width, mode.Height))];
        }
        catch (Exception e) when (e is InvalidOperationException or NullReferenceException)
        {
            reported = [];
        }

        return WindowSizes(reported, (desktop.Width, desktop.Height));
    }

    /// <summary>
    /// The sizes from <paramref name="modes"/> no larger than <paramref name="desktop"/> and no smaller than the game's
    /// smallest window, each once, smallest first; <paramref name="fallback"/> (likewise trimmed) when none are left.
    /// </summary>
    internal static List<(int Width, int Height)> WindowSizes(IEnumerable<(int Width, int Height)> modes, (int Width, int Height) desktop, IEnumerable<(int Width, int Height)>? fallback = null)
    {
        List<(int Width, int Height)> Fit(IEnumerable<(int Width, int Height)> sizes) =>
        [
            .. sizes
                .Where(size => size.Width >= OpenWardsGame.MinWidth && size.Height >= OpenWardsGame.MinHeight)
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
