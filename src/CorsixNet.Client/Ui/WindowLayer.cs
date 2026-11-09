using CorsixNet.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui;

/// <summary>
/// The windows open over the map, front one last: lays out their tables, draws them, and takes the mouse while it is
/// over one. A window is moved by dragging its title bar and closed with its [x] or Esc; the wheel scrolls a long one.
/// </summary>
internal sealed class WindowLayer
{
    private const int Padding = 8;
    private const int ColumnGap = 14;
    private const int CascadeStep = 24;

    private static readonly Color PanelColor = new(0, 0, 0, 215);
    private static readonly Color TitleColor = new(0, 48, 144);
    private static readonly Color ButtonColor = new(55, 55, 80);
    private static readonly Color ButtonHover = new(60, 90, 160);
    private static readonly Color DimText = new(140, 140, 140);
    private static readonly Color HighlightText = new(255, 220, 120);

    private readonly List<OpenWindow> _open = [];
    private readonly Func<string, int> _measure;
    private readonly int _lineHeight;
    private OpenWindow? _dragging;
    private Point _dragOffset;

    /// <param name="measure">The width of a text in pixels.</param>
    /// <param name="textHeight">The height of a line of text.</param>
    public WindowLayer(Func<string, int> measure, int textHeight)
    {
        _measure = measure;
        _lineHeight = textHeight + 6;
    }

    /// <summary>The open windows, back to front.</summary>
    public IEnumerable<UiWindow> Windows => _open.Select(open => open.Window);

    /// <summary>Whether a dialog is open; pinned windows such as the bottom panel do not count.</summary>
    public bool Any => _open.Any(open => !open.Window.IsPinned);

    /// <summary>The top-left corner of the area windows open in: below the status bar.</summary>
    public Point Origin { get; set; } = new(8, 44);

    /// <summary>Opens a window, or brings the open one of its kind to the front.</summary>
    public void Open(UiWindow window)
    {
        if (_open.FindIndex(open => open.Window.IsSameAs(window)) is >= 0 and var index)
        {
            var existing = _open[index];
            _open.RemoveAt(index);
            _open.Add(existing);
            return;
        }

        var step = _open.Count % 8;
        _open.Add(new OpenWindow(window) { Position = Origin + new Point(step * CascadeStep, step * CascadeStep) });
    }

    /// <summary>Opens the window, or closes it if one of its kind is open already (the dialogs' hotkeys).</summary>
    public void Toggle(UiWindow window)
    {
        if (_open.FindIndex(open => open.Window.IsSameAs(window)) is >= 0 and var index)
        {
            _open.RemoveAt(index);
            return;
        }

        Open(window);
    }

    public void Close(UiWindow window) => _open.RemoveAll(open => ReferenceEquals(open.Window, window));

    /// <summary>Closes the front window (Esc).</summary>
    /// <returns>Whether there was one to close.</returns>
    public bool CloseTop()
    {
        var index = _open.FindLastIndex(open => !open.Window.IsPinned);
        if (index < 0)
        {
            return false;
        }

        _open.RemoveAt(index);
        return true;
    }

    /// <summary>Closes every dialog; pinned windows stay.</summary>
    public void CloseAll() => _open.RemoveAll(open => !open.Window.IsPinned);

    /// <summary>
    /// Lays out the windows from the game's state and handles the mouse: clicks on buttons, dragging title bars,
    /// scrolling.
    /// </summary>
    /// <param name="screen">The area the windows must stay in.</param>
    /// <returns>Whether the mouse is over a window (or dragging one), so that the map should ignore it.</returns>
    public bool Update(IWindowHost host, MouseState mouse, MouseState previous, Rectangle screen)
    {
        _open.RemoveAll(open => open.Window.IsDone(host));
        foreach (var open in _open)
        {
            open.Layout = Lay(open, host, screen);
        }

        var point = new Point(mouse.X, mouse.Y);
        var pressed = mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released;
        if (_dragging is { } dragged)
        {
            if (mouse.LeftButton == ButtonState.Released)
            {
                _dragging = null;
            }
            else
            {
                dragged.Position = point - _dragOffset;
            }

            return true;
        }

        var hit = _open.LastOrDefault(open => open.Layout!.Bounds.Contains(point));
        if (hit is null)
        {
            return false;
        }

        var wheel = mouse.ScrollWheelValue - previous.ScrollWheelValue;
        if (wheel != 0)
        {
            hit.Scroll = Math.Clamp(hit.Scroll - Math.Sign(wheel) * 3, 0, hit.Layout!.MaxScroll);
        }

        if (!pressed)
        {
            return true;
        }

        if (!hit.Window.IsPinned)
        {
            _open.Remove(hit);
            _open.Add(hit);
        }

        var layout = hit.Layout!;
        if (layout.Close.Contains(point))
        {
            _open.Remove(hit);
            return true;
        }

        if (layout.TitleBar.Contains(point))
        {
            _dragging = hit;
            _dragOffset = point - hit.Position;
            return true;
        }

        var clicked = layout.Cells.FirstOrDefault(cell => cell.Cell.OnClick is not null && cell.Area.Contains(point));
        clicked.Cell?.OnClick?.Invoke();
        return true;
    }

    public void Draw(SpriteBatch batch, TextRenderer text, Texture2D pixel, Point mouse)
    {
        foreach (var open in _open)
        {
            if (open.Layout is not { } layout)
            {
                continue;
            }

            batch.Draw(pixel, layout.Bounds, PanelColor);
            if (!open.Window.IsPinned)
            {
                batch.Draw(pixel, layout.TitleBar, TitleColor);
                text.Draw(batch, layout.TitleText, new Vector2(layout.TitleBar.X + Padding, layout.TitleBar.Y + 3));
                batch.Draw(pixel, layout.Close, layout.Close.Contains(mouse) ? ButtonHover : ButtonColor);
                text.Draw(batch, "x", new Vector2(layout.Close.X + ((layout.Close.Width - _measure("x")) / 2), layout.Close.Y + 3));
            }

            foreach (var (area, cell) in layout.Cells)
            {
                if (cell.IsButton)
                {
                    var hover = cell.OnClick is not null && area.Contains(mouse) && (ReferenceEquals(open, _open[^1]) || open.Window.IsPinned);
                    batch.Draw(pixel, area, hover ? ButtonHover : ButtonColor);
                }

                var tint = cell.Dim ? DimText : cell.Highlight ? HighlightText : Color.White;
                var width = _measure(cell.Text);
                var x = cell.IsButton ? area.X + ((area.Width - width) / 2) : cell.Align == CellAlign.Right ? area.Right - width : area.X;
                text.Draw(batch, cell.Text, new Vector2(x, area.Y + 3), tint: tint);
            }

            if (layout.Canvas != Rectangle.Empty)
            {
                open.Window.DrawCanvas(batch, layout.Canvas, pixel, text);
            }

            if (layout.MaxScroll > 0)
            {
                // How far down the table the window shows, as a thin bar on the right.
                var track = layout.Bounds.Height - layout.TitleBar.Height;
                var barHeight = Math.Max(8, track * layout.VisibleRows / (layout.VisibleRows + layout.MaxScroll));
                var barY = layout.Bounds.Y + layout.TitleBar.Height + ((track - barHeight) * open.Scroll / layout.MaxScroll);
                batch.Draw(pixel, new Rectangle(layout.Bounds.Right - 4, barY, 3, barHeight), ButtonHover);
            }
        }
    }

    /// <summary>Lays out a window's table: columns as wide as their widest cell, buttons padded, kept on screen.</summary>
    private WindowLayout Lay(OpenWindow open, IWindowHost host, Rectangle screen)
    {
        var rows = open.Window.Rows(host).ToList();
        var columns = new List<int>();
        var spanWidth = 0;
        foreach (var row in rows)
        {
            var column = 0;
            var used = 0;
            foreach (var cell in row.Cells)
            {
                var cellWidth = CellWidth(cell);
                if (cell.Span)
                {
                    spanWidth = Math.Max(spanWidth, used + cellWidth);
                    break;
                }

                if (column == columns.Count)
                {
                    columns.Add(0);
                }

                columns[column] = Math.Max(columns[column], cellWidth);
                used += columns[column] + ColumnGap;
                column++;
            }
        }

        var pinned = open.Window.IsPinned;
        var titleHeight = pinned ? 0 : _lineHeight;
        var title = open.Window.Hotkey is { } key ? $"{open.Window.Title}  ({key})" : open.Window.Title;
        var tableWidth = columns.Count == 0 ? 0 : columns.Sum() + (ColumnGap * (columns.Count - 1));
        var canvas = open.Window.CanvasSize ?? Point.Zero;
        var width = Math.Max(Math.Max(Math.Max(tableWidth, spanWidth), canvas.X), pinned ? 0 : _measure(title) + _lineHeight + Padding) + (2 * Padding);
        width = pinned ? screen.Width : Math.Min(width, screen.Width);
        var canvasHeight = canvas.Y > 0 ? canvas.Y + Padding : 0;
        var visibleRows = Math.Max(1, Math.Min(rows.Count, ((screen.Height - titleHeight - canvasHeight - (2 * Padding)) / _lineHeight) - 1));
        var height = titleHeight + (visibleRows * _lineHeight) + canvasHeight + (2 * Padding);
        if (pinned)
        {
            open.Position = new Point(screen.X, screen.Bottom - height);
        }

        var maxScroll = Math.Max(0, rows.Count - visibleRows);
        open.Scroll = Math.Clamp(open.Scroll, 0, maxScroll);
        var x = Math.Clamp(open.Position.X, screen.X, Math.Max(screen.X, screen.Right - width));
        var y = Math.Clamp(open.Position.Y, screen.Y, Math.Max(screen.Y, screen.Bottom - height));
        open.Position = new Point(x, y);
        var bounds = new Rectangle(x, y, width, height);
        var titleBar = pinned ? Rectangle.Empty : new Rectangle(x, y, width - _lineHeight, _lineHeight);
        var close = pinned ? Rectangle.Empty : new Rectangle(x + width - _lineHeight, y, _lineHeight, _lineHeight);
        var cells = new List<(Rectangle Area, WindowCell Cell)>();
        for (var i = 0; i < visibleRows && i + open.Scroll < rows.Count; i++)
        {
            var rowY = y + titleHeight + Padding + (i * _lineHeight);
            var cellX = x + Padding;
            var column = 0;
            foreach (var cell in rows[i + open.Scroll].Cells)
            {
                if (cell.Span)
                {
                    cells.Add((new Rectangle(cellX, rowY, bounds.Right - Padding - cellX, _lineHeight - 2), cell));
                    break;
                }

                cells.Add((new Rectangle(cellX, rowY, columns[column], _lineHeight - 2), cell));
                cellX += columns[column] + ColumnGap;
                column++;
            }
        }

        var canvasArea = canvas.Y > 0 ? new Rectangle(x + Padding, bounds.Bottom - Padding - canvas.Y, canvas.X, canvas.Y) : Rectangle.Empty;
        return new WindowLayout(bounds, titleBar, close, title, cells, visibleRows, maxScroll, canvasArea);
    }

    private int CellWidth(WindowCell cell) => _measure(cell.Text) + (cell.IsButton ? 12 : 0);

    private sealed class OpenWindow(UiWindow window)
    {
        public UiWindow Window { get; } = window;

        public Point Position { get; set; }

        public int Scroll { get; set; }

        public WindowLayout? Layout { get; set; }
    }

    private sealed record WindowLayout(
        Rectangle Bounds,
        Rectangle TitleBar,
        Rectangle Close,
        string TitleText,
        IReadOnlyList<(Rectangle Area, WindowCell Cell)> Cells,
        int VisibleRows,
        int MaxScroll,
        Rectangle Canvas);
}
