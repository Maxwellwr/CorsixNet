namespace CorsixNet.Client;

/// <summary>
/// When the cursor is kept in the window, the way current games do it rather than CorsixTH's grab whenever the window
/// has focus: only after a click inside the window since it got focus, so that focusing it by its title bar or Alt+Tab
/// does not trap the cursor; never without focus; and only while the screen wants it (in a game, not in a menu or a
/// full-screen dialog).
/// </summary>
internal sealed class MouseGrabPolicy
{
    private bool _armed;

    /// <summary>Whether the cursor is to be kept in the window this frame.</summary>
    /// <param name="active">Whether the window has focus.</param>
    /// <param name="clickedInside">Whether a mouse button is down with the cursor inside the window.</param>
    /// <param name="wanted">Whether the screen wants the cursor kept in (<see cref="IScreen.CapturesMouse"/>).</param>
    public bool Update(bool active, bool clickedInside, bool wanted)
    {
        if (!active)
        {
            _armed = false;
            return false;
        }

        _armed |= clickedInside;
        return _armed && wanted;
    }
}
