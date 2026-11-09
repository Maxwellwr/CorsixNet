using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Sim.Tests;

/// <summary>Warmth and happiness day by day (CorsixTH's Humanoid:tickDay and Staff:tickDay).</summary>
public class FeelingsTests
{
    private static readonly RoomDefinition Office = new("office", "Office", 3, 18, new WallSet(82, 83, 90, 91));
    private static readonly RoomDefinition StaffRoom = new("staff_room", "Staff Room", 3, 18, new WallSet(82, 83, 90, 91));

    [Fact]
    public void WarmthMovesAQuarterTowardsTheTileAndComfortCheersUp()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor, new TilePosition(2, 2));
        doctor.Happiness = 0.5m;
        var temperature = simulation.Temperatures[2, 2] / 65535m;

        Feelings.OnEndDay(simulation, doctor);

        // 0.29 × 0.75 + about 0.125 × 0.25, within the comfortable 0.22–0.36; the wage is fair.
        Assert.Equal((0.29m * 0.75m) + (temperature * 0.25m), doctor.Warmth);
        Assert.Equal(0.505m, doctor.Happiness);
    }

    [Fact]
    public void TheColdMakesUnhappy()
    {
        var simulation = NewSimulation();
        var nurse = Hire(simulation, StaffClass.Nurse, new TilePosition(2, 2));
        nurse.Warmth = 0m;
        nurse.Happiness = 0.5m;
        var warmth = simulation.Temperatures[2, 2] / 65535m * 0.25m;

        Feelings.OnEndDay(simulation, nurse);

        Assert.Equal(warmth, nurse.Warmth);
        Assert.Equal(0.5m - (0.1m * (0.22m - warmth)), nurse.Happiness);
    }

    [Fact]
    public void ReceptionistsAndStaffOnTheHighestSalaryAreAlwaysHappy()
    {
        var simulation = NewSimulation();
        var receptionist = Hire(simulation, StaffClass.Receptionist, new TilePosition(2, 2));
        receptionist.Warmth = 0m;
        receptionist.Happiness = 0.5m;
        Feelings.OnEndDay(simulation, receptionist);
        Assert.Equal(1m, receptionist.Happiness);

        var rich = NewSimulation(maxSalary: 1);
        var doctor = Hire(rich, StaffClass.Doctor, new TilePosition(2, 2));
        doctor.Warmth = 0m;
        Feelings.OnEndDay(rich, doctor);
        Assert.Equal(1m, doctor.Happiness);
    }

    [Fact]
    public void StaffLikeSomeObjectsWithinTwoTilesInTheSameRoom()
    {
        var simulation = NewSimulation();
        var handyman = Hire(simulation, StaffClass.Handyman, new TilePosition(4, 4));
        handyman.Happiness = 0.5m;
        Place(simulation, "extinguisher", new TilePosition(6, 6));
        Place(simulation, "bookcase", new TilePosition(2, 5));
        Place(simulation, "plant", new TilePosition(4, 3));
        Place(simulation, "extinguisher", new TilePosition(7, 4)); // Three tiles off.
        simulation.Map[3, 4].RoomId = 9;
        Place(simulation, "bin", new TilePosition(3, 4)); // In a room.

        Feelings.OnEndDay(simulation, handyman);

        // Comfortable 0.005, a healthy plant −0.003 + 0.005, an extinguisher 0.002 and a bookcase 0.003.
        Assert.Equal(0.5m + 0.005m + 0.002m + 0.002m + 0.003m, handyman.Happiness);
    }

    [Fact]
    public void ThoseWithoutAHospitalOnlyFeelTheWarmth()
    {
        var simulation = NewSimulation();
        var wanderer = simulation.AddEntity(id => new Humanoid(id, new TilePosition(1, 1)));
        wanderer.Happiness = 0.5m;

        Feelings.OnEndDay(simulation, wanderer);

        Assert.NotEqual(Humanoid.StartWarmth, wanderer.Warmth);
        Assert.Equal(0.5m, wanderer.Happiness);
    }

    [Fact]
    public void TheDayEndChangesWarmthAndSavesKeepIt()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor, new TilePosition(2, 2));
        while (simulation.Date.Day == 1)
        {
            simulation.Step();
        }

        Assert.NotEqual(Humanoid.StartWarmth, doctor.Warmth);
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(doctor.Warmth, Assert.Single(restored.Entities.OfType<Humanoid>()).Warmth);
    }

    [Theory]
    [InlineData("office", 3, 3, 0, 0)] // The smallest room, no windows.
    [InlineData("office", 6, 6, 0, 1)] // Four times the smallest area: round(ln 4) = 1.
    [InlineData("office", 3, 3, 2, 1)] // round(ln 2) = 1.
    [InlineData("office", 3, 3, 5, 2)] // round(ln 5) = 2.
    [InlineData("staff_room", 3, 3, 3, 2)] // Doubled: round(ln 6) = 2.
    [InlineData("office", 5, 3, 1, 1)] // ln 1 = 0, and 15 / 9: round(ln 1.67) = 1.
    public void ARoomWithWindowsAndSpaceIsNicer(string roomType, int width, int height, int windows, int thousandths)
    {
        var simulation = NewSimulation();
        var room = simulation.AddEntity(id => new Room(id, roomType, new TileRect(1, 1, width, height), [.. Enumerable.Repeat(default(OriginalTile), width * height)]));
        for (var i = 0; i < windows; i++)
        {
            // The external wall's windows along the room's north side; a player's window (128) does not count.
            simulation.Map[1 + i % width, 1 + (i / width)].NorthWall = 124;
        }

        simulation.Map[1, height].WestWall = 128;

        Assert.Equal(thousandths / 1000m, Feelings.RoomHappinessFactor(simulation, room));
    }

    private static Humanoid Hire(Simulation simulation, StaffClass staffClass, TilePosition tile)
    {
        simulation.Schedule(0, new HireStaffCommand(staffClass, 0, tile));
        simulation.Step();
        return simulation.Entities.OfType<Humanoid>().Last();
    }

    private static void Place(Simulation simulation, string objectType, TilePosition tile) =>
        simulation.AddEntity(id => new RoomObject(id, objectType, tile, Direction.South, 0));

    private static Simulation NewSimulation(int maxSalary = LevelRules.DefaultMaxSalary)
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
        return new Simulation(map, seed: 1, content, new LevelRules(10_000, [], [], maxSalary: maxSalary));
    }
}
