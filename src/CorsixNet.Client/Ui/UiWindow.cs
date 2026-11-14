using CorsixNet.Client.Rendering;
using CorsixNet.Content;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
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

/// <summary>
/// The interface's texts in the language the settings choose, for code without a window host: CorsixTH's strings by
/// name (<c>options_window.caption</c>) and CorsixNet's own (<c>corsixnet.*</c>, from <c>Languages/CorsixNet</c>), each
/// with its English stand-in for when no translation is loaded.
/// </summary>
internal static class UiText
{
    /// <summary>The loaded texts, set whenever the language is (re)loaded; null before and in tests.</summary>
    public static OriginalTexts? Current { get; set; }

    /// <summary>The string by name in the chosen language, or <paramref name="fallback"/> (English).</summary>
    public static string Get(string name, string fallback) => Current?.Named(name) ?? fallback;

    /// <summary>
    /// The string by name as a plain label: CorsixTH's hotkey placeholders (<c>(%1%)</c>) and the padding of its menu
    /// items taken out (<c>"  (%1%) LOAD  "</c> gives <c>LOAD</c>).
    /// </summary>
    public static string Label(string name, string fallback) =>
        System.Text.RegularExpressions.Regex.Replace(Get(name, fallback), @"\(?%\d+%\)?", string.Empty).Trim();

    /// <summary>A room's name (CorsixTH's <c>rooms_short</c>, which each room's <c>room.name</c> names).</summary>
    public static string Room(RoomDefinition room) => Get("rooms_short." + RoomKey(room.Id), room.Name);

    /// <summary>The name of the room with this id, the id itself when the content has no such room.</summary>
    public static string RoomName(GameContent content, string roomId) =>
        content.FindRoom(roomId) is { } room ? Room(room) : Get("rooms_short." + RoomKey(roomId), roomId);

    /// <summary>An object's name (CorsixTH's <c>object</c> strings, which each object's <c>object.name</c> names).</summary>
    public static string Object(ObjectDefinition definition) => Get("object." + ObjectKey(definition.Id), definition.Name);

    /// <summary>The name of the object with this id, the id itself when the content has no such object.</summary>
    public static string ObjectName(GameContent content, string objectId) =>
        content.FindObject(objectId) is { } definition ? Object(definition) : Get("object." + ObjectKey(objectId), objectId);

    /// <summary>The name of a room by its id alone, the id itself without a translation.</summary>
    public static string RoomName(string roomId) => Get("rooms_short." + RoomKey(roomId), roomId);

    /// <summary>The name of an object by its id alone, the id itself without a translation.</summary>
    public static string ObjectName(string objectId) => Get("object." + ObjectKey(objectId), objectId);

    /// <summary>The name of a room or else an object by its id (what a sale was of).</summary>
    public static string ObjectOrRoomName(string id) =>
        Current?.Named("rooms_short." + RoomKey(id)) ?? ObjectName(id);

    /// <summary>The <c>rooms_short</c> name of a room id where CorsixTH's room files name it differently.</summary>
    private static string RoomKey(string id) => id switch
    {
        "gp" => "gps_office",
        "psych" => "psychiatric",
        "research" => "research_room",
        "slack_tongue" => "tongue_clinic",
        "staff_room" => "staffroom",
        "training" => "training_room",
        _ => id,
    };

    /// <summary>The <c>object</c> name of an object id where CorsixTH's object files name it differently.</summary>
    private static string ObjectKey(string id) => id switch
    {
        "analyser" => "atom_analyser",
        "autopsy" => "auto_autopsy",
        "entrance_left_door" => "entrance_left",
        "entrance_right_door" => "entrance_right",
        "swing_door_left" => "swing_door1",
        "swing_door_right" => "swing_door2",
        "extinguisher" => "fire_extinguisher",
        "gates_to_hell" => "gates_of_hell",
        "loo" => "toilet",
        "operating_table_b" => "operating_table",
        "radiation_shield_b" => "radiation_shield",
        "sink" => "toilet_sink",
        _ => id,
    };

    /// <summary>The string by name with its placeholders filled (<see cref="Fill"/>).</summary>
    public static string Format(string name, string fallback, params object?[] args) => Fill(Get(name, fallback), args);

    /// <summary>
    /// Fills CorsixTH's placeholders: <c>%1%</c>, <c>%2%</c>... by number, <c>%s</c> and <c>%d</c> with the arguments in
    /// order, <c>%%</c> as a percent sign; anything else is kept as written.
    /// </summary>
    public static string Fill(string template, params object?[] args)
    {
        if (!template.Contains('%', StringComparison.Ordinal))
        {
            return template;
        }

        var result = new System.Text.StringBuilder(template.Length + 16);
        var next = 0;
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c != '%' || i + 1 >= template.Length)
            {
                result.Append(c);
                continue;
            }

            var following = template[i + 1];
            if (following == '%')
            {
                result.Append('%');
                i++;
            }
            else if (following is 's' or 'd')
            {
                result.Append(Argument(next++));
                i++;
            }
            else if (char.IsAsciiDigit(following))
            {
                var end = i + 1;
                while (end < template.Length && char.IsAsciiDigit(template[end]))
                {
                    end++;
                }

                if (end < template.Length && template[end] == '%')
                {
                    result.Append(Argument(int.Parse(template.AsSpan(i + 1, end - i - 1), System.Globalization.CultureInfo.InvariantCulture) - 1));
                    i = end;
                }
                else
                {
                    result.Append(c);
                }
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();

        string Argument(int index) =>
            index >= 0 && index < args.Length ? Convert.ToString(args[index], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;
    }
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

    /// <summary>Whether the winning letter waits on the fax (World:winGame), until it is taken or turned down.</summary>
    bool IsWinLetterPending => false;

    /// <summary>decline_new_level: the letter goes and the level goes on.</summary>
    void DeclineWinLetter()
    {
    }

    /// <summary>The key given to an action in the hotkeys file, as the player reads it (such as F1), or null.</summary>
    string? HotkeyText(string action);

    /// <summary>The fullscreen dialogs' pictures, sprites and fonts, or null without the original's graphics.</summary>
    OriginalGraphics? Graphics => null;

    /// <summary>The player's name, which their hospital goes by (CorsixTH's <c>player_name</c>).</summary>
    string PlayerName => "Player";

    /// <summary>The level's town, in capitals, as the town map shows it (<c>map.level_name</c>), or null if unknown.</summary>
    string? LevelTitle => null;

    /// <summary>The next level's town, as the winning letter offers it (<c>_S.level_names[level + 1]</c>), or null.</summary>
    string? NextLevelTitle => null;

    /// <summary>
    /// The map round an entity as it looked last frame (a window's view circle), 75 by 75 world pixels, or null until
    /// the next frame has drawn it; asking keeps it drawn.
    /// </summary>
    Texture2D? CloseUp(int entityId) => null;

    /// <summary>Whether the epidemic's vaccination mode is on (<c>Epidemic.vaccination_mode_active</c>).</summary>
    bool IsVaccinationMode => false;

    /// <summary>The watch's vaccination button (<c>Epidemic:toggleVaccinationMode</c>).</summary>
    void ToggleVaccinationMode()
    {
    }

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

    /// <summary>Whether Esc closes the window (CorsixTH's <c>esc_closes</c>); a raise request must be answered.</summary>
    public virtual bool ClosesWithEsc => true;

    /// <summary>Whether the game stands still while the window is open (CorsixTH's <c>Window:mustPause</c>).</summary>
    public virtual bool MustPause => false;

    /// <summary>
    /// Where the window first opens, as CorsixTH's <c>Window:setDefaultPosition</c>: for each of x and y a negative value
    /// is pixels from the right or bottom edge, a value from 0 up to 1 a share of the free space (0.5 centres it) and
    /// 1 or more pixels from the left or top. Null cascades it from the top left as the other windows.
    /// </summary>
    public virtual Vector2? DefaultPosition => null;

    /// <summary>Whether the window has nothing more to show, such as a patient who has gone home; it is closed then.</summary>
    public virtual bool IsDone(IWindowHost host) => false;

    /// <summary>
    /// The window has been closed, by a button of its own, its close box, Esc or with every dialog, such as to give
    /// back settings it tried out while open.
    /// </summary>
    public virtual void Closed()
    {
    }

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
    public virtual Vector2? FullscreenPosition => null;

    /// <summary>Where on the screen the window in its original look goes, kept within the area, such as a popup at the cursor; before <see cref="FullscreenPosition"/>.</summary>
    public virtual Point? FullscreenScreenPosition => null;

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

    /// <summary>
    /// Whether the window, on top in its original look, handles a key action (CorsixTH's <c>addKeyHandler</c> on the
    /// window: <c>ingame_scroll_up</c> and the other scroll actions, <c>global_confirm</c>); the map does not see it then.
    /// </summary>
    public virtual bool TakesKey(string action) => false;

    /// <summary>A key action the window takes (<see cref="TakesKey"/>) was pressed.</summary>
    public virtual void FullscreenKey(IWindowHost host, string action)
    {
    }
}
