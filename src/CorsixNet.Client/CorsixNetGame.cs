using CorsixNet.Client.SpriteViewer;
using CorsixNet.Formats;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client;

/// <summary>
/// Client shell. Until the game itself exists, it hosts the sprite viewer.
/// </summary>
internal sealed class CorsixNetGame : Game
{
    private readonly ThemeHospitalInstall _install;
    private readonly string? _initialSheet;
    private SpriteBatch? _batch;
    private SpriteViewerScreen? _viewer;

    public CorsixNetGame(ThemeHospitalInstall install, string? initialSheet = null)
    {
        _install = install;
        _initialSheet = initialSheet;
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 800,
        };
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.Title = $"CorsixNet - {_install.RootPath}";
    }

    protected override void LoadContent()
    {
        _batch = new SpriteBatch(GraphicsDevice);
        _viewer = new SpriteViewerScreen(_install, GraphicsDevice, _initialSheet);
    }

    protected override void UnloadContent()
    {
        _viewer?.Dispose();
        _batch?.Dispose();
    }

    protected override void Update(GameTime gameTime)
    {
        if (IsActive)
        {
            _viewer?.Update();
        }

        if (_viewer?.ExitRequested == true)
        {
            Exit();
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_viewer is not null && _batch is not null)
        {
            _viewer.Draw(_batch);
        }

        base.Draw(gameTime);
    }
}
