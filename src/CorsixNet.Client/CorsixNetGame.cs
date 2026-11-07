using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client;

/// <summary>Client shell hosting one full-window screen.</summary>
internal sealed class CorsixNetGame : Game
{
    private readonly Func<GraphicsDevice, IScreen> _createScreen;
    private SpriteBatch? _batch;
    private IScreen? _screen;

    public CorsixNetGame(string title, Func<GraphicsDevice, IScreen> createScreen)
    {
        _createScreen = createScreen;
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 800,
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
        if (IsActive)
        {
            _screen?.Update(gameTime);
        }

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
