using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenWards.Client.Ui;
using Xunit;

namespace OpenWards.Client.Tests;

public sealed class OriginalBottomPanelTests
{
    [Fact]
    public void TheBalanceIsRightAlignedInSevenPlaces()
    {
        Assert.Equal([('9', 44 + 13), ('9', 44 + 26), ('3', 44 + 39), ('8', 44 + 52), ('1', 44 + 65), ('2', 44 + 78)], OriginalBottomPanel.BalanceDigits(993812));
        Assert.Equal([('-', 44 + 65), ('5', 44 + 78)], OriginalBottomPanel.BalanceDigits(-5));
    }

    [Fact]
    public void TheReputationMeterRunsOver65Pixels()
    {
        Assert.Equal(55, OriginalBottomPanel.ReputationX(0));
        Assert.Equal(55 + 32, OriginalBottomPanel.ReputationX(500));
        Assert.Equal(55 + 65, OriginalBottomPanel.ReputationX(1000));
    }

    [Fact]
    public void FaxesHaveTheirKindsIcons()
    {
        Assert.Equal(43, OriginalBottomPanel.FaxIcon("emergency"));
        Assert.Equal(45, OriginalBottomPanel.FaxIcon("epidemy"));
        Assert.Equal(49, OriginalBottomPanel.FaxIcon("personality"));
        Assert.Equal(55, OriginalBottomPanel.FaxIcon("report"));
        Assert.Equal(51, OriginalBottomPanel.FaxIcon("information"));
    }

    [Fact]
    public void ThePanelSitsAtTheBottomMiddleAndScriptsFindItsButtons()
    {
        var viewport = new Viewport(0, 0, 1280, 800);
        Assert.Equal(new Rectangle(302, 752, 676, 48), OriginalBottomPanel.Bounds(viewport));
        Assert.Equal(new Point(302 + 322 + 18, 752 + 24), OriginalBottomPanel.FindButton(viewport, "Hire"));
        Assert.Null(OriginalBottomPanel.FindButton(viewport, "Nothing"));
    }

    [Fact]
    public void TheMachineMenuButtonWidensThePanelOnWideScreensWhenSwitchedOn()
    {
        var viewport = new Viewport(0, 0, 1280, 800);
        Assert.Equal(new Point(302 + 407 + 20, 752 + 24), OriginalBottomPanel.FindButton(viewport, "Machines"));
        Assert.Equal(new Point(302 + 407 + 38 + 18, 752 + 24), OriginalBottomPanel.FindButton(viewport, "Town"));
        Assert.Equal(new Point(302 + 364 + 23, 752 + 24), OriginalBottomPanel.FindButton(viewport, "Staff"));

        var narrow = new Viewport(0, 0, 676, 480);
        Assert.Equal(new Rectangle(18, 432, 640, 48), OriginalBottomPanel.Bounds(narrow));
        Assert.Null(OriginalBottomPanel.FindButton(narrow, "Machines"));

        OriginalBottomPanel.MachineMenuButton = false;
        try
        {
            Assert.Equal(new Rectangle(320, 752, 640, 48), OriginalBottomPanel.Bounds(viewport));
            Assert.Equal(new Point(320 + 407 + 18, 752 + 24), OriginalBottomPanel.FindButton(viewport, "Town"));
        }
        finally
        {
            OriginalBottomPanel.MachineMenuButton = true;
        }
    }

    [Fact]
    public void CorsixThsOwnSpritesHaveTheMachineMenuButton()
    {
        var sheet = AuxSprites.Load();
        Assert.NotNull(sheet);
        Assert.Equal((38, 48), (sheet[22].Width, sheet[22].Height));
        Assert.Equal((38, 48), (sheet[23].Width, sheet[23].Height));
    }

    [Fact]
    public void AFaxIconRisesOutOfTheDoorThenSlidesToItsPlace()
    {
        var drawer = new FaxDrawer();
        drawer.Sync(["a"]);
        Assert.Equal(("a", 175, 4), drawer.Icons[0]);
        for (var i = 0; i < 3; i++)
        {
            drawer.Tick();
        }

        Assert.Equal(("a", 175, -20), drawer.Icons[0]);
        drawer.Tick();
        Assert.Equal(("a", 175, -24), drawer.Icons[0]);
        drawer.Tick();
        Assert.Equal(("a", 172, -24), drawer.Icons[0]);
        for (var i = 0; i < 100; i++)
        {
            drawer.Tick();
        }

        Assert.Equal(("a", 1, -24), drawer.Icons[0]);
        drawer.Sync(["a", "b"]);
        for (var i = 0; i < 100; i++)
        {
            drawer.Tick();
        }

        Assert.Equal(("b", 31, -24), drawer.Icons[1]);
        drawer.Sync(["b"]);
        drawer.Tick();
        Assert.Equal(("b", 28, -24), drawer.Icons[0]);
    }
}
