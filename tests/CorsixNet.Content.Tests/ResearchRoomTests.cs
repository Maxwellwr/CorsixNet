using CorsixNet.Content.Levels;
using CorsixNet.Formats.Tests;
using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Research;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;
using static CorsixNet.Content.Tests.PatientFlowTests;

namespace CorsixNet.Content.Tests;

/// <summary>The research department (CorsixTH's rooms/research.lua, Doctor:tickDay).</summary>
public class ResearchRoomTests
{
    private static readonly TileRect Area = new(3, 3, 6, 6);

    [Fact]
    public void OnLevelEightAResearcherWorksAtTheDeskAndComputerAndResearchMovesOn()
    {
        var install = RealGameData.Require();
        var rules = LevelRulesLoader.LoadOriginal(install, "LEVEL.L8", BaseGameContent.Create()).WithoutCompetitors();
        var simulation = NewHospital(rules);
        var hospital = simulation.Hospitals[0];
        Assert.True(hospital.IsRoomDiscovered("research"));
        Assert.False(hospital.IsRoomDiscovered("ultrascan"));

        var room = BuildResearchRoom(simulation);
        var researcher = HireResearcher(simulation);
        var used = new HashSet<string>();
        var balance = hospital.Balance;

        for (var tick = 0; tick < 62 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            if (researcher.Behaviour is DoctorBehaviour { Phase: DoctorPhase.UsingResearchObject, ResearchObjectId: var id } && simulation.FindEntity(id) is RoomObject placed)
            {
                used.Add(placed.ObjectType);
                Assert.Equal(room.Id, placed.RoomId);
            }

            Assert.True(researcher.Behaviour is not DoctorBehaviour { Phase: DoctorPhase.ResearchMeandering } || Area.Contains(researcher.Tile));
        }

        Assert.Contains("desk", used);
        Assert.Contains("computer", used);
        var research = hospital.Research;

        // Two months of a researcher's 1,550 to 2,550 points a day, over 5, by the areas' shares.
        var diagnosis = research.Area(ResearchCategory.Diagnosis).Current;
        Assert.Equal(ResearchTargetKind.Object, diagnosis.Kind);
        Assert.True(research.Objects[diagnosis.Id].Points > 2_000, $"Only {research.Objects[diagnosis.Id].Points} points for {diagnosis.Id}.");

        // Research is paid for each month.
        Assert.Contains(hospital.Transactions, t => t is { Kind: TransactionKind.Research, Amount: < 0 });
        Assert.True(hospital.Balance < balance);
    }

    private static Room BuildResearchRoom(Simulation simulation)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("research", Area, new DoorPlacement(new(5, Area.Bottom), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "research");
        foreach (var type in new[] { "autopsy", "desk", "cabinet", "computer" })
        {
            Place(simulation, type);
        }

        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        return room;
    }

    private static void Place(Simulation simulation, string type)
    {
        for (var y = Area.Y; y < Area.Bottom; y++)
        {
            for (var x = Area.X; x < Area.Right; x++)
            {
                foreach (var orientation in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
                {
                    if (ObjectPlacer.Check(simulation, type, new(x, y), orientation, 0) == ObjectPlacementProblem.None)
                    {
                        Run(simulation, new PlaceObjectCommand(type, new(x, y), orientation));
                        return;
                    }
                }
            }
        }

        Assert.Fail($"Found no place for {type}.");
    }

    private static Humanoid HireResearcher(Simulation simulation)
    {
        for (var day = 0; day < 200; day++)
        {
            var doctors = simulation.AvailableStaff(StaffClass.Doctor);
            for (var i = 0; i < doctors.Count; i++)
            {
                if (doctors[i].IsResearcher)
                {
                    Run(simulation, new HireStaffCommand(StaffClass.Doctor, i, new(15, 15)));
                    return simulation.Entities.OfType<Humanoid>().Single(h => h.Profile is { IsResearcher: true });
                }
            }

            StepFor(simulation, GameDate.HoursPerDay);
        }

        Assert.Fail("No researcher came up for hire.");
        return null!;
    }
}
