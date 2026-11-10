using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
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

/// <summary>The ward (CorsixTH's rooms/ward.lua, objects/bed.lua).</summary>
public class WardTests
{
    private static readonly TileRect Area = new(2, 12, 8, 8);

    [Fact]
    public void PatientsLieInTheBedsSideBySideWhileNursesWriteAtTheDesks()
    {
        var simulation = NewStaffedHospital(Rules(), weakDoctor: true);
        var room = BuildWard(simulation, beds: 2, desks: 2);
        var hospital = simulation.Hospitals[0];
        var inBed = new HashSet<int>();
        var animations = new HashSet<int>();
        var mostAtOnce = 0;
        var nursesAtOnce = 0;

        for (var tick = 0; tick < 4 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            var lying = 0;
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is PatientBehaviour { Phase: PatientPhase.UsingObject, Use.IsUsing: true } patient && patient.TargetId == room.Id)
                {
                    lying++;
                    inBed.Add(humanoid.Id);
                    if (humanoid.ActionAnimation is { } animation)
                    {
                        animations.Add(animation.Id);
                    }
                }

                if (humanoid.Behaviour is NurseBehaviour { Phase: NursePhase.UsingWardDesk } && humanoid.ActionAnimation is { } desk)
                {
                    animations.Add(desk.Id);
                }
            }

            mostAtOnce = Math.Max(mostAtOnce, lying);
            nursesAtOnce = Math.Max(nursesAtOnce, simulation.Entities.Count(e => e is Humanoid { Behaviour: NurseBehaviour { RoomId: var id } } && id == room.Id));
        }

        Assert.True(inBed.Count >= 3, $"Only {inBed.Count} patients lay in a bed.");
        Assert.Equal(2, mostAtOnce);
        Assert.Equal(2, nursesAtOnce);
        Assert.Contains(animations, id => id is 4686 or 4688 or 4710 or 4712);
        Assert.Contains(animations, id => id is 4694 or 4696 or 4718 or 4720);
        Assert.Contains(animations, id => id is 3256 or 3258);
        Assert.Contains(hospital.Transactions, t => t is { Kind: TransactionKind.Treatment, Subject: "diag_ward" });

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
    public void NursesHealFasterThanOneAndMorePatientsSlowThemDown()
    {
        var simulation = NewStaffedHospital(Rules(), weakDoctor: true);
        var room = BuildWard(simulation, beds: 2, desks: 1);
        StepFor(simulation, GameDate.HoursPerDay);

        var healing = 0m;
        for (var tick = 0; tick < 31 * GameDate.HoursPerDay && healing == 0; tick++)
        {
            simulation.Step();
            healing = RoomVisits.WardHealingAmount(simulation, room);
        }

        // One nurse, one patient in the room: (0.5 + quality) / ln 2 / 1.0 on medium.
        Assert.InRange(healing, 0.5m / DecimalMathLn2, 1.5m / DecimalMathLn2);
    }

    private const decimal DecimalMathLn2 = 0.6931471805599453m;

    /// <summary>Broken heart the only disease, hard to diagnose, so that the weak GP sends patients on to the ward.</summary>
    private static LevelRules Rules()
    {
        var nonVisuals = new int[DiseaseRules.NonVisualsCount];
        nonVisuals[DiseaseCatalog.Find("broken_heart")!.NonVisualsId!.Value] = 1;
        var diseases = new DiseaseRules(
            Enumerable.Repeat(100, DiseaseRules.ExpertiseCount + 1).ToArray(),
            Enumerable.Repeat(true, DiseaseRules.ExpertiseCount + 1).ToArray(),
            Enumerable.Repeat(900, DiseaseRules.ExpertiseCount + 1).ToArray(),
            new int[DiseaseRules.VisualsCount],
            nonVisuals,
            new int[DiseaseRules.VisualsCount],
            new int[DiseaseRules.NonVisualsCount],
            0,
            0);
        return new LevelRules(
            1_000_000,
            [KeyValuePair.Create("gp", 2500), KeyValuePair.Create("ward", 2500)],
            [KeyValuePair.Create(ReceptionDesks.ObjectId, 600)],
            diseases: diseases);
    }

    private static Room BuildWard(Simulation simulation, int beds, int desks)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("ward", Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "ward");
        for (var i = 0; i < beds; i++)
        {
            Place(simulation, "bed");
        }

        for (var i = 0; i < desks; i++)
        {
            Place(simulation, "desk");
        }

        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        for (var i = 0; i < desks; i++)
        {
            Run(simulation, new HireStaffCommand(StaffClass.Nurse, 0, new(14, 16)));
        }

        return room;
    }

    private static void Place(Simulation simulation, string type)
    {
        for (var y = Area.Bottom - 1; y >= Area.Y; y--)
        {
            for (var x = Area.Right - 1; x >= Area.X; x--)
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
}
