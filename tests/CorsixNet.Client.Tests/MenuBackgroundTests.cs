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
