using CorsixNet.Client.Input;
using CorsixNet.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client;

/// <summary>Client shell hosting one full-window screen.</summary>
internal sealed class CorsixNetGame : Game
{
    private readonly Func<GraphicsDevice, IScreen> _createScreen;
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch? _batch;
    private IScreen? _screen;

    /// <summary>The smallest window the settings may ask for (CorsixTH's 640 by 480).</summary>
    private const int MinWidth = 640;
    private const int MinHeight = 480;

    /// <param name="width">The window's width in the system's points: pixels at 100 % display scale, more above.</param>
    /// <param name="height">The window's height, likewise.</param>
    public CorsixNetGame(string title, Func<GraphicsDevice, IScreen> createScreen, int width = 1280, int height = 800, bool fullscreen = false)
    {
        _createScreen = createScreen;

        // The game says it handles the screen's density itself (app.manifest), so Windows no longer stretches its
        // window smoothly; the window keeps the size it had on the screen, and ScreenCanvas scales the picture by whole
        // pixels instead.
        var density = ScreenCanvas.SystemDpiScale();
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = (int)Math.Round(Math.Max(MinWidth, width) * density),
            PreferredBackBufferHeight = (int)Math.Round(Math.Max(MinHeight, height) * density),
            IsFullScreen = fullscreen,
        };
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.Title = title;
        Window.TextInput += (_, e) => _screen?.TextInput(e.Character);
        InputScript.TextTyped = character => _screen?.TextInput(character);
    }

    protected override void LoadContent()
    {
        _batch = new SpriteBatch(GraphicsDevice);
        _screen = _createScreen(GraphicsDevice);
    }

    protected override void UnloadContent()
    {
        MouseCapture.Set(Window.Handle, false);
        _screen?.Dispose();
        ScreenCanvas.Dispose();
        _batch?.Dispose();
    }

    protected override void Update(GameTime gameTime)
    {
        var script = InputScript.Current;
        script?.Advance(gameTime.ElapsedGameTime.TotalMilliseconds);
        ScreenCanvas.BeginFrame(GraphicsDevice);

        // Keep updating without focus: a network game must keep up with the server. A script's input counts as focused.
        _screen?.Update(gameTime, IsActive || script is not null);
        if (_screen?.TakeFullscreenToggleRequest() == true)
        {
            _graphics.ToggleFullScreen();
        }

        IsMouseVisible = _screen?.DrawsCursor != true;

        // App:setCaptureMouse: the cursor stays in the window while it has focus; let go without it, so that the
        // player can use other windows (CorsixTH frees it on quitting).
        MouseCapture.Set(Window.Handle, IsActive && script is null && _screen?.CapturesMouse == true);

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
            ScreenCanvas.BeginFrame(GraphicsDevice);
            ScreenCanvas.Bind(GraphicsDevice);
            _screen.Draw(_batch);
            ScreenCanvas.Present(GraphicsDevice, _batch);
            if (_screen.TakeScreenshotRequest() is { } screenshot)
            {
                try
                {
                    SaveScreenshot(screenshot);
                    _screen.ScreenshotSaved();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"Screenshot failed: {e.Message}");
                }
            }

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
        Console.WriteLine($"saved {path}");
    }
}
