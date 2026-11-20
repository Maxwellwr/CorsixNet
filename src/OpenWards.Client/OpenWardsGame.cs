using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenWards.Client.Input;
using OpenWards.Client.Rendering;

namespace OpenWards.Client;

/// <summary>Client shell hosting one full-window screen.</summary>
internal sealed class OpenWardsGame : Game
{
    private readonly Func<GraphicsDevice, IScreen> _createScreen;
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch? _batch;
    private IScreen? _screen;
    private readonly MouseGrabPolicy _grab = new();

    /// <summary>The window's size in pixels when it is not fullscreen.</summary>
    private Point _windowSize;

    /// <summary>The smallest window the settings may ask for (CorsixTH's 640 by 480).</summary>
    private const int MinWidth = 640;
    private const int MinHeight = 480;

    /// <param name="width">The window's width in the system's points: pixels at 100 % display scale, more above.</param>
    /// <param name="height">The window's height, likewise.</param>
    public OpenWardsGame(string title, Func<GraphicsDevice, IScreen> createScreen, int width = 1280, int height = 800, bool fullscreen = false)
    {
        _createScreen = createScreen;

        // The game says it handles the screen's density itself (app.manifest), so Windows no longer stretches its
        // window smoothly; the window keeps the size it had on the screen, and ScreenCanvas scales the picture by whole
        // pixels instead.
        var density = ScreenCanvas.SystemDpiScale();
        _windowSize = new Point((int)Math.Round(Math.Max(MinWidth, width) * density), (int)Math.Round(Math.Max(MinHeight, height) * density));
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = _windowSize.X,
            PreferredBackBufferHeight = _windowSize.Y,

            // Fullscreen is a borderless window over the whole screen at the desktop's own resolution, as current games
            // and CorsixTH's SDL_WINDOW_FULLSCREEN_DESKTOP do: switching the monitor to the window's size instead
            // (1920 by 1200 on a 2560 by 1600 screen at 150 %) left the picture off the screen or black.
            HardwareModeSwitch = false,
        };
        if (fullscreen)
        {
            UseDesktopSize();
            _graphics.IsFullScreen = true;
        }
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
            SetFullscreen(!_graphics.IsFullScreen);
        }

        IsMouseVisible = _screen?.DrawsCursor != true;

        // App:setCaptureMouse, as current games do it: the cursor stays in the window only once it has been clicked
        // since getting focus, and only while the game wants it; without focus it is let go.
        var mouse = Mouse.GetState();
        var clickedInside = Window.ClientBounds is var bounds
            && mouse.X >= 0 && mouse.Y >= 0 && mouse.X < bounds.Width && mouse.Y < bounds.Height
            && (mouse.LeftButton == ButtonState.Pressed || mouse.RightButton == ButtonState.Pressed || mouse.MiddleButton == ButtonState.Pressed);
        MouseCapture.Set(Window.Handle, _grab.Update(IsActive && script is null, clickedInside, _screen?.CapturesMouse == true));

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

    /// <summary>Goes fullscreen at the desktop's resolution, or back to the window's last size.</summary>
    private void SetFullscreen(bool fullscreen)
    {
        if (fullscreen == _graphics.IsFullScreen)
        {
            return;
        }

        if (fullscreen)
        {
            // The window's size now, which may have been dragged, for coming back to.
            var client = Window.ClientBounds;
            if (client.Width >= MinWidth && client.Height >= MinHeight)
            {
                _windowSize = new Point(client.Width, client.Height);
            }

            UseDesktopSize();
        }
        else
        {
            _graphics.PreferredBackBufferWidth = _windowSize.X;
            _graphics.PreferredBackBufferHeight = _windowSize.Y;
        }

        _graphics.IsFullScreen = fullscreen;
        _graphics.ApplyChanges();
    }

    /// <summary>Asks for a back buffer the size of the desktop, in real pixels.</summary>
    private void UseDesktopSize()
    {
        var mode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        _graphics.PreferredBackBufferWidth = mode.Width;
        _graphics.PreferredBackBufferHeight = mode.Height;
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
