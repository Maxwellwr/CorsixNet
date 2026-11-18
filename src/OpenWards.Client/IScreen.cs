using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenWards.Client;

/// <summary>A full-window mode of the client, such as the map view or the sprite viewer.</summary>
internal interface IScreen : IDisposable
{
    bool ExitRequested { get; }

    /// <summary>Advances the screen.</summary>
    /// <param name="gameTime">Time since the last update.</param>
    /// <param name="hasFocus">Whether the window has focus; input is ignored without it, but games keep running.</param>
    void Update(GameTime gameTime, bool hasFocus);

    void Draw(SpriteBatch batch);

    /// <summary>Whether the screen draws its own mouse cursor, so the system's is hidden.</summary>
    bool DrawsCursor => false;

    /// <summary>Whether the cursor is to be kept inside the window while it has focus (<c>App:setCaptureMouse</c>).</summary>
    bool CapturesMouse => false;

    /// <summary>The file a screenshot of the frame just drawn should be saved to, once asked for (<c>UI:makeScreenshot</c>), else null.</summary>
    string? TakeScreenshotRequest() => null;

    /// <summary>The screenshot asked for has been saved (<c>UI:makeScreenshot</c> then plays SNAPSHOT.WAV).</summary>
    void ScreenshotSaved()
    {
    }

    /// <summary>A character typed, as the keyboard layout gives it (MonoGame's <c>GameWindow.TextInput</c>).</summary>
    void TextInput(char character)
    {
    }

    /// <summary>Whether fullscreen was asked to be switched on or off since the last call (<c>UI:fullscreenHotkey</c>).</summary>
    bool TakeFullscreenToggleRequest() => false;
}
