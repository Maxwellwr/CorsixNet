using CorsixNet.Content;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Maps;

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

    /// <summary>Shows a short notice in the status line.</summary>
    void Notice(string text);

    /// <summary>Plays one of the original's sound effects.</summary>
    void PlaySound(string name);

    /// <summary>Picks a member of staff up to put them down elsewhere, as the M key does.</summary>
    void PickUpStaff(int staffId);

    /// <summary>Moves the view to a tile.</summary>
    void CenterOn(TilePosition tile);
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

    /// <summary>Whether the window has nothing more to show, such as a patient who has gone home; it is closed then.</summary>
    public virtual bool IsDone(IWindowHost host) => false;

    /// <summary>The window's table, built afresh each frame from the game's state.</summary>
    public abstract IEnumerable<WindowRow> Rows(IWindowHost host);
}
