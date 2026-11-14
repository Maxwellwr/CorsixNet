using CorsixNet.Client.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace CorsixNet.Client.Tests;

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
        Assert.Equal(new Rectangle(320, 752, 640, 48), OriginalBottomPanel.Bounds(viewport));
        Assert.Equal(new Point(320 + 322 + 18, 752 + 24), OriginalBottomPanel.FindButton(viewport, "Hire"));
        Assert.Null(OriginalBottomPanel.FindButton(viewport, "Nothing"));
    }
}
