using CorsixNet.Client.Rendering;
using CorsixNet.Content;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Staff;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui;

/// <summary>How a cell's text sits in its column.</summary>
internal enum CellAlign
{
    Left,
    Right,
}

/// <summary>
/// One cell of a window's table: text, and what a click on it does. A cell with an action is drawn as a button.
/// </summary>
/// <param name="Text">What the cell shows.</param>
/// <param name="OnClick">What a click does, or null for plain text.</param>
/// <param name="Dim">Drawn greyed out: something unavailable, or a button that cannot be used now.</param>
/// <param name="Highlight">Drawn in the highlight colour: the selected row, the current choice.</param>
/// <param name="Align">Where the text sits in its column.</param>
/// <param name="Span">Whether the cell runs over the rest of the row instead of taking a column.</param>
internal sealed record WindowCell(string Text, Action? OnClick = null, bool Dim = false, bool Highlight = false, CellAlign Align = CellAlign.Left, bool Span = false)
{
    public static implicit operator WindowCell(string text) => new(text);

    /// <summary>A button: an action, greyed out and doing nothing when <paramref name="enabled"/> is false.</summary>
    public static WindowCell Button(string text, Action onClick, bool enabled = true, bool highlight = false) =>
        new(text, enabled ? onClick : null, Dim: !enabled, Highlight: highlight) { IsButton = true };

    /// <summary>A number or amount, right-aligned in its column.</summary>
    public static WindowCell Number(string text, bool dim = false, bool highlight = false) => new(text, Dim: dim, Highlight: highlight, Align: CellAlign.Right);

    /// <summary>A line of text over the whole row, such as a heading or a note.</summary>
    public static WindowCell Line(string text, bool dim = false, bool highlight = false) => new(text, Dim: dim, Highlight: highlight, Span: true);

    /// <summary>Whether the cell is drawn as a button, also when it cannot be clicked now.</summary>
    public bool IsButton { get; init; }

    /// <summary>
    /// The sound a click on the button plays (CorsixTH's <c>button.sound</c>): null for the windows' default_button_sound,
    /// selectx.wav; empty for none.
    /// </summary>
    public string? Sound { get; init; }

    /// <summary>What the cell's tooltip says while the mouse rests on it, or null for none.</summary>
    public string? Tooltip { get; init; }

    /// <summary>The cell with a tooltip.</summary>
    public WindowCell WithTooltip(string? tooltip) => this with { Tooltip = tooltip };
}

/// <summary>
/// The original's tooltips for the windows (CorsixTH's <c>tooltip.*</c> texts, section 32 of the strings, as
/// <c>languages/original_strings.lua</c> maps them), with English stand-ins when the strings are not loaded.
/// </summary>
internal static class OriginalTooltips
{
    /// <summary>String <paramref name="index"/> of section 32, or <paramref name="fallback"/> without the game's strings.</summary>
    public static string Get(IWindowHost host, int index, string fallback) => host.Texts?.String(32, index) ?? fallback;
}

/// <summary>CorsixTH's own interface strings, by name in its language files (<c>main_menu.new_game</c>).</summary>
internal static class CorsixText
{
    /// <summary>The chosen translation's string by name, or <paramref name="fallback"/> (CorsixTH's English).</summary>
    public static string Get(IWindowHost host, string name, string fallback) => host.Texts?.Named(name) ?? fallback;
}

/// <summary>One row of a window's table.</summary>
internal sealed record WindowRow(IReadOnlyList<WindowCell> Cells)
{
    /// <summary>An empty row, a gap between parts of a window.</summary>
    public static WindowRow Gap { get; } = new([]);

    public static implicit operator WindowRow(WindowCell cell) => new([cell]);

    public static implicit operator WindowRow(string text) => new([WindowCell.Line(text)]);

    public static WindowRow Of(params WindowCell[] cells) => new(cells);
}

/// <summary>What a window can do besides showing figures: send the player's commands, open and close windows.</summary>
internal interface IWindowHost
{
    /// <summary>The game shown.</summary>
    Simulation Simulation { get; }

    /// <summary>The local player's number.</summary>
    int Player { get; }

    /// <summary>The game's texts, or null without string files.</summary>
    OriginalTexts? Texts { get; }

    /// <summary>Issues a command for the local player.</summary>
    void Submit(Command command);

    /// <summary>Opens a window, or brings it to the front if one of its kind is open.</summary>
    void Open(UiWindow window);

    /// <summary>Closes a window.</summary>
    void Close(UiWindow window);

    /// <summary>The adviser, who says the game's advice over the bottom panel.</summary>
    Adviser Adviser { get; }

    /// <summary>Shows a short notice in the status line.</summary>
    void Notice(string text);

    /// <summary>Plays one of the original's sound effects.</summary>
    void PlaySound(string name);

    /// <summary>Picks a member of staff up to put them down elsewhere, as the M key does.</summary>
    void PickUpStaff(int staffId);

    /// <summary>Moves the view to a tile.</summary>
    void CenterOn(TilePosition tile);

    /// <summary>Puts a candidate for hire on the cursor, to be placed in the hospital with a click (CorsixTH's UIPlaceStaff).</summary>
    void StartHiring(StaffClass staffClass, int index);

    /// <summary>Whether there is a level after this one to go on to.</summary>
    bool HasNextLevel { get; }

    /// <summary>Starts the next level, or this one again.</summary>
    void StartLevel(bool next);

    /// <summary>The key given to an action in the hotkeys file, as the player reads it (such as F1), or null.</summary>
    string? HotkeyText(string action);

    /// <summary>The fullscreen dialogs' pictures, sprites and fonts, or null without the original's graphics.</summary>
    OriginalGraphics? Graphics => null;

    /// <summary>The player's name, which their hospital goes by (CorsixTH's <c>player_name</c>).</summary>
    string PlayerName => "Player";

    /// <summary>
    /// The map round an entity as it looked last frame (a window's view circle), 75 by 75 world pixels, or null until
    /// the next frame has drawn it; asking keeps it drawn.
    /// </summary>
    Texture2D? CloseUp(int entityId) => null;

    /// <summary>Draws a humanoid standing in the first frame of their idle animation facing west, as UIQueue:drawPatient.</summary>
    void DrawIdle(SpriteBatch batch, Humanoid humanoid, Point at)
    {
    }

    /// <summary>Draws an animation of the game's (such as a room's preview) at a point, playing at the game's 18 frames a second.</summary>
    void DrawAnimation(SpriteBatch batch, int animation, Point at)
    {
    }

    /// <summary>The map tile under a point of the screen, or null off the map.</summary>
    TilePosition? TileAtScreen(Point screen) => null;
}

/// <summary>
/// A window over the map: a stand-in for one of CorsixTH's dialogs, showing its figures and buttons as a table of text
/// until the original's panels are drawn. The window keeps only interface state (a page, a selection); the game
/// changes through commands.
/// </summary>
internal abstract class UiWindow
{
    /// <summary>The title bar's text.</summary>
    public abstract string Title { get; }

    /// <summary>The key that opens and closes the window, if any, shown in the title bar.</summary>
    public virtual string? Hotkey => null;

    /// <summary>
    /// Whether this window stands for the same dialog as <paramref name="other"/>, so that opening it again brings the
    /// open one forward (CorsixTH's <c>UI:addWindow</c> keeps one of each fullscreen dialog).
    /// </summary>
    public virtual bool IsSameAs(UiWindow other) => other.GetType() == GetType();

    /// <summary>
    /// Whether the window is part of the screen rather than a dialog, such as the bottom panel: it sits at the bottom,
    /// has no title bar and is not closed with Esc.
    /// </summary>
    public virtual bool IsPinned => false;

    /// <summary>
    /// Where the window first opens, as CorsixTH's <c>Window:setDefaultPosition</c>: for each of x and y a negative value
    /// is pixels from the right or bottom edge, a value from 0 up to 1 a share of the free space (0.5 centres it) and
    /// 1 or more pixels from the left or top. Null cascades it from the top left as the other windows.
    /// </summary>
    public virtual Vector2? DefaultPosition => null;

    /// <summary>Whether the window has nothing more to show, such as a patient who has gone home; it is closed then.</summary>
    public virtual bool IsDone(IWindowHost host) => false;

    /// <summary>The window's table, built afresh each frame from the game's state.</summary>
    public abstract IEnumerable<WindowRow> Rows(IWindowHost host);

    /// <summary>The size of a drawing area below the table, such as a chart, or null for none.</summary>
    public virtual Point? CanvasSize => null;

    /// <summary>A click in the drawing area, at <paramref name="local"/> from its top left corner.</summary>
    public virtual void CanvasClicked(IWindowHost host, Point local)
    {
    }

    /// <summary>Draws the window's drawing area (see <see cref="CanvasSize"/>) in screen coordinates.</summary>
    public virtual void DrawCanvas(SpriteBatch batch, Rectangle area, Texture2D pixel, TextRenderer text)
    {
    }

    /// <summary>
    /// Whether the window draws itself as the original's 640 by 480 fullscreen dialog (CorsixTH's <c>UIFullscreen</c>),
    /// centred, without a title bar, instead of its table; false where the dialog's graphics are missing.
    /// </summary>
    public virtual bool IsFullscreen(IWindowHost host) => false;

    /// <summary>The size of the window drawn by itself: 640 by 480 for a fullscreen dialog, its own for a smaller one.</summary>
    public virtual Point FullscreenSize => new(640, 480);

    /// <summary>Where a window drawn by itself opens from the top left of the screen's free area; null centres it.</summary>
    public virtual Point? FullscreenPosition => null;

    /// <summary>Whether the window takes the mouse wherever it is (dragging something out of it), not only over it.</summary>
    public virtual bool CapturesMouse => false;

    /// <summary>Draws the fullscreen dialog with its top left corner at <paramref name="origin"/>.</summary>
    public virtual void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
    }

    /// <summary>The mouse over the fullscreen dialog, at <paramref name="local"/> from its top left corner.</summary>
    public virtual void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous)
    {
    }

    /// <summary>The fullscreen dialog's tooltip at <paramref name="local"/>, if any.</summary>
    public virtual string? FullscreenTooltip(IWindowHost host, Point local) => null;
}
