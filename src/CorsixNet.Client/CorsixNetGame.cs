using CorsixNet.Formats;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client;

/// <summary>
/// Client shell. For now it only opens the window; rendering arrives with the format readers.
/// </summary>
internal sealed class CorsixNetGame : Game
{
    private readonly ThemeHospitalInstall _install;

    public CorsixNetGame(ThemeHospitalInstall install)
    {
        _install = install;
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
        };
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.Title = $"CorsixNet — {_install.RootPath}";
    }

    protected override void Update(GameTime gameTime)
    {
        if (Keyboard.GetState().IsKeyDown(Keys.Escape))
        {
            Exit();
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);
        base.Draw(gameTime);
    }
}
