using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client;

/// <summary>A full-window mode of the client, such as the map view or the sprite viewer.</summary>
internal interface IScreen : IDisposable
{
    bool ExitRequested { get; }

    /// <summary>Advances the screen.</summary>
    /// <param name="gameTime">Time since the last update.</param>
    /// <param name="hasFocus">Whether the window has focus; input is ignored without it, but games keep running.</param>
    void Update(GameTime gameTime, bool hasFocus);

    void Draw(SpriteBatch batch);
}
