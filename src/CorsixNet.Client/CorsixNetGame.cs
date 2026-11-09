using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client;

/// <summary>Client shell hosting one full-window screen.</summary>
internal sealed class CorsixNetGame : Game
{
    private readonly Func<GraphicsDevice, IScreen> _createScreen;
    private SpriteBatch? _batch;
    private IScreen? _screen;

    /// <summary>The smallest window the settings may ask for (CorsixTH's 640 by 480).</summary>
    private const int MinWidth = 640;
    private const int MinHeight = 480;

    public CorsixNetGame(string title, Func<GraphicsDevice, IScreen> createScreen, int width = 1280, int height = 800, bool fullscreen = false)
    {
        _createScreen = createScreen;
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = Math.Max(MinWidth, width),
            PreferredBackBufferHeight = Math.Max(MinHeight, height),
            IsFullScreen = fullscreen,
        };
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.Title = title;
    }

    protected override void LoadContent()
    {
        _batch = new SpriteBatch(GraphicsDevice);
        _screen = _createScreen(GraphicsDevice);
    }

    protected override void UnloadContent()
    {
        _screen?.Dispose();
        _batch?.Dispose();
    }

    protected override void Update(GameTime gameTime)
    {
        // Keep updating without focus: a network game must keep up with the server.
        _screen?.Update(gameTime, IsActive);

        if (_screen?.ExitRequested == true)
        {
            Exit();
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_screen is not null && _batch is not null)
        {
            _screen.Draw(_batch);
        }

        base.Draw(gameTime);
    }
}
