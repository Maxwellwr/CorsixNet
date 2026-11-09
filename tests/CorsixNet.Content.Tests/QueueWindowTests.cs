using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;
using static CorsixNet.Content.Tests.PatientFlowTests;

namespace CorsixNet.Content.Tests;

/// <summary>The queue window's controls (CorsixTH's dialogs/queue_dialog.lua, Queue:movePatient and the size buttons).</summary>
public class QueueWindowTests
{
    [Fact]
    public void TheQueueWindowResizesQueuesWithinZeroAndThirty()
    {
        var simulation = NewStaffedHospital();
        var desk = Desk(simulation);
        var gp = simulation.Entities.OfType<Room>().Single();
        Assert.Equal(20, desk.Desk!.Queue.MaxSize);

        Run(simulation, new ChangeQueueSizeCommand(desk.Id, 5));
        Assert.Equal(25, desk.Desk.Queue.MaxSize);
        Run(simulation, new ChangeQueueSizeCommand(desk.Id, 10));
        Assert.Equal(30, desk.Desk.Queue.MaxSize);

        var before = gp.DoorQueue!.MaxSize;
        Run(simulation, new ChangeQueueSizeCommand(gp.Id, -100));
        Assert.Equal(0, gp.DoorQueue.MaxSize);
        Assert.True(gp.DoorQueue.IsFull);
        Run(simulation, new ChangeQueueSizeCommand(gp.Id, before));
        Assert.Equal(before, gp.DoorQueue.MaxSize);
    }

    [Fact]
    public void APatientCanBeMovedToTheFrontOfTheQueue()
    {
        var simulation = NewStaffedHospital();
        var queue = Desk(simulation).Desk!.Queue;
        for (var hour = 0; hour < 200 * GameDate.HoursPerDay && queue.Count < 3; hour++)
        {
            simulation.Step();
        }

        Assert.True(queue.Count >= 3, "No queue of three formed at the desk.");
        var last = queue.Members[^1];
        var first = queue.Members[0];

        Run(simulation, new MoveInQueueCommand(last, 0));
        Assert.Equal(last, queue.Members[0]);
        Assert.Equal(first, queue.Members[1]);

        // Out of the queue's range nothing moves.
        var order = queue.Members.ToList();
        Run(simulation, new MoveInQueueCommand(last, queue.Count + 5));
        Assert.Equal(order.Take(queue.Count), queue.Members.Take(order.Count));
    }

    [Fact]
    public void APatientCanBeSentToAnotherRoomOfTheSameType()
    {
        var simulation = NewStaffedHospital();
        var first = simulation.Entities.OfType<Room>().Single();
        var second = BuildSecondGp(simulation);
        var queue = first.DoorQueue!;
        for (var hour = 0; hour < 200 * GameDate.HoursPerDay && queue.Count < 2; hour++)
        {
            simulation.Step();
        }

        Assert.True(queue.Count >= 2, "No queue of two formed at the GP's door.");
        var patient = (Humanoid?)simulation.FindEntity(queue.Members[^1]);
        Assert.NotNull(patient);
        var behaviour = (PatientBehaviour)patient.Behaviour!;

        // Not to the room they already head for.
        Run(simulation, new SendPatientToRoomCommand(patient.Id, first.Id));
        Assert.Equal(first.Id, behaviour.TargetId);

        Run(simulation, new SendPatientToRoomCommand(patient.Id, second.Id));
        Assert.Equal(second.Id, behaviour.TargetId);
        Assert.DoesNotContain(patient.Id, queue.Members);
    }
}
