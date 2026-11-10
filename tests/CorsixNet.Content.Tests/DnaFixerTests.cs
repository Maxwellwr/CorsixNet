using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Disasters;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;
using static CorsixNet.Content.Tests.PatientFlowTests;

namespace CorsixNet.Content.Tests;

/// <summary>
/// Aliens and the DNA fixer (CorsixTH's diseases/alien_dna.lua, rooms/dna_fixer.lua, objects/machines/dna_fixer.lua):
/// they come only by emergency and are turned into standard patients while a researcher works the console.
/// </summary>
public class DnaFixerTests
{
    private static readonly TileRect Area = new(3, 13, 6, 6);
    private static readonly TilePosition Heliport = new(1, 12);

    [Fact]
    public void AliensComeByHelicopterAndAreMorphedIntoStandardPatientsByAResearcher()
    {
        var simulation = NewStaffedHospital(Rules());
        simulation.Map.SetHeliportTile(0, Heliport);
        var room = BuildDnaFixer(simulation);
        var hospital = simulation.Hospitals[0];

        // Nobody walks in with alien DNA: it is for emergencies only.
        StepFor(simulation, 20 * GameDate.HoursPerDay);
        Assert.DoesNotContain(simulation.Entities.OfType<Humanoid>(), h => h.Type is HumanoidType.AlienMalePatient or HumanoidType.AlienFemalePatient);

        Run(simulation, new CheatCommand(CheatKind.Emergency));
        Assert.Equal("alien_dna", hospital.Emergencies.Current!.DiseaseId);
        Run(simulation, new AnswerEmergencyCommand(Accept: true));

        var aliens = new HashSet<int>();
        var animations = new HashSet<int>();
        var morphs = new HashSet<int>();
        var researcherAtConsole = false;
        var limit = (HospitalEmergencies.WatchSegments + 1) * (HospitalEmergencies.WatchTickRate + 1);
        for (var tick = 0; tick < limit && hospital.Emergencies.Current is not null; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Type is HumanoidType.AlienMalePatient or HumanoidType.AlienFemalePatient)
                {
                    aliens.Add(humanoid.Id);
                }

                if (humanoid.Behaviour is DoctorBehaviour { Phase: DoctorPhase.UsingShield } && humanoid.Profile is { IsResearcher: true })
                {
                    researcherAtConsole = true;
                }

                if (humanoid.Behaviour is PatientBehaviour { Use: not null } patient && patient.TargetId == room.Id && humanoid.ActionAnimation is { } animation)
                {
                    animations.Add(animation.Id);
                    if (humanoid.MorphAnimation is { } morph)
                    {
                        morphs.Add(morph.Id);
                        Assert.NotEqual(humanoid.ActionAnimationFlip, humanoid.MorphFlip);
                        Assert.Equal(0, humanoid.HiddenObjectId);
                    }
                }
            }
        }

        Assert.NotEmpty(aliens);
        Assert.True(researcherAtConsole, "The researcher never worked the console.");
        Assert.Contains(3614, animations);
        Assert.Contains(3594, animations);
        Assert.Contains(animations, id => id is 3622 or 1154);
        Assert.Contains(morphs, id => id is 3618 or 1150);
        Assert.True(hospital.Cured > 0, "Nobody was cured.");

        // The cured are standard patients now.
        Assert.DoesNotContain(simulation.Entities.OfType<Humanoid>(), h => h.Behaviour is PatientBehaviour { IsCured: true }
            && h.Type is HumanoidType.AlienMalePatient or HumanoidType.AlienFemalePatient);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        StepFor(simulation, 5 * GameDate.HoursPerDay);
        StepFor(loaded, 5 * GameDate.HoursPerDay);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    [Fact]
    public void OnlyAResearcherWorksInTheDnaFixer()
    {
        var simulation = NewStaffedHospital(Rules());
        var room = BuildDnaFixer(simulation, researcher: false);

        StepFor(simulation, 3 * GameDate.HoursPerDay);
        Assert.Equal(0, room.StaffId);

        Run(simulation, new CheatCommand(CheatKind.ToggleSuperDoctor));
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, new(14, 16)));
        Run(simulation, new CheatCommand(CheatKind.ToggleSuperDoctor));
        StepFor(simulation, 3 * GameDate.HoursPerDay);

        Assert.True(simulation.FindEntity(room.StaffId) is Humanoid { Profile.IsResearcher: true });
    }

    /// <summary>Alien DNA the only disease, known.</summary>
    private static LevelRules Rules()
    {
        var visuals = new int[DiseaseRules.VisualsCount];
        visuals[DiseaseCatalog.Find("alien_dna")!.VisualsId!.Value] = 1;
        var diseases = new DiseaseRules(
            Enumerable.Repeat(100, DiseaseRules.ExpertiseCount + 1).ToArray(),
            Enumerable.Repeat(true, DiseaseRules.ExpertiseCount + 1).ToArray(),
            Enumerable.Repeat(500, DiseaseRules.ExpertiseCount + 1).ToArray(),
            visuals,
            new int[DiseaseRules.NonVisualsCount],
            new int[DiseaseRules.VisualsCount],
            new int[DiseaseRules.NonVisualsCount],
            0,
            0);
        return new LevelRules(
            1_000_000,
            [KeyValuePair.Create("gp", 2500), KeyValuePair.Create("dna_fixer", 2500)],
            [KeyValuePair.Create(ReceptionDesks.ObjectId, 600)],
            diseases: diseases);
    }

    private static Room BuildDnaFixer(Simulation simulation, bool researcher = true)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("dna_fixer", Area, new DoorPlacement(new(6, Area.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "dna_fixer");
        Place(simulation, "dna_fixer");
        Place(simulation, "console");
        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        if (researcher)
        {
            Run(simulation, new CheatCommand(CheatKind.ToggleSuperDoctor));
            Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, new(14, 16)));
            Run(simulation, new CheatCommand(CheatKind.ToggleSuperDoctor));
        }

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
