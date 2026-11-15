using CorsixNet.Client.Ui;
using Microsoft.Xna.Framework;

namespace CorsixNet.Client.Tests;

/// <summary>The main menu's background (CorsixTH's UI:setMenuBackground and UI:draw).</summary>
public class MenuBackgroundTests
{
    [Fact]
    public void TheLargestPictureNoHigherThanTheWindowIsChosenAndCoversTheScreen()
    {
        Assert.Equal((640, 480), MenuBackground.SizeFor(400));
        Assert.Equal((640, 480), MenuBackground.SizeFor(719));
        Assert.Equal((1280, 720), MenuBackground.SizeFor(800));
        Assert.Equal((1920, 1080), MenuBackground.SizeFor(1440));

        // A 1280 by 720 picture on a 1280 by 800 window: scaled by 800/720 to cover it, centred.
        Assert.Equal(new Rectangle(-71, 0, 1423, 800), MenuBackground.Cover(1280, 720, new Rectangle(0, 0, 1280, 800)));
    }

    [Fact]
    public void ThePicturesAreCarriedInTheClient()
    {
        foreach (var (_, height) in MenuBackground.Sizes)
        {
            Assert.NotNull(typeof(MenuBackground).Assembly.GetManifestResourceStream($"CorsixNet.Client.Assets.mainmenu{height}.dat.gz"));
            Assert.NotNull(typeof(MenuBackground).Assembly.GetManifestResourceStream($"CorsixNet.Client.Assets.mainmenu{height}.pal"));
        }
    }
}

/// <summary>The menu bar's submenus and the warmth colours (CorsixTH's menu.lua and th_lua_ui.cpp).</summary>
public class MenuBarSubmenuTests
{
    [Fact]
    public void ASubmenuOpensTenPixelsInFromItsMenusRightLevelWithItsItem()
    {
        var area = OriginalMenuBar.SubmenuArea(new Rectangle(100, 14, 150, 90), 2, [new MenuBarItem("A", () => { }), new MenuBarItem("BB", () => { })], text => text.Length * 10);
        Assert.Equal(new Rectangle(240, 42, 44, 34), area);
    }

    [Fact]
    public void TheThreeWarmthThemesColourTheHeat()
    {
        // 25 °C and above is the hottest, 4 °C and below the coldest.
        Assert.Equal(new Color(255, 0, 70), Maps.MapViewScreen.TemperatureColor(40000, 1));
        Assert.Equal(new Color(0, 0, 70), Maps.MapViewScreen.TemperatureColor(0, 1));
        Assert.Equal(new Color(255, 0, 70), Maps.MapViewScreen.TemperatureColor(40000, 2));
        Assert.Equal(new Color(0, 0, 200), Maps.MapViewScreen.TemperatureColor(0, 2));
        Assert.Equal(new Color(235, 104, 53), Maps.MapViewScreen.TemperatureColor(40000, 3));
        Assert.Equal(new Color(100, 80, 0), Maps.MapViewScreen.TemperatureColor(0, 3));
    }
}
