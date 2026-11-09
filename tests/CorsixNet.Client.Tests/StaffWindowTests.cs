using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Client.Tests;

/// <summary>The staff management screen and the staff window.</summary>
public class StaffWindowTests
{
    [Fact]
    public void TheStaffScreenListsAClassAndPaysTheChosenOne()
    {
        var host = new WindowTestHost();
        var doctor = host.Hire(StaffClass.Doctor);
        var nurse = host.Hire(StaffClass.Nurse, 7, 6);
        var window = new StaffManagementWindow();

        var lines = host.Lines(window);
        Assert.StartsWith("Doctors (1)|Nurses (1)|Handymen (0)", lines[0], StringComparison.Ordinal);
        Assert.Contains(lines, line => line.StartsWith($"Doctor {doctor.Id}|", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith($"Nurse {nurse.Id}|", StringComparison.Ordinal));

        host.Click(window, $"Doctor {doctor.Id}");
        var balance = host.Simulation.Hospitals[0].Balance;
        var wage = doctor.Profile!.Wage;
        host.Click(window, $"Bonus ${wage / 10:N0}");
        Assert.Equal(balance - (wage / 10), host.Simulation.Hospitals[0].Balance);

        host.Click(window, $"Raise salary ${wage / 10:N0}");
        Assert.Equal(Math.Min(wage + (wage / 10), host.Simulation.Rules.MaxSalary), doctor.Profile.Wage);
    }

    [Fact]
    public void FiringAsksFirst()
    {
        var host = new WindowTestHost();
        var doctor = host.Hire(StaffClass.Doctor);
        var window = new StaffManagementWindow();
        host.Click(window, $"Doctor {doctor.Id}");

        host.Click(window, "Fire");
        Assert.False(doctor.IsFired);
        host.Click(window, "Sure? Fire");
        Assert.True(doctor.IsFired);
        Assert.Contains(host.Lines(window), line => line == "None hired.");
    }

    [Fact]
    public void TheStaffWindowPicksUpAndChangesAHandymansParcel()
    {
        var host = new WindowTestHost();
        var handyman = host.Hire(StaffClass.Handyman);
        var window = new StaffWindow(handyman.Id);
        Assert.Contains(host.Lines(window), line => line == "Looks after|All parcels|Next parcel");

        host.Click(window, "Next parcel");
        Assert.Equal(1, Assert.IsType<HandymanBehaviour>(handyman.Behaviour).Parcel);
        host.Click(window, "Next parcel");
        Assert.Equal(0, Assert.IsType<HandymanBehaviour>(handyman.Behaviour).Parcel);

        host.Click(window, "Pick up");
        Assert.Equal(handyman.Id, host.PickedUp);
        Assert.False(window.IsDone(host));
        host.Click(window, "Fire");
        host.Click(window, "Sure? Fire");
        Assert.True(window.IsDone(host));
        Assert.Contains(host.Simulation.Hospitals[0].Transactions, t => t.Kind == TransactionKind.Severance);
    }
}
