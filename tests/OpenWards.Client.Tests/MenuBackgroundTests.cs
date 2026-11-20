using Microsoft.Xna.Framework;
using OpenWards.Client.Ui;

namespace OpenWards.Client.Tests;

/// <summary>The main menu's background (CorsixTH's UI:setMenuBackground and UI:draw).</summary>
public class MenuBackgroundTests
{
    [Fact]
    public void TheSmallestPictureNotBlownUpIsChosen()
    {
        Assert.Equal((1280, 720), MenuBackground.SizeFor(new Point(640, 480)));
        Assert.Equal((1280, 720), MenuBackground.SizeFor(new Point(960, 600)));
        Assert.Equal((1280, 720), MenuBackground.SizeFor(new Point(1280, 720)));
        Assert.Equal((1920, 1080), MenuBackground.SizeFor(new Point(1280, 800)));
        Assert.Equal((1920, 1080), MenuBackground.SizeFor(new Point(2560, 1440)));
    }

    [Fact]
    public void ThePictureCoversTheScreenCentred()
    {
        // 16:10: scaled by 800/720 to cover it, centred, a little cut off each side.
        Assert.Equal(new Rectangle(-71, 0, 1423, 800), MenuBackground.Place(1280, 720, new Rectangle(0, 0, 1280, 800)));

        // 16:9: exactly.
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), MenuBackground.Place(1920, 1080, new Rectangle(0, 0, 1920, 1080)));
    }

    [Fact]
    public void OnANarrowScreenThePartToKeepStaysOnIt()
    {
        foreach (var screen in new[] { new Rectangle(0, 0, 640, 480), new Rectangle(0, 0, 1280, 1024), new Rectangle(0, 0, 800, 1200) })
        {
            var picture = MenuBackground.Place(1920, 1080, screen);
            var factor = (double)picture.Width / 1920;
            var keepLeft = picture.X + (MenuBackground.Keep.Left * factor);
            var keepRight = picture.X + (MenuBackground.Keep.Right * factor);
            Assert.InRange(keepLeft, -2, screen.Width);
            Assert.InRange(keepRight, 0, screen.Width + 2);
        }

        // 4:3 still covers: scaled to the height, moved right so that the doctor at the window is in.
        var fourByThree = MenuBackground.Place(1920, 1080, new Rectangle(0, 0, 640, 480));
        Assert.Equal(0, fourByThree.Y);
        Assert.Equal(480, fourByThree.Height);
        Assert.True(fourByThree.X <= 0 && fourByThree.Right >= 640);

        // An ultra-wide window: scaled to the part's height, so that the ward's plate is not cut off; bars at the sides.
        var wide = MenuBackground.Place(1920, 1080, new Rectangle(0, 0, 2560, 1080));
        Assert.True(wide.Y + (MenuBackground.Keep.Top * wide.Height / 1080.0) >= -1, wide.ToString());
        Assert.True(wide.X > 0 && wide.Right < 2560, wide.ToString());

        // A tall window: scaled down to the part's width, bars above and below.
        var tall = MenuBackground.Place(1920, 1080, new Rectangle(0, 0, 800, 1200));
        Assert.True(tall.Y > 0 && tall.Bottom < 1200, tall.ToString());
    }

    [Fact]
    public void TheSignStaysWholeAndClearOfTheMenu()
    {
        var sign = new Point(107, 22);
        var cases = new (Rectangle Screen, Rectangle Menu)[]
        {
            (new Rectangle(0, 0, 1920, 1200), new Rectangle(855, 200, 212, 450)),
            (new Rectangle(0, 0, 1920, 1080), new Rectangle(855, 140, 212, 450)),
            (new Rectangle(0, 0, 960, 600), new Rectangle(374, 48, 212, 450)),
            (new Rectangle(0, 0, 640, 480), new Rectangle(214, 15, 212, 450)),
            (new Rectangle(0, 0, 1280, 1024), new Rectangle(534, 287, 212, 450)),
        };
        foreach (var (screen, menu) in cases)
        {
            var picture = MenuBackground.Place(1920, 1080, screen);
            var (area, cell) = MenuBackground.PlaceSign(picture, screen, menu, sign);
            Assert.True(cell >= 1);
            Assert.Equal(sign.X * cell, area.Width);
            Assert.True(screen.Contains(area), $"{screen}: {area}");
            Assert.False(area.Intersects(menu), $"{screen}: {area} over {menu}");
        }

        // With room, it keeps its place in the picture at the picture's size: 4 pixels a cell on 1920 by 1080.
        var (full, fullCell) = MenuBackground.PlaceSign(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1080), new Rectangle(855, 200, 212, 450), sign);
        Assert.Equal(4, fullCell);
        Assert.Equal(new Rectangle(976, 48, 428, 88), full);
    }

    [Fact]
    public void ThePicturesAreCarriedInTheClient()
    {
        foreach (var (_, height) in MenuBackground.Sizes)
        {
            Assert.NotNull(typeof(MenuBackground).Assembly.GetManifestResourceStream($"OpenWards.Client.Assets.mainmenu{height}.dat.gz"));
            Assert.NotNull(typeof(MenuBackground).Assembly.GetManifestResourceStream($"OpenWards.Client.Assets.mainmenu{height}.pal"));
        }

        Assert.NotNull(typeof(MenuBackground).Assembly.GetManifestResourceStream("OpenWards.Client.Assets.mainmenu_sign.png"));
    }

    [Fact]
    public void TheWindowIconIsWhereMonoGameLooksForIt()
    {
        // SdlGameWindow loads the entry assembly's "Icon.bmp" resource, or falls back to MonoGame's own icon.
        using var icon = typeof(MenuBackground).Assembly.GetManifestResourceStream("Icon.bmp");
        Assert.NotNull(icon);
        Assert.Equal((byte)'B', (byte)icon.ReadByte());
        Assert.Equal((byte)'M', (byte)icon.ReadByte());
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
