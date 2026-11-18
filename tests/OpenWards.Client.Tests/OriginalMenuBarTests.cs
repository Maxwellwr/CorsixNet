using Microsoft.Xna.Framework;
using OpenWards.Client.Ui;

namespace OpenWards.Client.Tests;

/// <summary>The menu bar at the top (CorsixTH's dialogs/menu.lua).</summary>
public sealed class OriginalMenuBarTests
{
    private static int Measure(string text) => text.Length * 6;

    [Fact]
    public void MenusSitSideBySideAndItemsAreFourteenPixelsApart()
    {
        var menus = new List<MenuBarMenu>
        {
            new("FILE", [new("LOAD", () => { }), new("SAVE", () => { })]),
            new("CHARTS", [new("TOWN MAP", () => { })]),
        };
        var titles = OriginalMenuBar.TitleAreas(menus, Measure);
        Assert.Equal(new Rectangle(0, 1, 24 + 32, 16), titles[0]);
        Assert.Equal(56, titles[1].X);

        var area = OriginalMenuBar.MenuArea(titles[0], menus[0], Measure);
        Assert.Equal(new Rectangle(0, 15, 48, 34), area);
        Assert.Equal(0, OriginalMenuBar.ItemAt(area, 2, new Point(5, 20)));
        Assert.Equal(1, OriginalMenuBar.ItemAt(area, 2, new Point(5, 34)));
        Assert.Equal(-1, OriginalMenuBar.ItemAt(area, 2, new Point(60, 20)));
    }
}
