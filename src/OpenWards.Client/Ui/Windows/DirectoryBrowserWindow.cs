// Choosing a folder, ported from CorsixTH CorsixTH/Lua/dialogs/resizables/directory_browser.lua (UIDirectoryBrowser opened
// from the game, as folder_settings.lua does: 500 by 423, the instruction at the top, the tree, OK and Cancel) and
// dialogs/tree_ctrl.lua (TreeControl: rows of 14 pixels, 14 pixels in for each level with tree_ctrl's sprites, a click
// on the sprite opening or closing a folder, a click on a row selecting it and a second one choosing it, the scrollbar).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace OpenWards.Client.Ui.Windows;

/// <summary>
/// <c>UIDirectoryBrowser</c>: the folders of the computer as a tree under an instruction; a folder clicked twice, or
/// selected and OK, is chosen and the window closes. Cancel closes it.
/// </summary>
/// <param name="instruction">What to choose a folder for (<c>instruction</c>).</param>
/// <param name="choose">Called with the folder chosen.</param>
/// <param name="tree">The folders; the computer's volumes when null.</param>
internal sealed class DirectoryBrowserWindow(string instruction, Action<string> choose, DirectoryTree? tree = null) : UiWindow
{
    public static readonly Point Size = new(500, 423);

    private const int RowHeight = 14;
    private const int LevelIndent = 14;
    private const int ScrollbarWidth = 20;

    /// <summary>The tree's place (<c>TreeControl(root, 5, 55, 490, 340, ...)</c>).</summary>
    private static readonly Rectangle TreeArea = new(5, 55, 490, 340);

    /// <summary>The scrollbar's slider and the highlighted rows (<c>col_scrollbar</c>).</summary>
    private static readonly Color ScrollbarColour = new(164, 156, 208);

    private readonly DirectoryTree _tree = tree ?? DirectoryTree.ForVolumes();
    private readonly ResizablePanels _panels = new();
    private int _first;
    private bool _pressedInTree;

    /// <summary>The folder selected (<c>selected_node</c>), if any.</summary>
    public DirectoryTree.Node? Selected { get; private set; }

    public DirectoryTree Tree => _tree;

    public override string Title => instruction;

    public override bool UsesResizableLook => true;

    public override bool IsSameAs(UiWindow other) => other is DirectoryBrowserWindow;

    public override Vector2? DefaultPosition => new(0.5f, 0.25f);

    public override Vector2? FullscreenPosition => new(0.5f, 0.25f);

    public override bool IsFullscreen(IWindowHost host) => ResizablePanels.IsAvailable(host);

    public override Point FullscreenSize => Size;

    /// <summary>How many rows the tree shows (<c>num_rows</c>).</summary>
    private static int VisibleRows => (TreeArea.Height - (TreeArea.Height % RowHeight)) / RowHeight;

    private static Rectangle RowArea => new(TreeArea.X, TreeArea.Y, TreeArea.Width - ScrollbarWidth, VisibleRows * RowHeight);

    private List<DialogPanel> Panels(IWindowHost host) =>
    [
        DialogPanel.Button(new Rectangle(130, 400, 100, 18), UiText.Get("install.ok", "OK"), _ => Confirm(host)),
        DialogPanel.Button(new Rectangle(260, 400, 100, 18), UiText.Get("install.cancel", "Cancel"), _ => host.Close(this)),
    ];

    /// <summary>The OK button: the selected folder is chosen.</summary>
    private void Confirm(IWindowHost host)
    {
        if (Selected is { } node)
        {
            Choose(host, node);
        }
    }

    /// <summary><c>select_function</c>: the folder goes to the caller and the window closes.</summary>
    private void Choose(IWindowHost host, DirectoryTree.Node node)
    {
        choose(node.Path);
        host.Close(this);
    }

    /// <summary>
    /// <c>TreeControl:onMouseUp</c> on a row: on its sprite a folder with subfolders opens or closes; on a row already
    /// selected the folder is chosen; on another the row is selected.
    /// </summary>
    public void ClickRow(IWindowHost host, DirectoryTree.Node node, bool onSprite)
    {
        if (onSprite)
        {
            node.Toggle();
            _first = Math.Clamp(_first, 0, Math.Max(0, _tree.Visible().Count - VisibleRows));
        }
        else if (Selected == node)
        {
            Choose(host, node);
        }
        else
        {
            Selected = node;
        }
    }

    /// <summary><c>hitTestTree</c>: the node of the row under <paramref name="local"/>, and whether it is on its sprite.</summary>
    public (DirectoryTree.Node Node, bool OnSprite)? NodeAt(Point local)
    {
        if (!RowArea.Contains(local))
        {
            return null;
        }

        var visible = _tree.Visible();
        var index = _first + ((local.Y - RowArea.Y) / RowHeight);
        if (index >= visible.Count)
        {
            return null;
        }

        var node = visible[index];
        var x = local.X - RowArea.X;
        return x <= node.Level * LevelIndent ? null : (node, x < (node.Level + 1) * LevelIndent);
    }

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        _panels.Draw(batch, host, origin, Size, Panels(host), mouse);
        var look = host.Graphics!.Resizable!;
        var font = ResizablePanels.Font(host);

        // The instruction, wrapped, 15 pixels down.
        var top = origin.Y + 15;
        foreach (var line in Maps.MapViewScreen.WrapToWidth(instruction, Size.X - 10, text => font.Measure(text).Width))
        {
            font.Draw(batch, line, new Vector2(origin.X + 5, top));
            top += font.LineHeight;
        }

        // The tree's lowered panel, its rows, and the scrollbar beside them.
        var area = new Rectangle(origin.X + TreeArea.X, origin.Y + TreeArea.Y, TreeArea.Width, TreeArea.Height);
        look.DrawBevel(batch, area, ResizableLook.PanelDefault, lowered: true);
        var visible = _tree.Visible();
        _first = Math.Clamp(_first, 0, Math.Max(0, visible.Count - VisibleRows));
        var hovered = NodeAt(new Point(mouse.X, mouse.Y) - origin)?.Node;
        for (var i = 0; i < VisibleRows && _first + i < visible.Count; i++)
        {
            var node = visible[_first + i];
            var x = origin.X + RowArea.X;
            var y = origin.Y + RowArea.Y + (i * RowHeight);
            for (var level = 0; level < node.Level; level++)
            {
                look.DrawTreeIcon(batch, 1, x + (level * LevelIndent), y);
            }

            var offset = (node.Level + 1) * LevelIndent;
            if (node == hovered || node == Selected)
            {
                // The highlighted and selected rows in the scrollbar's colour (DirTreeNode:getSelectColour, as every folder is valid).
                look.Fill(batch, new Rectangle(x + offset - 1, y, RowArea.Width - offset - 1, RowHeight), ScrollbarColour);
            }

            look.DrawTreeIcon(batch, !node.HasChildren ? 2 : node.IsExpanded ? 4 : 3, x + (node.Level * LevelIndent), y);
            font.Draw(batch, ResizableLook.ClipLine(node.Label, RowArea.Width - offset - 2, text => font.Measure(text).Width), new Vector2(x + offset, y + 2));
        }

        var bar = new Rectangle(area.Right - ScrollbarWidth, area.Y, ScrollbarWidth, area.Height);
        look.DrawBevel(batch, bar, ResizableLook.PanelDefault, lowered: true);
        if (visible.Count > 0)
        {
            var length = Math.Max(10, bar.Height * Math.Min(VisibleRows, visible.Count) / visible.Count);
            var sliderTop = visible.Count <= VisibleRows ? 0 : (bar.Height - length) * _first / (visible.Count - VisibleRows);
            look.DrawBevel(batch, new Rectangle(bar.X + 1, bar.Y + sliderTop, bar.Width - 2, length), ScrollbarColour, lowered: false);
        }
    }

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous)
    {
        _panels.Mouse(host, Panels(host), local, mouse, previous);
        var count = _tree.Visible().Count;
        if (mouse.ScrollWheelValue != previous.ScrollWheelValue)
        {
            _first = Math.Clamp(_first - (Math.Sign(mouse.ScrollWheelValue - previous.ScrollWheelValue) * 3), 0, Math.Max(0, count - VisibleRows));
        }

        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressedInTree = TreeArea.Contains(local);
        }

        if (mouse.LeftButton != ButtonState.Released || previous.LeftButton != ButtonState.Pressed || !_pressedInTree)
        {
            return;
        }

        _pressedInTree = false;
        var bar = new Rectangle(TreeArea.Right - ScrollbarWidth, TreeArea.Y, ScrollbarWidth, TreeArea.Height);
        if (bar.Contains(local) && count > VisibleRows)
        {
            var slider = bar.Y + ((bar.Height - Math.Max(10, bar.Height * VisibleRows / count)) * _first / (count - VisibleRows));
            _first = Math.Clamp(_first + (local.Y < slider ? -VisibleRows : VisibleRows), 0, count - VisibleRows);
        }
        else if (NodeAt(local) is var (node, onSprite))
        {
            ClickRow(host, node, onSprite);
        }
    }

    public override string? FullscreenTooltip(IWindowHost host, Point local) => ResizablePanels.Tooltip(Panels(host), local);

    public override Point? FindFullscreenButton(IWindowHost host, string text, int nth) => ResizablePanels.FindButton(Panels(host), text, nth);

    /// <summary>Without CorsixTH's sprites: the folders shown as buttons (+ opens, a name selects, chosen when selected), OK and Cancel.</summary>
    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        foreach (var node in _tree.Visible().Skip(_first).Take(VisibleRows))
        {
            var sign = !node.HasChildren ? " " : node.IsExpanded ? "-" : "+";
            yield return WindowRow.Of(
                WindowCell.Button(new string(' ', node.Level * 2) + sign, () => ClickRow(host, node, onSprite: true)),
                WindowCell.Button(node.Label, () => ClickRow(host, node, onSprite: false), highlight: node == Selected));
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(
            WindowCell.Button(UiText.Get("install.ok", "OK"), () => Confirm(host)),
            WindowCell.Button(UiText.Get("install.cancel", "Cancel"), () => host.Close(this)));
    }
}
