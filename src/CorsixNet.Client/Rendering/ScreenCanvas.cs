using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Rendering;

/// <summary>
/// The picture the screens draw: the window itself, or, at an interface scale above 1, a picture as many times smaller
/// that is blown up to the window with each pixel a square of whole pixels, so that the game's pixel art and lettering
/// stay sharp on a screen of high density (where Windows would otherwise stretch the whole window smoothly). Screens
/// see only the picture: its size is the device's viewport while they update and draw, and the mouse is given in its
/// pixels.
/// </summary>
internal static partial class ScreenCanvas
{
    /// <summary>The largest interface scale the settings offer.</summary>
    public const int MaxScale = 4;

    /// <summary>The smallest picture the game is laid out for (CorsixTH's 640 by 480).</summary>
    public static readonly Point MinSize = new(640, 480);

    private static RenderTarget2D? _target;

    /// <summary>How many window pixels each of the picture's pixels takes across and down.</summary>
    public static int Scale { get; private set; } = 1;

    /// <summary>The picture's size.</summary>
    public static Point Size { get; private set; }

    /// <summary>
    /// The scale the settings ask for (<c>ui_scale</c>): 1 to <see cref="MaxScale"/>, or 0 for the screen's own
    /// (<see cref="AutomaticScale"/>).
    /// </summary>
    public static int Requested { get; set; }

    /// <summary>
    /// The screen's density as a whole scale: Windows' display scale rounded, 150 % and up counting as 2; 1 elsewhere.
    /// </summary>
    public static int AutomaticScale => ScaleFor(SystemDpiScale());

    /// <summary>The scale for a display scale (1.0 for 100 %), halves rounded up, between 1 and <see cref="MaxScale"/>.</summary>
    internal static int ScaleFor(double displayScale) => Math.Clamp((int)Math.Floor(displayScale + 0.5), 1, MaxScale);

    /// <summary>
    /// The scale a window of <paramref name="window"/> pixels is drawn at: the one asked for (or the automatic one), but
    /// never so large that the picture is smaller than <see cref="MinSize"/>.
    /// </summary>
    internal static int ScaleFor(int requested, int automatic, Point window)
    {
        var scale = Math.Clamp(requested <= 0 ? automatic : requested, 1, MaxScale);
        while (scale > 1 && (window.X / scale < MinSize.X || window.Y / scale < MinSize.Y))
        {
            scale--;
        }

        return scale;
    }

    /// <summary>Windows' display scale for the system (1.0 for 100 %, 1.5 for 150 %); 1.0 elsewhere or when unknown.</summary>
    public static double SystemDpiScale()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
        {
            return 1;
        }

        try
        {
            var dpi = GetDpiForSystem();
            return dpi > 0 ? dpi / 96.0 : 1;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return 1;
        }
    }

    /// <summary>
    /// Sizes the picture for the window's back buffer and makes it the device's viewport, for the screen's update.
    /// </summary>
    public static void BeginFrame(GraphicsDevice device)
    {
        var buffer = new Point(device.PresentationParameters.BackBufferWidth, device.PresentationParameters.BackBufferHeight);
        Scale = ScaleFor(Requested, AutomaticScale, buffer);
        Size = new Point(Math.Max(1, buffer.X / Scale), Math.Max(1, buffer.Y / Scale));
        if (Scale == 1)
        {
            _target?.Dispose();
            _target = null;
        }
        else if (_target is null || _target.Width != Size.X || _target.Height != Size.Y)
        {
            _target?.Dispose();
            _target = new RenderTarget2D(device, Size.X, Size.Y, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        }

        device.Viewport = new Viewport(0, 0, Size.X, Size.Y);
    }

    /// <summary>Draws on the picture from now on (the screens' <c>SetRenderTarget(null)</c>).</summary>
    public static void Bind(GraphicsDevice device) => device.SetRenderTarget(_target);

    /// <summary>Blows the picture up to the window, each pixel a square of <see cref="Scale"/> by <see cref="Scale"/>.</summary>
    public static void Present(GraphicsDevice device, SpriteBatch batch)
    {
        if (_target is null)
        {
            return;
        }

        device.SetRenderTarget(null);
        device.Clear(Color.Black);
        batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp);
        batch.Draw(_target, new Rectangle(0, 0, Size.X * Scale, Size.Y * Scale), Color.White);
        batch.End();
    }

    /// <summary>The mouse in the picture's pixels.</summary>
    public static MouseState ToCanvas(MouseState mouse) => Scale == 1
        ? mouse
        : new MouseState(
            FloorDivide(mouse.X, Scale),
            FloorDivide(mouse.Y, Scale),
            mouse.ScrollWheelValue,
            mouse.LeftButton,
            mouse.MiddleButton,
            mouse.RightButton,
            mouse.XButton1,
            mouse.XButton2,
            mouse.HorizontalScrollWheelValue);

    /// <summary>Lets the picture go (the game is closing).</summary>
    public static void Dispose()
    {
        _target?.Dispose();
        _target = null;
    }

    /// <summary>Division rounding down, so that a cursor just left of or above the window stays outside the picture.</summary>
    private static int FloorDivide(int value, int divisor) => value >= 0 ? value / divisor : ((value + 1) / divisor) - 1;

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();
}
