using System.Globalization;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Input;

/// <summary>
/// Mouse and keyboard played from a text file (<c>--script</c>) instead of the real ones, with screenshots of the whole
/// window, so that the client can be checked without anyone at the computer. One command a line, coordinates in the
/// window's pixels, <c>#</c> starts a comment:
/// <code>
/// wait 6000              milliseconds of real time
/// key Escape             press and release; several with +, e.g. key LeftShift+F3
/// hold LeftShift / release LeftShift
/// move 400 300
/// click 400 300 [right]  move there, press and release
/// drag 100 100 200 300   left button from one point to the other
/// button Hire [2]        click the (second) button reading Hire in the front window that has one
/// wheel 1                notches; negative zooms out
/// shot menu.png          screenshot, relative to the script's folder
/// log some text          written to the console
/// quit
/// </code>
/// </summary>
internal sealed class InputScript
{
    private const double KeyMilliseconds = 80;
    private const double ClickMilliseconds = 100;
    private const int WheelNotch = 120;

    private readonly Queue<Step> _steps;
    private readonly string _folder;
    private readonly HashSet<Keys> _down = [];
    private double _wait;
    // Outside the window until the first move, so that the screen's edges do not scroll the map.
    private int _x = -1;
    private int _y = -1;
    private int _wheel;
    private bool _left;
    private bool _right;

    private InputScript(IEnumerable<Step> steps, string folder)
    {
        _steps = new Queue<Step>(steps);
        _folder = folder;
    }

    /// <summary>The script driving the client, or null for the real mouse and keyboard.</summary>
    public static InputScript? Current { get; set; }

    /// <summary>Finds a window's button by its text and number (from 1) for <c>button</c>; set by the map screen.</summary>
    public static Func<string, int, Microsoft.Xna.Framework.Point?>? ButtonFinder { get; set; }

    /// <summary>A screenshot to save after the next frame is drawn, or null.</summary>
    public string? PendingShot { get; private set; }

    /// <summary>Whether the script has asked to close the client.</summary>
    public bool QuitRequested { get; private set; }

    /// <summary>The mouse: the script's while one runs, else the real one.</summary>
    public static MouseState Mouse() => Current?.MouseState ?? Microsoft.Xna.Framework.Input.Mouse.GetState();

    /// <summary>The keyboard: the script's while one runs, else the real one.</summary>
    public static KeyboardState Keyboard() => Current?.KeyboardState ?? Microsoft.Xna.Framework.Input.Keyboard.GetState();

    public MouseState MouseState => new(
        _x,
        _y,
        _wheel,
        _left ? ButtonState.Pressed : ButtonState.Released,
        ButtonState.Released,
        _right ? ButtonState.Pressed : ButtonState.Released,
        ButtonState.Released,
        ButtonState.Released);

    public KeyboardState KeyboardState => new([.. _down]);

    /// <summary>Reads a script file.</summary>
    /// <exception cref="InvalidDataException">A line is not a command this class knows.</exception>
    public static InputScript Load(string path)
    {
        var steps = new List<Step>();
        var lines = File.ReadAllLines(path);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var hash = line.IndexOf('#', StringComparison.Ordinal);
            var text = (hash >= 0 ? line[..hash] : line).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            try
            {
                steps.AddRange(Parse(text));
            }
            catch (Exception e) when (e is FormatException or ArgumentException or IndexOutOfRangeException)
            {
                throw new InvalidDataException($"{path}, line {i + 1}: {e.Message}", e);
            }
        }

        return new InputScript(steps, Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
    }

    /// <summary>Runs the script on by the real time that has passed.</summary>
    public void Advance(double milliseconds)
    {
        _wait -= milliseconds;
        while (_wait <= 0 && PendingShot is null && _steps.TryDequeue(out var step))
        {
            Run(step);
        }
    }

    /// <summary>Called once the pending screenshot has been saved.</summary>
    public void ShotTaken() => PendingShot = null;

    private void Run(Step step)
    {
        switch (step)
        {
            case Wait wait:
                _wait += wait.Milliseconds;
                break;
            case MoveTo move:
                (_x, _y) = (move.X, move.Y);
                break;
            case MoveToButton find:
                if (ButtonFinder?.Invoke(find.Text, find.Nth) is { } point)
                {
                    (_x, _y) = (point.X, point.Y);
                }
                else
                {
                    // Click nowhere rather than on whatever is under the last position.
                    (_x, _y) = (-1, -1);
                    Console.WriteLine($"script: no button '{find.Text}' ({find.Nth})");
                }

                break;
            case Button button when button.Right:
                _right = button.Down;
                break;
            case Button button:
                _left = button.Down;
                break;
            case KeyChange change when change.Down:
                _down.Add(change.Key);
                break;
            case KeyChange change:
                _down.Remove(change.Key);
                break;
            case Wheel wheel:
                _wheel += wheel.Notches * WheelNotch;
                break;
            case Shot shot:
                PendingShot = Path.Combine(_folder, shot.File);
                break;
            case Log log:
                Console.WriteLine($"script: {log.Text}");
                break;
            case Quit:
                QuitRequested = true;
                break;
        }
    }

    private static IEnumerable<Step> Parse(string text)
    {
        var space = text.IndexOf(' ', StringComparison.Ordinal);
        var command = (space < 0 ? text : text[..space]).ToLowerInvariant();
        var rest = space < 0 ? "" : text[(space + 1)..].Trim();
        var words = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int Number(int index) => int.Parse(words[index], NumberStyles.Integer, CultureInfo.InvariantCulture);

        switch (command)
        {
            case "wait":
                return [new Wait(Number(0))];
            case "key":
                var keys = rest.Split('+').Select(ParseKey).ToList();
                return
                [
                    .. keys.Select(key => new KeyChange(key, true)),
                    new Wait(KeyMilliseconds),
                    .. Enumerable.Reverse(keys).Select(key => new KeyChange(key, false)),
                    new Wait(KeyMilliseconds),
                ];
            case "hold":
                return [new KeyChange(ParseKey(rest), true), new Wait(KeyMilliseconds)];
            case "release":
                return [new KeyChange(ParseKey(rest), false), new Wait(KeyMilliseconds)];
            case "move":
                return [new MoveTo(Number(0), Number(1)), new Wait(ClickMilliseconds)];
            case "click":
                var right = words.Length > 2 && words[2].Equals("right", StringComparison.OrdinalIgnoreCase);
                return
                [
                    new MoveTo(Number(0), Number(1)),
                    new Wait(ClickMilliseconds),
                    new Button(right, true),
                    new Wait(ClickMilliseconds),
                    new Button(right, false),
                    new Wait(ClickMilliseconds),
                ];
            case "button":
                var last = words.Length > 1 && int.TryParse(words[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var nth) ? nth : 0;
                var label = last > 0 ? string.Join(' ', words[..^1]) : rest;
                return
                [
                    new MoveToButton(label, Math.Max(1, last)),
                    new Wait(ClickMilliseconds),
                    new Button(false, true),
                    new Wait(ClickMilliseconds),
                    new Button(false, false),
                    new Wait(ClickMilliseconds),
                ];
            case "drag":
                var (x1, y1, x2, y2) = (Number(0), Number(1), Number(2), Number(3));
                return
                [
                    new MoveTo(x1, y1),
                    new Wait(ClickMilliseconds),
                    new Button(false, true),
                    new Wait(ClickMilliseconds),
                    new MoveTo((x1 + x2) / 2, (y1 + y2) / 2),
                    new Wait(ClickMilliseconds),
                    new MoveTo(x2, y2),
                    new Wait(ClickMilliseconds),
                    new Button(false, false),
                    new Wait(ClickMilliseconds),
                ];
            case "wheel":
                return [new Wheel(Number(0)), new Wait(ClickMilliseconds)];
            case "shot":
                return [new Shot(rest)];
            case "log":
                return [new Log(rest)];
            case "quit":
                return [new Quit()];
            default:
                throw new FormatException($"unknown command '{command}'");
        }
    }

    private static Keys ParseKey(string name) => Enum.Parse<Keys>(name.Trim(), ignoreCase: true);

    private abstract record Step;

    private sealed record Wait(double Milliseconds) : Step;

    private sealed record MoveTo(int X, int Y) : Step;

    private sealed record MoveToButton(string Text, int Nth) : Step;

    private sealed record Button(bool Right, bool Down) : Step;

    private sealed record KeyChange(Keys Key, bool Down) : Step;

    private sealed record Wheel(int Notches) : Step;

    private sealed record Shot(string File) : Step;

    private sealed record Log(string Text) : Step;

    private sealed record Quit : Step;
}
