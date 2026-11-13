using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Sim.Tests;

/// <summary>Staff tiring at work (CorsixTH's Staff:tick, isTiring, checkIfNeedRest and Staff:tickDay).</summary>
public class TirednessTests
{
    private static readonly RoomDefinition Office = new("office", "Office", 3, 18, new WallSet(82, 83, 90, 91));
    private static readonly RoomDefinition StaffRoom = new("staff_room", "Staff Room", 3, 18, new WallSet(82, 83, 90, 91));

    [Fact]
    public void OnlyAHandymanTiresInTheCorridor()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor, new TilePosition(6, 6));
        var handyman = Hire(simulation, StaffClass.Handyman, new TilePosition(6, 5));
        handyman.Happiness = 0.5m;

        StaffEmployment.Tick(simulation, doctor);
        StaffEmployment.Tick(simulation, handyman);

        Assert.Equal(0m, doctor.Fatigue);
        Assert.Equal(1m, doctor.Happiness);
        Assert.Equal(0.00009m, handyman.Fatigue);
        Assert.Equal(0.49998m, handyman.Happiness);
    }

    [Fact]
    public void StaffTireInARoomButNotInTheStaffRoom()
    {
        var simulation = NewSimulation();
        AddRoom(simulation, "office", new TileRect(1, 1, 3, 3), active: false);
        AddRoom(simulation, "staff_room", new TileRect(4, 1, 3, 3), active: false);
        var working = Hire(simulation, StaffClass.Doctor, new TilePosition(2, 2));
        var resting = Hire(simulation, StaffClass.Nurse, new TilePosition(5, 2));
        working.Fatigue = 0m; // The doctor worked through the nurse's hiring.

        for (var i = 0; i < 100; i++)
        {
            StaffEmployment.Tick(simulation, working);
            StaffEmployment.Tick(simulation, resting);
        }

        Assert.Equal(0.009m, working.Fatigue);
        Assert.Equal(0m, resting.Fatigue);
    }

    [Fact]
    public void AnOverworkedDoctorGoesCrazyAndComesRoundRestingInTheStaffRoom()
    {
        var simulation = NewSimulation();
        AddRoom(simulation, "staff_room", new TileRect(4, 1, 3, 3), active: false);
        var doctor = Hire(simulation, StaffClass.Doctor, new TilePosition(6, 6));
        var layer = doctor.Layers[Humanoid.StaffLayer];
        doctor.Fatigue = 1m;

        // Doctor:tickDay: one day in 300 a very tired doctor goes crazy.
        for (var day = 0; day < 3000 && !doctor.IsCrazy; day++)
        {
            Tiredness.CheckCrazy(simulation, doctor);
        }

        Assert.True(doctor.IsCrazy);
        Assert.Equal(doctor.Profile!.Layer5 + 4, doctor.Layers[Humanoid.StaffLayer]);

        // Still very tired, they stay crazy; rested in the staff room, they are themselves again.
        doctor.PlaceAt(new TilePosition(5, 2));
        Tiredness.CheckCrazy(simulation, doctor);
        Assert.True(doctor.IsCrazy);
        doctor.Fatigue = 0m;
        Tiredness.CheckCrazy(simulation, doctor);
        Assert.False(doctor.IsCrazy);
        Assert.Equal(layer, doctor.Layers[Humanoid.StaffLayer]);
    }

    [Fact]
    public void FatigueStopsAtOne()
    {
        var simulation = NewSimulation();
        var handyman = Hire(simulation, StaffClass.Handyman, new TilePosition(6, 6));
        handyman.Fatigue = 0.99995m;

        StaffEmployment.Tick(simulation, handyman);

        Assert.Equal(1m, handyman.Fatigue);
    }

    [Fact]
    public void TiredStaffWithoutAStaffRoomGrowUnhappyEveryHour()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor, new TilePosition(6, 6));
        doctor.Fatigue = 0.6m;
        doctor.Happiness = 0.5m;

        StaffEmployment.Tick(simulation, doctor);
        Assert.True(doctor.WaitingForStaffRoom);
        Assert.Equal(0.5m, doctor.Happiness);

        StaffEmployment.Tick(simulation, doctor);
        Assert.Equal(0.499m, doctor.Happiness);

        // Very tired costs a little more.
        doctor.Fatigue = 0.7m;
        StaffEmployment.Tick(simulation, doctor);
        Assert.Equal(0.4978m, doctor.Happiness);
    }

    [Fact]
    public void NobodyWaitsWhenThereIsAStaffRoomAndBuildingOneEndsTheWait()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor, new TilePosition(6, 6));
        doctor.Fatigue = 0.6m;
        doctor.WaitingForStaffRoom = true;

        var staffRoom = AddRoom(simulation, "staff_room", new TileRect(1, 1, 3, 3), active: true);
        Tiredness.OnRoomActivated(simulation, staffRoom);
        Assert.False(doctor.WaitingForStaffRoom);

        StaffEmployment.Tick(simulation, doctor);
        Assert.False(doctor.WaitingForStaffRoom);
        Assert.Equal(1m, doctor.Happiness);
    }

    [Theory]
    [InlineData(0.4, false, 0.5)] // Not tired yet.
    [InlineData(0.5, false, 0.49)] // Getting tired.
    [InlineData(0.6, false, 0.47)] // Should be taking a break.
    [InlineData(0.6, true, 0.506)] // Resting.
    [InlineData(0.7, true, 0.47)] // Resting, but still very tired.
    public void TheDayWeighsWorkingTiredAgainstResting(double fatigue, bool inStaffRoom, double happiness)
    {
        var simulation = NewSimulation();
        AddRoom(simulation, "staff_room", new TileRect(1, 1, 3, 3), active: true);
        var nurse = Hire(simulation, StaffClass.Nurse, inStaffRoom ? new TilePosition(2, 2) : new TilePosition(6, 6));
        nurse.Fatigue = (decimal)fatigue;
        nurse.Happiness = 0.5m;

        Tiredness.OnEndDay(simulation, nurse);

        Assert.Equal((decimal)happiness, nurse.Happiness);
    }

    [Fact]
    public void TheLevelSetsWhenStaffCountAsTired()
    {
        var simulation = NewSimulation(new TirednessRules(300, 400, 500));
        var doctor = Hire(simulation, StaffClass.Doctor, new TilePosition(6, 6));
        doctor.Fatigue = 0.45m;
        doctor.Happiness = 0.5m;

        Assert.True(Tiredness.IsVeryTired(simulation, doctor.Fatigue));
        Assert.False(Tiredness.IsCrackUpTired(simulation, doctor.Fatigue));
        Tiredness.OnEndDay(simulation, doctor);
        Assert.Equal(0.48m, doctor.Happiness);
    }

    [Fact]
    public void SavesKeepFatigueAndTheWait()
    {
        var simulation = NewSimulation(new TirednessRules(300, 400, 500));
        var handyman = Hire(simulation, StaffClass.Handyman, new TilePosition(6, 6));
        handyman.Fatigue = 0.35m;
        for (var i = 0; i < 3; i++)
        {
            simulation.Step();
        }

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        var copy = Assert.Single(restored.Entities.OfType<Humanoid>());
        Assert.Equal(handyman.Fatigue, copy.Fatigue);
        Assert.True(copy.WaitingForStaffRoom);
        Assert.Equal(400, restored.Rules.Tiredness.VeryTired);
    }

    private static Room AddRoom(Simulation simulation, string roomType, TileRect area, bool active)
    {
        var room = simulation.AddEntity(id => new Room(id, roomType, area, [.. Enumerable.Repeat(default(OriginalTile), area.Width * area.Height)]));
        room.IsActive = active;
        for (var x = area.X; x < area.Right; x++)
        {
            for (var y = area.Y; y < area.Bottom; y++)
            {
                simulation.Map[x, y].RoomId = room.Id;
            }
        }

        return room;
    }

    private static Humanoid Hire(Simulation simulation, StaffClass staffClass, TilePosition tile)
    {
        // Hired in the corridor, then put where wanted: staff cannot be hired into a room.
        simulation.Schedule(0, new HireStaffCommand(staffClass, 0, new TilePosition(7, 7)));
        simulation.Step();
        var hired = simulation.Entities.OfType<Humanoid>().Last();
        hired.PlaceAt(tile);
        hired.Fatigue = 0m;
        hired.Happiness = 1m;
        return hired;
    }

    private static Simulation NewSimulation(TirednessRules? tiredness = null)
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

        var content = new GameContent([Office, StaffRoom], [], []);
        return new Simulation(map, seed: 1, content, new LevelRules(10_000, [], [], tiredness: tiredness));
    }
}
