using Microsoft.Xna.Framework;
using OpenWards.Client.Ui.Windows;
using Xunit;

namespace OpenWards.Client.Tests;

public sealed class QueueScreenTests
{
    [Fact]
    public void ThePatientsStandInARowAndDropsFindTheirPlace()
    {
        Assert.Equal(0, QueueScreen.Spacing(1, false));
        Assert.Equal(138, QueueScreen.Spacing(3, false));
        Assert.Equal(128, QueueScreen.Spacing(3, true));
        Assert.Equal(0, QueueScreen.DropPosition(225, 3));
        Assert.Equal(1, QueueScreen.DropPosition(360, 3));
        Assert.Equal(2, QueueScreen.DropPosition(530, 3));
        Assert.True(QueueScreen.InQueueBox(new Point(300, 50)));
        Assert.False(QueueScreen.InQueueBox(new Point(100, 50)));
    }

    [Fact]
    public void WithoutTheGraphicsTheQueueIsATable() => Assert.False(new QueueWindow(1).IsFullscreen(new WindowTestHost()));
}
