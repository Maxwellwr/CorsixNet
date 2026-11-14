using CorsixNet.Client.Maps;
using CorsixNet.Client.Ui;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class AdviserFigureTests
{
    private const long Frame = AdviserFigure.FrameMilliseconds;

    [Fact]
    public void HeGetsUpTalksWaitsAndGetsDown()
    {
        var figure = new AdviserFigure();
        figure.Update(0, null);
        Assert.Null(figure.Animation);

        figure.Update(0, "Hello");
        Assert.Equal(AdviserFigure.GetUpAnimation, figure.Animation);
        Assert.Null(figure.Speech);

        figure.Update(3 * Frame, "Hello");
        Assert.Equal(AdviserPhase.Talking, figure.Phase);
        Assert.Equal("Hello", figure.Speech);

        figure.Update(100 * Frame, "Hello");
        Assert.Equal(44, figure.Frame);

        figure.Update(101 * Frame, null);
        Assert.Equal(AdviserPhase.Idle, figure.Phase);
        Assert.Null(figure.Speech);

        figure.Update((101 * Frame) + AdviserFigure.IdleMilliseconds, null);
        Assert.Equal(AdviserFigure.GetDownAnimation, figure.Animation);

        figure.Update((104 * Frame) + AdviserFigure.IdleMilliseconds, null);
        Assert.Equal(AdviserPhase.Hidden, figure.Phase);
    }

    [Fact]
    public void ANewMessageWhileGettingDownBringsHimUpAgain()
    {
        var figure = new AdviserFigure();
        figure.Update(0, "One");
        figure.Update(3 * Frame, "One");
        figure.Hide(4 * Frame);
        Assert.False(figure.IsUp);

        figure.Update(5 * Frame, "Two");
        figure.Update(7 * Frame, "Two");
        Assert.Equal(AdviserPhase.GettingUp, figure.Phase);
        figure.Update(10 * Frame, "Two");
        Assert.Equal("Two", figure.Speech);
    }

    [Fact]
    public void TheBalloonFollowsTheSpeechsLength()
    {
        Assert.Equal(40, AdviserFigure.BalloonWidth("Hi"));
        Assert.Equal(30 / 3 * 7, AdviserFigure.BalloonWidth(new string('a', 30)));
        // Each trailing slash is a line fewer.
        Assert.Equal(30 / 2 * 7, AdviserFigure.BalloonWidth(new string('a', 30) + "/"));
        Assert.Equal(420, AdviserFigure.BalloonWidth(new string('a', 1000)));
    }

    [Fact]
    public void SpeechWrapsByWidth()
    {
        var lines = MapViewScreen.WrapToWidth("one two three four", 9, text => text.Length);
        Assert.Equal(["one two", "three", "four"], lines);
    }
}
