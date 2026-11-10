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
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;
using static CorsixNet.Content.Tests.PatientFlowTests;

namespace CorsixNet.Content.Tests;

/// <summary>Autopsies (CorsixTH's rooms/research.lua commandEnteringPatient, objects/autopsy.lua, the fax's research choice).</summary>
public class AutopsyTests
{
    private static readonly TileRect Area = new(3, 13, 6, 6);

    [Fact]
    public void APatientSentToResearchIsAutopsiedAndHelpsResearchTheirTreatment()
    {
        var install = RealGameData.Require();
        var rules = LevelRulesLoader.LoadOriginal(install, "LEVEL.L8", BaseGameContent.Create()).WithoutCompetitors();
        var simulation = NewStaffedHospital(rules);
        var hospital = simulation.Hospitals[0];
        var room = BuildResearchRoom(simulation);

        // Patients guess the cure when the diagnosis rooms run out, so that they ask for the treatment room instead.
        Run(simulation, new SetPolicyCommand(PolicyKind.SendHome, 0));
        Run(simulation, new SetPolicyCommand(PolicyKind.GuessCure, 0));
        Run(simulation, new CheatCommand(CheatKind.ToggleSuperDoctor));
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, new(15, 15)));
        Run(simulation, new CheatCommand(CheatKind.ToggleSuperDoctor));
        var researcher = simulation.Entities.OfType<Humanoid>().Last(h => h.Profile is { IsResearcher: true });

        var sent = new Dictionary<int, string>();
        var autopsied = new List<string>();
        var animations = new HashSet<int>();
        var researchedAgain = false;
        for (var tick = 0; tick < 6 * 31 * GameDate.HoursPerDay && autopsied.Count == 0; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>().ToList())
            {
                if (humanoid.Behaviour is PatientBehaviour { Question: PatientQuestion.NoTreatmentRoom } asking && sent.Count == 0)
                {
                    Assert.True(PatientBehaviour.CanGoToResearch(simulation, humanoid));
                    sent[humanoid.Id] = asking.Disease!.TreatmentRooms[^1];
                    Run(simulation, new AnswerPatientCommand(humanoid.Id, PatientAnswer.Research));
                }
            }

            if (researcher.ActionAnimation is { } animation)
            {
                animations.Add(animation.Id);
            }

            foreach (var (id, treatmentRoom) in sent)
            {
                if (simulation.FindEntity(id) is null && !autopsied.Contains(treatmentRoom))
                {
                    autopsied.Add(treatmentRoom);
                }
            }
        }

        Assert.NotEmpty(sent);
        Assert.Single(autopsied);
        Assert.Contains(2154, animations);
        Assert.Contains(4010, animations);
        Assert.Equal(0, hospital.Deaths);

        // The treatment's room needs an object that got a third of what it needs, unless it was discovered already.
        var treatment = simulation.Content.FindRoom(autopsied[0])!;
        var objectId = treatment.NeededObjects!.Keys.First(id => hospital.Research.Objects.ContainsKey(id));
        var research = hospital.Research.Objects[objectId];
        Assert.True(research.Discovered || research.Points > 0, $"{objectId} got no points.");

        // The researcher goes back to research.
        for (var tick = 0; tick < 3 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            researchedAgain |= researcher.Behaviour is DoctorBehaviour { Phase: DoctorPhase.UsingResearchObject };
        }

        Assert.True(researchedAgain);
        Assert.Equal(0, room.PatientId);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    private static Room BuildResearchRoom(Simulation simulation)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("research", Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "research");
        foreach (var type in new[] { "autopsy", "desk", "cabinet" })
        {
            Place(simulation, type);
        }

        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        return room;
    }

    private static void Place(Simulation simulation, string type)
    {
        for (var y = Area.Bottom - 1; y >= Area.Y; y--)
        {
            for (var x = Area.Right - 1; x >= Area.X; x--)
            {
                foreach (var orientation in new[] { Direction.North, Direction.East })
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
}
