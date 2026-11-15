// The save and load windows in CorsixTH's look, ported from CorsixTH CorsixTH/Lua/dialogs/resizables/file_browser.lua
// (UIFileBrowser: the caption, the list with its name and date headers, Back and OK; FilteredTreeControl:
// drawExtraOnRow's dates, sortByName, sortByDate), dialogs/tree_ctrl.lua (rows of 14 pixels, the highlighted and
// selected rows, the scrollbar beside them) and file_browsers/save_game.lua and load_game.lua (the new savegame box,
// the list sizes, the buttons' labels).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// <c>UIFileBrowser</c>: 450 by 380, the caption centred at the top, the saves in a list of rows below it (their names,
/// and their dates at the right), a scrollbar, the new savegame box when saving, and Back and Save or Load.
/// </summary>
internal sealed class FileBrowserScreen
{
    public static readonly Point Size = new(450, 380);

    private const int RowHeight = 14;
    private const int HeaderHeight = 15;
    private const int ScrollbarWidth = 20;
    private const int ListX = 5;
    private const int ListY = 35;

    /// <summary>The scrollbar's slider and the highlighted rows (<c>col_scrollbar</c>).</summary>
    private static readonly Color ScrollbarColour = new(164, 156, 208);

    private readonly ResizablePanels _panels = new();
    private int _first;
    private bool _byName;
    private bool _descending = true;

    /// <summary>The list's height (<c>vertical_size</c>): 265 with the new savegame box under it, 295 to load.</summary>
    private static int ListHeight(bool saving) => saving ? 265 : 295;

    private static int VisibleRows(bool saving) => (ListHeight(saving) - (ListHeight(saving) % RowHeight) - HeaderHeight) / RowHeight;

    private static Rectangle ListArea(bool saving) => new(ListX, ListY, Size.X - 10 - ScrollbarWidth, ListHeight(saving) - (ListHeight(saving) % RowHeight));

    /// <summary>The saves in the order the headers sort them: by date, newest first, to begin with (<c>sortByDate</c>).</summary>
    public List<(string Path, string Label)> Sorted(IReadOnlyList<(string Path, string Label)> games)
    {
        IEnumerable<(string Path, string Label)> sorted = _byName
            ? games.OrderBy(game => game.Label, StringComparer.CurrentCultureIgnoreCase)
            : games.OrderBy(game => File.GetLastWriteTimeUtc(game.Path));
        if (_descending)
        {
            sorted = sorted.Reverse();
        }

        return [.. sorted];
    }

    /// <summary><c>sortByName</c> and <c>sortByDate</c>: a header clicked again turns the order round.</summary>
    private void SortBy(bool byName)
    {
        _descending = byName == _byName ? !_descending : !byName;
        _byName = byName;
        _first = 0;
    }

    /// <summary>The panels: the caption, the two headers, the new savegame box when saving, Back and OK.</summary>
    public List<DialogPanel> Panels(string title, bool saving, string? typed, bool typing, string okLabel, Action back, Action ok, Action startTyping)
    {
        var list = ListArea(saving);
        var buttonIndent = (Size.X - (2 * 135)) / 3;
        List<DialogPanel> panels =
        [
            DialogPanel.Title(new Rectangle((Size.X - 190) / 2, 10, 190, 20), title),
            DialogPanel.Button(new Rectangle(list.X + 1, list.Y + 1, list.Width - 170 + ScrollbarWidth, 13), UiText.Get("menu_list_window.name", "Name"), _ => SortBy(byName: true), UiText.Get("tooltip.menu_list_window.name", "Click here to sort the list by name")),
            DialogPanel.Button(new Rectangle(list.X + list.Width - 169 + ScrollbarWidth, list.Y + 1, 150, 13), UiText.Get("menu_list_window.save_date", "Last Modified"), _ => SortBy(byName: false), UiText.Get("tooltip.menu_list_window.save_date", "Click here to sort the list by last modification date")),
            DialogPanel.Button(new Rectangle(buttonIndent, 340, 135, 30), UiText.Get("menu_list_window.back", "Back"), _ => back(), UiText.Get("tooltip.menu_list_window.back", "Close this window")),
            DialogPanel.Button(new Rectangle(Size.X - 135 - buttonIndent, 340, 135, 30), okLabel, _ => ok(), UiText.Get("tooltip.menu_list_window.ok", "Confirm choice")),
        ];
        if (saving)
        {
            // The new savegame box: black, with the caption colours round it.
            var text = typing ? typed + "_" : typed is { Length: > 0 } name ? name : UiText.Get("save_game_window.new_save_game", "New Savegame");
            panels.Add(new DialogPanel(new Rectangle(5, 310, Size.X - 10, 17), text, DialogPanel.TextboxColour, Lowered: true, Click: _ => startTyping(),
                Tooltip: UiText.Get("tooltip.save_game_window.new_save_game", "Enter name for a new savegame"), Highlight: ResizableLook.Title, Shadow: DialogPanel.CaptionColour));
        }

        return panels;
    }

    /// <summary>Draws the window, its panels, the rows with their dates, and the scrollbar.</summary>
    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse, IReadOnlyList<DialogPanel> panels, IReadOnlyList<(string Path, string Label)> games, bool saving, string? selected)
    {
        _panels.Draw(batch, host, origin, Size, panels, mouse);
        var look = host.Graphics!.Resizable!;
        var font = ResizablePanels.Font(host);
        var list = ListArea(saving);
        var rows = VisibleRows(saving);
        _first = Math.Clamp(_first, 0, Math.Max(0, games.Count - rows));
        var hovered = RowAt(new Point(mouse.X, mouse.Y) - origin, games.Count, saving);
        for (var i = 0; i < rows && _first + i < games.Count; i++)
        {
            var (path, label) = games[_first + i];
            var row = new Rectangle(origin.X + list.X, origin.Y + list.Y + HeaderHeight + (i * RowHeight), list.Width, RowHeight);
            if (_first + i == hovered || string.Equals(path, selected, StringComparison.OrdinalIgnoreCase))
            {
                look.DrawBevel(batch, row, ScrollbarColour, lowered: false);
            }

            font.Draw(batch, ResizableLook.ClipLine(label, list.Width - 150, text => font.Measure(text).Width), new Vector2(row.X + 2, row.Y + 1));

            // drawExtraOnRow: the day and month as the game writes them, the year and the time.
            var when = File.GetLastWriteTime(path);
            var month = host.Texts?.String(6, when.Month) ?? when.ToString("MMM", CultureInfo.InvariantCulture);
            var date = string.Create(CultureInfo.InvariantCulture, $"{when.Day} {month} {when:yyyy HH:mm:ss}");
            font.Draw(batch, date, new Vector2(row.X + list.Width - 140, row.Y + 1));
        }

        // The scrollbar: a lowered panel beside the rows, its slider as long as the share of rows shown.
        var bar = new Rectangle(origin.X + list.Right, origin.Y + list.Y, ScrollbarWidth, list.Height);
        look.DrawBevel(batch, bar, ResizableLook.PanelDefault, lowered: true);
        if (games.Count > 0)
        {
            var length = Math.Max(10, bar.Height * Math.Min(rows, games.Count) / games.Count);
            var top = games.Count <= rows ? 0 : (bar.Height - length) * _first / (games.Count - rows);
            look.DrawBevel(batch, new Rectangle(bar.X + 1, bar.Y + top, bar.Width - 2, length), ScrollbarColour, lowered: false);
        }
    }

    /// <summary>
    /// The mouse: the panels' buttons; a click on a row picks that save; the wheel scrolls the rows, as does a click on
    /// the scrollbar above or below its slider.
    /// </summary>
    /// <returns>The row clicked, if any.</returns>
    public int? Mouse(IWindowHost host, Point local, MouseState mouse, MouseState previous, IReadOnlyList<DialogPanel> panels, int count, bool saving)
    {
        _panels.Mouse(host, panels, local, mouse, previous);
        var rows = VisibleRows(saving);
        if (mouse.ScrollWheelValue != previous.ScrollWheelValue)
        {
            _first = Math.Clamp(_first - Math.Sign(mouse.ScrollWheelValue - previous.ScrollWheelValue) * 3, 0, Math.Max(0, count - rows));
        }

        if (mouse.LeftButton != ButtonState.Released || previous.LeftButton != ButtonState.Pressed)
        {
            return null;
        }

        var list = ListArea(saving);
        if (new Rectangle(list.Right, list.Y, ScrollbarWidth, list.Height).Contains(local) && count > rows)
        {
            var slider = list.Y + ((list.Height - Math.Max(10, list.Height * rows / count)) * _first / (count - rows));
            _first = Math.Clamp(_first + (local.Y < slider ? -rows : rows), 0, count - rows);
            return null;
        }

        return RowAt(local, count, saving);
    }

    /// <summary>The save under the mouse in the list, if any.</summary>
    public int? RowAt(Point local, int count, bool saving)
    {
        var list = ListArea(saving);
        var rows = new Rectangle(list.X, list.Y + HeaderHeight, list.Width, VisibleRows(saving) * RowHeight);
        if (!rows.Contains(local))
        {
            return null;
        }

        var index = _first + ((local.Y - rows.Y) / RowHeight);
        return index < count ? index : null;
    }
}
