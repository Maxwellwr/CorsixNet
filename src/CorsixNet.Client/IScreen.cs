using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client;

/// <summary>A full-window mode of the client, such as the map view or the sprite viewer.</summary>
internal interface IScreen : IDisposable
{
    bool ExitRequested { get; }

    void Update(GameTime gameTime);

    void Draw(SpriteBatch batch);
}
