using CorsixNet.Client.Ui.Windows;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class DialogScreensTests
{
    [Fact]
    public void WithoutTheGraphicsTheDialogsAreTables()
    {
        var host = new WindowTestHost();
        Assert.False(new InformationWindow(["Hello"]).IsFullscreen(host));
        Assert.False(new ConfirmWindow("Sure?", () => { }).IsFullscreen(host));
    }

    [Fact]
    public void TheConfirmFrameStartsWithOneMiddlePiece() => Assert.Equal(new Microsoft.Xna.Framework.Point(183, 22 + 11 + 63), new ConfirmScreen().Size);
}
