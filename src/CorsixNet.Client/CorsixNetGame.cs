using CorsixNet.Client.Input;
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
        var script = InputScript.Current;
        script?.Advance(gameTime.ElapsedGameTime.TotalMilliseconds);

        // Keep updating without focus: a network game must keep up with the server. A script's input counts as focused.
        _screen?.Update(gameTime, IsActive || script is not null);
        IsMouseVisible = _screen?.DrawsCursor != true;

        if (_screen?.ExitRequested == true || script?.QuitRequested == true)
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
            if (InputScript.Current is { PendingShot: { } shot } script)
            {
                SaveScreenshot(shot);
                script.ShotTaken();
            }
        }

        base.Draw(gameTime);
    }

    /// <summary>Saves what has just been drawn to the back buffer as a PNG file.</summary>
    private void SaveScreenshot(string path)
    {
        var width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var height = GraphicsDevice.PresentationParameters.BackBufferHeight;
        var pixels = new Color[width * height];
        GraphicsDevice.GetBackBufferData(pixels);
        for (var i = 0; i < pixels.Length; i++)
        {
            // The map is drawn opaque without caring about alpha; a PNG would show it see-through.
            pixels[i].A = 255;
        }

        using var texture = new Texture2D(GraphicsDevice, width, height);
        texture.SetData(pixels);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        using var file = File.Create(path);
        texture.SaveAsPng(file, width, height);
        Console.WriteLine($"script: saved {path}");
    }
}
