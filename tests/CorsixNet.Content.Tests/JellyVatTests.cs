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

/// <summary>The jelly vat (CorsixTH's rooms/jelly_vat.lua, objects/machines/jelly_moulder.lua).</summary>
public class JellyVatTests
{
    private static readonly TileRect Area = new(3, 13, 5, 5);

    [Fact]
    public void TheDoctorMouldsJellyPatientsBackIntoShapeAndTheyGoHomeCured()
    {
        var simulation = NewStaffedHospital(Rules());
        var room = BuildJellyVat(simulation);
        Run(simulation, new HireStaffCommand(StaffClass.Handyman, 0, new(14, 15)));
        var hospital = simulation.Hospitals[0];
        var treated = new HashSet<int>();
        var doctorAnimations = new HashSet<int>();
        var seenWhileUsed = false;
        var hiddenWhileUsed = false;
        var moulder = simulation.Entities.OfType<RoomObject>().Single(o => o.ObjectType == "jelly_moulder");
        var waitFacing = moulder.Orientation == Direction.North ? Direction.West : Direction.North;
        var waited = 0;

        for (var tick = 0; tick < 3 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is PatientBehaviour { Phase: PatientPhase.OnTrolley, TrolleyUse: { } use } patient && patient.TargetId == room.Id)
                {
                    treated.Add(humanoid.Id);
                    seenWhileUsed |= !humanoid.IsHidden;
                    hiddenWhileUsed |= humanoid.IsHidden;
                    if (simulation.FindEntity(use.UseWithId) is Humanoid { ActionAnimation: { } animation })
                    {
                        doctorAnimations.Add(animation.Id);
                    }
                }

                if (humanoid.Behaviour is PatientBehaviour { Phase: PatientPhase.AtTrolley } waiting && waiting.TargetId == room.Id && !humanoid.IsMoving)
                {
                    // JellyVatRoom:commandEnteringPatient: waiting for the doctor, facing the moulder.
                    Assert.Equal(waitFacing, humanoid.Facing);
                    waited++;
                }

                if (humanoid.Behaviour is DoctorBehaviour { RoomId: var id } && id == room.Id && humanoid.ActionAnimation is { } doctorAnimation)
                {
                    doctorAnimations.Add(doctorAnimation.Id);
                }
            }
        }

        Assert.True(treated.Count >= 2, $"Only {treated.Count} patients were treated.");
        Assert.True(waited > 0, "Nobody waited for the doctor at the moulder.");

        // The doctor's animations show the patient climbing in and stepping out; the patient waits in view at first.
        Assert.Contains(1382, doctorAnimations);
        Assert.Contains(1322, doctorAnimations);
        Assert.Contains(doctorAnimations, id => id is 1358 or 3982);
        Assert.True(seenWhileUsed);
        Assert.True(hiddenWhileUsed);
        Assert.True(hospital.Cured > 0, "Nobody was cured.");
        Assert.Contains(hospital.Transactions, t => t is { Kind: TransactionKind.Treatment, Subject: "jellyitis" });
        Assert.True(moulder.Machine!.TotalUsage >= treated.Count - 1);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        StepFor(simulation, 5 * GameDate.HoursPerDay);
        StepFor(loaded, 5 * GameDate.HoursPerDay);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    /// <summary>Jellyitis the only disease, known and easy to diagnose, so that the GP sends patients straight on.</summary>
    internal static LevelRules Rules()
    {
        var visuals = new int[DiseaseRules.VisualsCount];
        visuals[DiseaseCatalog.Find("jellyitis")!.VisualsId!.Value] = 1;
        var diseases = new DiseaseRules(
            Enumerable.Repeat(100, DiseaseRules.ExpertiseCount + 1).ToArray(),
            Enumerable.Repeat(true, DiseaseRules.ExpertiseCount + 1).ToArray(),
            new int[DiseaseRules.ExpertiseCount + 1],
            visuals,
            new int[DiseaseRules.NonVisualsCount],
            new int[DiseaseRules.VisualsCount],
            new int[DiseaseRules.NonVisualsCount],
            0,
            0);
        return new LevelRules(
            1_000_000,
            [KeyValuePair.Create("gp", 2500), KeyValuePair.Create("jelly_vat", 2500)],
            [KeyValuePair.Create(ReceptionDesks.ObjectId, 600)],
            diseases: diseases);
    }

    internal static Room BuildJellyVat(Simulation simulation)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("jelly_vat", Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "jelly_vat");
        Place(simulation, "jelly_moulder");
        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, new(14, 16)));
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
