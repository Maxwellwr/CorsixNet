using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;
using static CorsixNet.Content.Tests.PatientFlowTests;

namespace CorsixNet.Content.Tests;

/// <summary>Rooms calling staff (CorsixTH's calls_dispatcher.lua: verifyStaffForRoom, getPriorityForRoom).</summary>
public class StaffCallTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnIdleDoctorIsCalledAwayToARoomWithPatientsWaitingIfStaffMayMove(bool allowed)
    {
        var simulation = NewStaffedHospital();
        var first = simulation.Entities.OfType<Room>().Single();
        for (var hour = 0; hour < 10 * GameDate.HoursPerDay && !first.StaffPresent; hour++)
        {
            simulation.Step();
        }

        Assert.True(first.StaffPresent);
        var doctor = first.StaffId;
        var second = BuildSecondGp(simulation);
        Run(simulation, new SetPolicyCommand(PolicyKind.StaffAllowedToMove, allowed ? 1 : 0));

        // Every patient sent to the first office goes to the second instead, which has no doctor of its own.
        // Without the policy, the queue is given ten days for the doctor not to come.
        var waited = 0;
        for (var hour = 0; hour < 300 * GameDate.HoursPerDay && !second.StaffPresent && first.PatientId == 0 && waited < 10 * GameDate.HoursPerDay; hour++)
        {
            foreach (var patient in simulation.Entities.OfType<Humanoid>().Where(h => h.Behaviour is PatientBehaviour { TargetId: var target } && target == first.Id).ToList())
            {
                Run(simulation, new SendPatientToRoomCommand(patient.Id, second.Id));
            }

            simulation.Step();
            if (second.DoorQueue is { Count: > 0 })
            {
                waited++;
            }
        }

        Assert.True(second.DoorQueue is { Count: > 0 } || second.StaffPresent, "No patient reached the second office.");
        if (allowed)
        {
            Assert.True(second.StaffPresent, "The doctor stayed in the empty office.");
            Assert.Equal(doctor, second.StaffId);
            Assert.Equal(0, first.StaffId);
        }
        else
        {
            Assert.False(second.StaffPresent);
            Assert.Equal(doctor, first.StaffId);
        }
    }
}
