// The menu bar at the top of the screen, ported from CorsixTH CorsixTH/Lua/dialogs/menu.lua (UIMenuBar: appearing when
// the cursor nears the top, PullDV's panels, the menus and their items, the clock; makeGameMenu's file, options, charts
// and debug menus).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Client.Ui.Windows;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui;

/// <summary>One item of a menu: its text and what it does.</summary>
internal sealed record MenuBarItem(string Title, Action Act);

/// <summary>One menu of the bar: its title and items.</summary>
internal sealed record MenuBarMenu(string Title, IReadOnlyList<MenuBarItem> Items);

/// <summary>
/// The original's menu bar: hidden until the cursor comes within 22 pixels of the top, then PullDV's 16 pixel bar across
/// the screen with each menu's title and the clock on the right. Pressing a title opens its menu below it; the item let
/// go over is done. The bar goes a while after the cursor leaves it.
/// </summary>
internal sealed class OriginalMenuBar
{
    public const int Height = 16;
    private const int Padding = 6;
    private const int ItemHeight = 14;

    /// <summary>How long the bar stays after the cursor leaves it (disappear_counter's 100 ticks).</summary>
    private const long HideAfter = 100 * 1000 / 18;

    private int? _open;
    private int _hovered = -1;
    private long _hideAt;

    public bool IsVisible { get; private set; }

    /// <summary>The menu open, by its place on the bar, or null.</summary>
    public int? OpenMenu => _open;

    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Sheet("PULLDV", OriginalGraphics.MainPalette, "DATA") is not null
        && graphics.Font("FONT01V", OriginalGraphics.MainPalette) is not null && graphics.Font("FONT02V", OriginalGraphics.MainPalette) is not null;

    /// <summary>The menus' places on the bar: each as wide as its title and 32 more (addMenu).</summary>
    public static List<Rectangle> TitleAreas(IReadOnlyList<MenuBarMenu> menus, Func<string, int> measure)
    {
        var areas = new List<Rectangle>();
        var x = 0;
        foreach (var menu in menus)
        {
            var width = measure(menu.Title) + 32;
            areas.Add(new Rectangle(x, 1, width, Height));
            x += width;
        }

        return areas;
    }

    /// <summary>calculateMenuSize: as wide as the widest item and 10 more (at least 20), 14 a line and 6 more (at least 20).</summary>
    public static Rectangle MenuArea(Rectangle title, MenuBarMenu menu, Func<string, int> measure) =>
        new(title.X, title.Y + Height - 2, Math.Max(20, menu.Items.Select(item => measure(item.Title) + 10).DefaultIfEmpty(0).Max()), Math.Max(20, 6 + (menu.Items.Count * ItemHeight)));

    /// <summary>UIMenu:hitTest: the item under the point, from 0, or -1.</summary>
    public static int ItemAt(Rectangle area, int count, Point at)
    {
        if (!area.Contains(at))
        {
            return -1;
        }

        var index = ((at.Y - area.Y + 12) / ItemHeight) - 1;
        return index >= 0 && index < count ? index : -1;
    }

    /// <summary>
    /// Follows the mouse (onMouseMove, onMouseDown, onMouseUp): shows the bar near the top, opens a menu pressed on its
    /// title, does the item let go over and hides the bar a while after the cursor leaves it.
    /// </summary>
    /// <returns>Whether the bar or an open menu has the mouse, so nothing under it does.</returns>
    public bool Update(IWindowHost host, IReadOnlyList<MenuBarMenu> menus, Func<string, int> measure, MouseState mouse, MouseState previous, long now)
    {
        var at = new Point(mouse.X, mouse.Y);
        var titles = TitleAreas(menus, measure);
        var menuArea = _open is { } open ? MenuArea(titles[open], menus[open], measure) : Rectangle.Empty;
        var nearTop = at.Y < Height + Padding;
        var overMenu = _open is not null && Inflate(menuArea, Padding).Contains(at);
        if (nearTop || overMenu)
        {
            IsVisible = true;
            _hideAt = now + HideAfter;
        }
        else if (IsVisible && now >= _hideAt)
        {
            IsVisible = false;
            _open = null;
        }

        if (!IsVisible)
        {
            return false;
        }

        var titleIndex = at.Y < Height ? titles.FindIndex(area => area.X <= at.X && at.X < area.Right) : -1;
        var hovered = _open is { } shown ? ItemAt(menuArea, menus[shown].Items.Count, at) : -1;
        if (hovered >= 0 && hovered != _hovered)
        {
            host.PlaySound("HLight5.wav");
        }

        _hovered = hovered;
        var pressed = mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released;
        var released = mouse.LeftButton == ButtonState.Released && previous.LeftButton == ButtonState.Pressed;
        if (pressed)
        {
            if (titleIndex >= 0 && titleIndex != _open)
            {
                _open = titleIndex;
                host.PlaySound(WindowLayer.DefaultButtonSound);
            }
            else if (!overMenu && titleIndex < 0)
            {
                _open = null;
            }
        }
        else if (mouse.LeftButton == ButtonState.Pressed && titleIndex >= 0 && _open is not null)
        {
            // Dragging along the bar opens each menu in turn.
            _open = titleIndex;
        }
        else if (released && _open is { } chosen && hovered >= 0)
        {
            var item = menus[chosen].Items[hovered];
            _open = null;
            _hovered = -1;
            item.Act();
        }

        return nearTop || overMenu;
    }

    public void Draw(SpriteBatch batch, IWindowHost host, IReadOnlyList<MenuBarMenu> menus, int screenWidth, DateTime clock)
    {
        if (!IsVisible)
        {
            return;
        }

        var graphics = host.Graphics!;
        var sheet = graphics.Sheet("PULLDV", OriginalGraphics.MainPalette, "DATA")!;
        var white = graphics.Font("FONT01V", OriginalGraphics.MainPalette)!;
        var blue = graphics.Font("FONT02V", OriginalGraphics.MainPalette)!;
        int Measure(string text) => white.Measure(text).Width;
        void Panel(int sprite, int x, int y) => sheet.Draw(batch, sprite, x, y, Color.White);

        // The bar: its left end, middle and right end in three rows (6, 4 and 6 pixels).
        Panel(1, 0, 0);
        Panel(4, 0, 6);
        Panel(7, 0, 10);
        for (var x = 10; x <= screenWidth - 10; x += 10)
        {
            Panel(2, x, 0);
            Panel(5, x, 6);
            Panel(8, x, 10);
        }

        Panel(3, screenWidth - 10, 0);
        Panel(6, screenWidth - 10, 6);
        Panel(9, screenWidth - 10, 10);
        var titles = TitleAreas(menus, Measure);
        for (var i = 0; i < menus.Count; i++)
        {
            white.Draw(batch, menus[i].Title, new Vector2(titles[i].X + ((titles[i].Width - Measure(menus[i].Title)) / 2), titles[i].Y + ((Height - white.LineHeight) / 2)));
        }

        var time = clock.ToString("HH:mm", CultureInfo.InvariantCulture);
        white.Draw(batch, time, new Vector2(screenWidth - 45, 1 + ((Height - white.LineHeight) / 2)));
        if (_open is not { } open)
        {
            return;
        }

        // drawMenu: the menu's panels (calculateMenuSize's render list), its right edge, and the items.
        var menu = menus[open];
        var area = MenuArea(titles[open], menu, Measure);
        Panel(1, area.X, area.Y);
        for (var x = 10; x <= area.Width - 10; x += 10)
        {
            Panel(2, area.X + x, area.Y);
        }

        for (var y = 6; y <= area.Height - 6; y += 4)
        {
            Panel(4, area.X, area.Y + y);
            for (var x = 10; x <= area.Width - 10; x += 10)
            {
                Panel(5, area.X + x, area.Y + y);
            }
        }

        Panel(7, area.X, area.Y + area.Height - 6);
        for (var x = 10; x <= area.Width - 10; x += 10)
        {
            Panel(8, area.X + x, area.Y + area.Height - 6);
        }

        Panel(3, area.Right - 10, area.Y);
        for (var y = 6; y <= area.Height - 6; y += 4)
        {
            Panel(6, area.Right - 10, area.Y + y);
        }

        Panel(9, area.Right - 10, area.Y + area.Height - 6);
        for (var i = 0; i < menu.Items.Count; i++)
        {
            (i == _hovered ? blue : white).Draw(batch, menu.Items[i].Title, new Vector2(area.X, area.Y + 4 + (i * ItemHeight)));
        }
    }

    private static Rectangle Inflate(Rectangle area, int by) => new(area.X - by, area.Y - by, area.Width + (2 * by), area.Height + (2 * by));
}
