// The debug console, standing in for CorsixTH's Lua console (CorsixTH/Lua/dialogs/resizables/lua_console.lua: UILuaConsole,
// 320 by 240 in green, a black text box, Execute and Close; opened with F12, global_showLuaConsole, or from the debug
// menu). This game has no Lua to run: the box takes short commands instead, which play the cheats and debug actions
// as commands, so that they reach every player of a network game alike.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenWards.Content.Settings;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Economy;

namespace OpenWards.Client.Ui.Windows;

/// <summary>
/// The debug console: lines typed in the box run on Return or Execute (<c>buttonExecute</c>); what they did is written
/// above the line being typed.
/// </summary>
/// <param name="debugAction">Runs one of the debug menu's actions by its name ("debugrat"), or null for none.</param>
internal sealed class DebugConsoleWindow(Action<string>? debugAction = null) : UiWindow, ITextInputWindow
{
    public static readonly Point Size = new(320, 240);

    /// <summary>How many lines the box keeps (the last ones show).</summary>
    public const int KeptLines = 40;

    /// <summary>The console's green (<c>col_bg</c>).</summary>
    private static readonly Color Background = new(46, 186, 60);

    private static readonly Color Highlight = new(174, 166, 218);
    private static readonly Color Shadow = new(134, 126, 178);
    private static readonly Rectangle TextBox = new(20, 20, 280, 140);

    /// <summary>The commands, with the cheat each plays (null for those handled apart) and how many times at most.</summary>
    private static readonly (string Name, CheatKind? Cheat, int MaxRepeat)[] Commands =
    [
        ("money", CheatKind.Money, 100),
        ("patient", CheatKind.CreatePatient, 50),
        ("vip", CheatKind.Vip, 1),
        ("emergency", CheatKind.Emergency, 1),
        ("epidemic", CheatKind.Epidemic, 1),
        ("earthquake", CheatKind.Earthquake, 1),
        ("reputation", CheatKind.MaxReputation, 1),
        ("research", CheatKind.AllResearch, 1),
        ("repair", CheatKind.RepairAllMachines, 1),
        ("endmonth", CheatKind.EndMonth, 12),
        ("endyear", CheatKind.EndYear, 10),
        ("win", CheatKind.WinLevel, 1),
        ("lose", CheatKind.LoseLevel, 1),
        ("rat", null, 1),
        ("help", null, 1),
        ("clear", null, 1),
    ];

    private readonly List<string> _lines = [];
    private readonly ResizablePanels _panels = new();
    private string _text = string.Empty;

    /// <summary>The line being typed.</summary>
    public string TypedText => _text;

    /// <summary>What the console has written, oldest first.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>The text box takes the keys while the console is open (<c>textbox:setActive(true)</c>).</summary>
    public bool IsCapturing { get; private set; } = true;

    public override string Title => UiText.Get("openwards.console.caption", "Debug console");

    public override bool UsesResizableLook => true;

    public override bool IsSameAs(UiWindow other) => other is DebugConsoleWindow;

    /// <summary><c>setDefaultPosition(0.1, 0.1)</c>.</summary>
    public override Vector2? DefaultPosition => new(0.1f, 0.1f);

    public override Vector2? FullscreenPosition => new(0.1f, 0.1f);

    public override bool IsFullscreen(IWindowHost host) => ResizablePanels.IsAvailable(host);

    public override Point FullscreenSize => Size;

    public void Capture(Hotkey key)
    {
        switch (key.Key)
        {
            case "escape":
                IsCapturing = false;
                break;
            case "backspace":
                _text = _text.Length > 0 ? _text[..^1] : _text;
                break;
            case "return":
                _pendingRun = true;
                break;
        }
    }

    public void Type(char character)
    {
        if (IsCapturing && !char.IsControl(character) && _text.Length < 80)
        {
            _text += character;
        }
    }

    /// <summary>Return was pressed; the line runs on the next layout, where the host is known.</summary>
    private bool _pendingRun;

    /// <summary>
    /// What a line does: the commands to send, or why it cannot be run. "money 3" plays the money cheat three times;
    /// "help", "clear" and "rat" give no command (they are done by the console itself).
    /// </summary>
    public static (string Name, IReadOnlyList<Command> Commands, string? Problem) Parse(string line)
    {
        var words = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return (string.Empty, [], null);
        }

        var name = words[0].ToLowerInvariant();
        var entry = Commands.FirstOrDefault(command => command.Name == name);
        if (entry.Name is null)
        {
            return (name, [], UiText.Format("openwards.console.unknown", "Unknown command: %1%. Type help for the list.", words[0]));
        }

        var times = 1;
        if (words.Length > 1 && (!int.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out times) || times < 1))
        {
            return (name, [], UiText.Format("openwards.console.unknown", "Unknown command: %1%. Type help for the list.", line.Trim()));
        }

        times = Math.Min(times, entry.MaxRepeat);
        return (name, entry.Cheat is { } cheat ? Enumerable.Repeat<Command>(new CheatCommand(cheat), times).ToList() : [], null);
    }

    /// <summary>
    /// <c>buttonExecute</c>: runs the line typed, writes it (after a dash: the game's font has no "&gt;") and what came of
    /// it, and empties the box.
    /// </summary>
    public void Execute(IWindowHost host)
    {
        var line = _text.Trim();
        _text = string.Empty;
        IsCapturing = true;
        if (line.Length == 0)
        {
            return;
        }

        Write("- " + line);
        var (name, commands, problem) = Parse(line);
        if (problem is not null)
        {
            Write(problem);
            return;
        }

        switch (name)
        {
            case "help":
                Write(UiText.Get("openwards.console.commands", "Commands:") + " " + string.Join(' ', Commands.Select(command => command.Name)));
                return;
            case "clear":
                _lines.Clear();
                return;
            case "rat":
                debugAction?.Invoke("debugrat");
                break;
        }

        foreach (var command in commands)
        {
            host.Submit(command);
        }

        Write(UiText.Get("openwards.console.done", "Done."));
    }

    private void Write(string line)
    {
        _lines.Add(line);
        if (_lines.Count > KeptLines)
        {
            _lines.RemoveAt(0);
        }
    }

    private List<DialogPanel> Panels(IWindowHost host) =>
    [
        DialogPanel.Button(new Rectangle(20, Size.Y - 60, 130, 40), UiText.Get("lua_console.execute_code", "Execute"), _ => Execute(host),
            UiText.Get("openwards.console.execute_tooltip", "Run the command typed")),
        DialogPanel.Button(new Rectangle(170, Size.Y - 60, 130, 40), UiText.Get("lua_console.close", "Close"), _ => host.Close(this),
            UiText.Get("tooltip.lua_console.close", "Close the console")),
    ];

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        RunPending(host);
        _panels.Draw(batch, host, origin, Size, Panels(host), mouse, Background);
        var look = host.Graphics!.Resizable!;
        var box = new Rectangle(origin.X + TextBox.X, origin.Y + TextBox.Y, TextBox.Width, TextBox.Height);
        look.DrawBevel(batch, box, Color.Black, lowered: true, highlight: Highlight, shadow: Shadow);

        // The lines written, the newest at the bottom, then the line being typed.
        var font = ResizablePanels.Font(host);
        int Width(string text) => font.Measure(text).Width;
        var shown = _lines.SelectMany(line => Maps.MapViewScreen.WrapToWidth(line, TextBox.Width - 8, Width))
            .Append(ResizableLook.ClipLine("- " + _text + (IsCapturing ? "_" : string.Empty), TextBox.Width - 8, Width)).ToList();
        var rows = (TextBox.Height - 4) / font.LineHeight;
        var top = box.Y + 2;
        foreach (var line in shown.Skip(Math.Max(0, shown.Count - rows)))
        {
            font.Draw(batch, line, new Vector2(box.X + 4, top));
            top += font.LineHeight;
        }
    }

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous)
    {
        RunPending(host);
        _panels.Mouse(host, Panels(host), local, mouse, previous);
        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released && TextBox.Contains(local))
        {
            IsCapturing = true;
        }
    }

    public override string? FullscreenTooltip(IWindowHost host, Point local) => ResizablePanels.Tooltip(Panels(host), local);

    public override Point? FindFullscreenButton(IWindowHost host, string text, int nth) => ResizablePanels.FindButton(Panels(host), text, nth);

    /// <summary>Without CorsixTH's sprites: the lines, the one being typed, Execute and Close.</summary>
    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        RunPending(host);
        foreach (var line in _lines.Skip(Math.Max(0, _lines.Count - 8)))
        {
            yield return line;
        }

        yield return WindowRow.Of(WindowCell.Button("- " + _text + (IsCapturing ? "_" : string.Empty), () => IsCapturing = true, highlight: IsCapturing));
        yield return WindowRow.Of(
            WindowCell.Button(UiText.Get("lua_console.execute_code", "Execute"), () => Execute(host)),
            WindowCell.Button(UiText.Get("lua_console.close", "Close"), () => host.Close(this)));
    }

    private void RunPending(IWindowHost host)
    {
        if (_pendingRun)
        {
            _pendingRun = false;
            Execute(host);
        }
    }
}
