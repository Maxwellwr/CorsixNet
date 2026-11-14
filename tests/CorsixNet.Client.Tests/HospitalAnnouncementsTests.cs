using CorsixNet.Client.Audio;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Client.Tests;

/// <summary>The hospital's announcements (CorsixTH's callForStaff call_sound, announceCheat).</summary>
public class HospitalAnnouncementsTests
{
    [Fact]
    public void ARoomWithoutItsStaffCallsOnceAndACheatIsAnnounced()
    {
        var host = new WindowTestHost();
        var simulation = host.Simulation;
        host.Submit(new BuildRoomCommand(new RoomPlan("gp", new TileRect(4, 4, 4, 4), new DoorPlacement(new(5, 4), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>());
        var announcements = new HospitalAnnouncements(new Random(1));
        Assert.Empty(announcements.Update(simulation, 0));

        // The office opens without a doctor: the GP call, once.
        room.IsActive = true;
        Assert.Equal(["reqd008.wav"], announcements.Update(simulation, 0));
        Assert.Empty(announcements.Update(simulation, 0));

        simulation.Hospitals[0].Cheated = true;
        Assert.Matches("^cheat00[1-3]\\.wav$", Assert.Single(announcements.Update(simulation, 0)));

        // A cure cheers, a death boos.
        simulation.Hospitals[0].Cured++;
        simulation.Hospitals[0].Deaths++;
        Assert.Equal(["cheer.wav", "boo.wav"], announcements.Update(simulation, 0));

        // A handyman fired says goodbye.
        host.Submit(new HireStaffCommand(StaffClass.Handyman, 0, new(5, 2)));
        announcements.Update(simulation, 0);
        simulation.Entities.OfType<Humanoid>().Single(h => h.Profile is { Class: StaffClass.Handyman }).IsFired = true;
        Assert.Contains("sack006.wav", announcements.Update(simulation, 0));
    }
}
