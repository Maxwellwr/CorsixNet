using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.People;

namespace CorsixNet.Client.Tests;

/// <summary>The patient, queue and fax windows.</summary>
public class PatientWindowTests
{
    [Fact]
    public void ThePatientWindowShowsThePatientAndSendsThemHomeAfterAsking()
    {
        var host = new WindowTestHost();
        var humanoid = host.AddPatient();
        var window = new PatientWindow(humanoid.Id);

        var lines = host.Lines(window);
        Assert.Contains("Disease|Not diagnosed yet", lines);
        Assert.Contains(lines, line => line.StartsWith("Health|", StringComparison.Ordinal));
        Assert.False(window.IsDone(host));

        host.Click(window, "Send home");
        Assert.DoesNotContain(host.Submitted, command => command is SendPatientHomeCommand);
        host.Click(window, "Sure? Send home");
        Assert.Equal(new SendPatientHomeCommand(humanoid.Id), host.Submitted[^1]);
        Assert.True(window.IsDone(host));
    }

    [Fact]
    public void TheQueueWindowResizesADesksQueue()
    {
        var host = new WindowTestHost();
        Assert.Equal(ObjectPlacementProblem.None, ObjectPlacer.Check(host.Simulation, ReceptionDesks.ObjectId, new TilePosition(8, 8), Direction.South, 0));
        host.Submit(new PlaceObjectCommand(ReceptionDesks.ObjectId, new TilePosition(8, 8), Direction.South));
        var desk = host.Simulation.Entities.OfType<RoomObject>().Single(o => o.Desk is not null);
        var window = new QueueWindow(desk.Id);
        var size = desk.Desk!.Queue.MaxSize;

        Assert.Contains("Nobody is queueing.", host.Lines(window));
        host.Click(window, "+5");
        Assert.Equal(Math.Min(size + 5, HumanoidQueue.MaxMaxSize), desk.Desk.Queue.MaxSize);
        host.Click(window, "-1");
        Assert.Equal(Math.Min(size + 5, HumanoidQueue.MaxMaxSize) - 1, desk.Desk.Queue.MaxSize);
    }

    [Fact]
    public void TheFaxesSayWhenNothingWaits()
    {
        var host = new WindowTestHost();

        Assert.Empty(FaxWindow.Pending(host.Simulation, 0));
        Assert.Equal("No faxes waiting.", host.Lines(new FaxWindow())[0]);
    }
}
