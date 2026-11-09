using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Sim.Tests;

/// <summary>Which member of staff a room calls (CorsixTH's CallsDispatcher.getPriorityForRoom).</summary>
public class CallsDispatcherTests
{
    [Fact]
    public void ThePathCountsAndTheTiredAndThoseOutsideRoomsGoFirst()
    {
        var simulation = NewSimulation();
        var room = AddRoom(simulation, new TileRect(1, 1, 3, 3));
        var doctor = Hire(simulation, new TilePosition(6, 2));
        var entrance = new TilePosition(4, 2);

        var fresh = RoomVisits.CallScore(simulation, room, entrance, doctor);
        Assert.Equal(2 - 50, fresh);

        doctor.Fatigue = 0.5m;
        Assert.Equal(2 - 50 - 20, RoomVisits.CallScore(simulation, room, entrance, doctor));
    }

    [Fact]
    public void WaitingPatientsMakeARoomMoreUrgent()
    {
        var simulation = NewSimulation();
        var room = AddRoom(simulation, new TileRect(1, 1, 3, 3));
        var doctor = Hire(simulation, new TilePosition(6, 2));
        var entrance = new TilePosition(4, 2);
        room.DoorQueue = new HumanoidQueue(entrance, entrance, 6);
        var patient = simulation.AddEntity(id => Humanoid.CreatePatient(id, new TilePosition(5, 5), simulation.Random, PatientLook.Standard, 0));
        patient.Behaviour = new PatientBehaviour();
        room.DoorQueue.Push(simulation, patient);

        Assert.Equal(2 - 50 - 5, RoomVisits.CallScore(simulation, room, entrance, doctor));
    }

    [Fact]
    public void NobodyIsCalledWhoCannotGetThere()
    {
        var simulation = NewSimulation();
        var room = AddRoom(simulation, new TileRect(1, 1, 3, 3));
        var doctor = Hire(simulation, new TilePosition(6, 2));
        for (var y = 0; y < simulation.Map.Height; y++)
        {
            simulation.Map[5, y].Set(TileFlags.Passable, false);
        }

        Assert.Null(RoomVisits.CallScore(simulation, room, new TilePosition(4, 2), doctor));
    }

    private static Room AddRoom(Simulation simulation, TileRect area)
    {
        var room = simulation.AddEntity(id => new Room(id, "gp", area, [.. Enumerable.Repeat(default(OriginalTile), area.Width * area.Height)]));
        for (var x = area.X; x < area.Right; x++)
        {
            for (var y = area.Y; y < area.Bottom; y++)
            {
                simulation.Map[x, y].RoomId = room.Id;
            }
        }

        return room;
    }

    private static Humanoid Hire(Simulation simulation, TilePosition tile)
    {
        simulation.Schedule(0, new HireStaffCommand(StaffClass.Doctor, 0, new TilePosition(7, 7)));
        simulation.Step();
        var hired = simulation.Entities.OfType<Humanoid>().Last();
        hired.PlaceAt(tile);
        hired.Fatigue = 0m;
        return hired;
    }

    private static Simulation NewSimulation()
    {
        var map = TestMaps.FromRows("........", "........", "........", "........", "........", "........", "........", "........");
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Parcel = 1;
                map[x, y].Set(TileFlags.Buildable | TileFlags.Hospital, true);
            }
        }

        return new Simulation(map, seed: 1, rules: new Economy.LevelRules(100_000, [], []));
    }
}
