using System.Globalization;
using System.Text;
using OpenWards.Content.Levels;
using OpenWards.Formats.Tests;
using OpenWards.Sim;
using OpenWards.Sim.Calendar;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Definitions;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Objects;
using OpenWards.Sim.People;
using OpenWards.Sim.Rooms;
using OpenWards.Sim.Staff;

namespace OpenWards.Content.Tests;

/// <summary>
/// An automatic playtest of the twelve campaign levels on the real game data: the hospital opens with a reception desk,
/// every room the level has is built, furnished and confirmed, staff are hired, and the cheats bring VIPs, emergencies,
/// epidemics and earthquakes. Each month the game must save and load to the same state and stay the same for a day,
/// and nothing may throw.
/// </summary>
/// <remarks>
/// The short run (a month a level) is part of the normal suite. The long one (two years a level) carries the
/// <c>Category=LongPlaytest</c> trait and runs only with <see cref="LongVariable"/> set:
/// <c>OPENWARDS_LONG_PLAYTEST=1 dotnet test --filter Category=LongPlaytest</c>.
/// </remarks>
public class CampaignPlaytestTests
{
    /// <summary>Set to run the long playtest.</summary>
    public const string LongVariable = "OPENWARDS_LONG_PLAYTEST";

    private static readonly string[] RoomOrder =
    [
        "gp", "general_diag", "pharmacy", "staff_room", "toilets", "ward", "psych", "cardiogram", "scanner", "ultrascan",
        "blood_machine", "x_ray", "inflation", "slack_tongue", "fracture_clinic", "hair_restoration", "electrolysis",
        "dna_fixer", "jelly_vat", "decontamination", "operating_theatre", "research", "training",
    ];

    private static readonly CheatKind[] Cheats = [CheatKind.Vip, CheatKind.Emergency, CheatKind.Epidemic, CheatKind.Earthquake, CheatKind.CreatePatient];

    public static TheoryData<int> Levels() => [.. Enumerable.Range(1, 12)];

    [Theory]
    [MemberData(nameof(Levels))]
    public void ACampaignLevelPlaysAMonthAndSavesAndLoadsTheSame(int level) => Play(level, months: 1);

    [Theory]
    [MemberData(nameof(Levels))]
    [Trait("Category", "LongPlaytest")]
    public void ACampaignLevelPlaysTwoYearsAndSavesAndLoadsTheSame(int level)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(LongVariable)))
        {
            Assert.Skip($"Set {LongVariable} to run the long playtest.");
        }

        Play(level, months: 24);
    }

    private static void Play(int level, int months)
    {
        var install = RealGameData.Require();
        var name = string.Create(CultureInfo.InvariantCulture, $"LEVEL.L{level}");
        var simulation = LevelMapLoader.LoadOriginal(install, name).StartSimulation((ulong)(1000 + level));
        var problems = new StringBuilder();
        Build(simulation, problems);

        Assert.Contains(simulation.Entities.OfType<Room>(), room => room is { RoomType: "gp", IsActive: true });
        Assert.NotEmpty(ReceptionDesks.OfHospital(simulation, 0, staffedOnly: false));

        for (var day = 0; day < months * 30; day++)
        {
            if (day % 20 == 10)
            {
                Run(simulation, new CheatCommand(Cheats[day / 20 % Cheats.Length]));
            }

            for (var tick = 0; tick < GameDate.HoursPerDay; tick++)
            {
                simulation.Step();
            }

            if (day % 30 == 29)
            {
                CheckSaveAndLoad(simulation, problems, day);
            }
        }

        // The reception desk is staffed by the end, so the rooms did not wall anyone in.
        Assert.NotEmpty(ReceptionDesks.OfHospital(simulation, 0, staffedOnly: true));
        Assert.True(problems.Length == 0, $"{name}:\n{problems}");
    }

    private static void CheckSaveAndLoad(Simulation simulation, StringBuilder problems, int day)
    {
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        if (loaded.ComputeStateHash() != simulation.ComputeStateHash())
        {
            problems.AppendLine(CultureInfo.InvariantCulture, $"day {day}: the loaded game differs from the saved one");
            return;
        }

        for (var tick = 0; tick < GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            loaded.Step();
        }

        if (loaded.ComputeStateHash() != simulation.ComputeStateHash())
        {
            problems.AppendLine(CultureInfo.InvariantCulture, $"day {day}: the loaded game went another way within a day");
        }
    }

    /// <summary>Money, the hospital opened, a reception desk, the level's rooms with what they need, radiators and staff.</summary>
    private static void Build(Simulation simulation, StringBuilder problems)
    {
        for (var i = 0; i < 20; i++)
        {
            Run(simulation, new CheatCommand(CheatKind.Money));
        }

        Run(simulation, new OpenHospitalCommand());
        var start = simulation.Map.GetCameraTile(0);
        PlaceNear(simulation, ReceptionDesks.ObjectId, start, null);

        foreach (var roomId in RoomOrder)
        {
            if (simulation.Content.FindRoom(roomId) is not { } room || !simulation.Rules.IsRoomAvailable(room) || !RoomPurchases.CanBuild(simulation, room, 0)
                || FindSpot(simulation, room, start) is not { } plan)
            {
                continue;
            }

            Run(simulation, new MarkBlueprintCommand(roomId, plan.Area));
            Run(simulation, new BuildRoomCommand(plan));
            for (var tick = 0; tick < 400 && !simulation.Entities.OfType<Room>().Any(r => r.Area == plan.Area); tick++)
            {
                simulation.Step();
            }

            if (simulation.Entities.OfType<Room>().FirstOrDefault(r => r.Area == plan.Area) is not { } built)
            {
                Run(simulation, new UnmarkBlueprintCommand());
                continue;
            }

            var inside = new TilePosition(built.Area.X + 1, built.Area.Y + 1);
            foreach (var (objectId, count) in room.NeededObjects ?? new Dictionary<string, int>())
            {
                for (var i = 0; i < count; i++)
                {
                    if (!PlaceNear(simulation, objectId, inside, built))
                    {
                        problems.AppendLine(CultureInfo.InvariantCulture, $"{roomId}: no place for {objectId}");
                    }
                }
            }

            PlaceNear(simulation, "radiator", inside, built);
            Run(simulation, new ConfirmRoomCommand(built.Id));
        }

        for (var i = 0; i < 6; i++)
        {
            PlaceNear(simulation, "radiator", new TilePosition(start.X + (i * 3) - 6, start.Y + 2), null);
        }

        foreach (var (staffClass, count) in new[] { (StaffClass.Receptionist, 1), (StaffClass.Doctor, 6), (StaffClass.Nurse, 3), (StaffClass.Handyman, 2) })
        {
            for (var i = 0; i < count && simulation.AvailableStaff(staffClass).Count > 0; i++)
            {
                HireNear(simulation, staffClass, start);
            }
        }
    }

    private static void Run(Simulation simulation, Command command)
    {
        simulation.Schedule(0, command);
        simulation.Step();
    }

    /// <summary>Tiles in rings round <paramref name="near"/>, nearest first.</summary>
    private static IEnumerable<TilePosition> Around(TilePosition near, int radiusLimit)
    {
        for (var radius = 0; radius < radiusLimit; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == radius)
                    {
                        yield return new TilePosition(near.X + dx, near.Y + dy);
                    }
                }
            }
        }
    }

    private static bool PlaceNear(Simulation simulation, string objectId, TilePosition near, Room? room)
    {
        if (simulation.Content.FindObject(objectId) is not { } definition)
        {
            return false;
        }

        foreach (var tile in Around(near, 25))
        {
            if (room is not null && !room.Area.Contains(tile))
            {
                continue;
            }

            foreach (var orientation in definition.Orientations.Keys)
            {
                if (ObjectPlacer.Check(simulation, objectId, tile, orientation, 0) == ObjectPlacementProblem.None)
                {
                    var before = simulation.Entities.OfType<RoomObject>().Count();
                    Run(simulation, new PlaceObjectCommand(objectId, tile, orientation));
                    return simulation.Entities.OfType<RoomObject>().Count() > before;
                }
            }
        }

        return false;
    }

    private static void HireNear(Simulation simulation, StaffClass staffClass, TilePosition near)
    {
        foreach (var tile in Around(near, 25))
        {
            if (simulation.Map.Contains(tile.X, tile.Y) && simulation.Map[tile.X, tile.Y].RoomId == 0
                && StaffHiring.Check(simulation, staffClass, 0, tile, 0) == StaffHireProblem.None)
            {
                Run(simulation, new HireStaffCommand(staffClass, 0, tile));
                return;
            }
        }
    }

    /// <summary>The nearest square area a little bigger than the room's smallest, with a door, that may be built.</summary>
    private static RoomPlan? FindSpot(Simulation simulation, RoomDefinition room, TilePosition near)
    {
        var size = room.MinimumSize + 1;
        foreach (var corner in Around(near, 40))
        {
            var area = new TileRect(corner.X, corner.Y, size, size);
            if (RoomBuilder.CheckArea(simulation.Map, room, area, 0) != RoomPlanProblem.None)
            {
                continue;
            }

            for (var i = 0; i < size; i++)
            {
                DoorPlacement[] doors =
                [
                    new(new TilePosition(area.X + i, area.Bottom), EdgeOrientation.North),
                    new(new TilePosition(area.Right, area.Y + i), EdgeOrientation.West),
                    new(new TilePosition(area.X + i, area.Y), EdgeOrientation.North),
                    new(new TilePosition(area.X, area.Y + i), EdgeOrientation.West),
                ];
                foreach (var door in doors)
                {
                    var plan = new RoomPlan(room.Id, area, door);
                    if (RoomBuilder.Check(simulation.Map, simulation.Content, plan, 0) == RoomPlanProblem.None)
                    {
                        return plan;
                    }
                }
            }
        }

        return null;
    }
}
