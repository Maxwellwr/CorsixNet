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

/// <summary>The ultrascan (CorsixTH's rooms/ultrascan.lua, objects/machines/ultrascanner.lua).</summary>
public class UltrascanTests
{
    private static readonly TileRect Area = new(3, 13, 5, 5);

    [Fact]
    public void TheDoctorScansPatientsOnTheUltrascannerAndSendsThemBackToTheGp()
    {
        // In the original the ultrascanner is never there from the start: it is researched. Research is not ported, so the
        // default rules, which have every room from the start, stand in for a level where it has been.
        var simulation = NewStaffedHospital(weakDoctor: true);
        var room = BuildUltrascan(simulation);
        var hospital = simulation.Hospitals[0];
        var scanned = new HashSet<int>();
        var backAtGp = new HashSet<int>();
        var animations = new HashSet<int>();

        for (var tick = 0; tick < 4 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is not PatientBehaviour patient)
                {
                    continue;
                }

                // On the machine the patient's animation shows the doctor, who is hidden meanwhile.
                if (patient is { Phase: PatientPhase.OnTrolley, TrolleyUse: { } use } && patient.TargetId == room.Id
                    && simulation.FindEntity(use.UseWithId) is Humanoid { IsHidden: true, Type: HumanoidType.Doctor })
                {
                    scanned.Add(humanoid.Id);
                    if (humanoid.ActionAnimation is { } animation)
                    {
                        animations.Add(animation.Id);
                    }
                }

                if (scanned.Contains(humanoid.Id) && patient is { Seeking: RoomSeek.Gp, Phase: PatientPhase.UsingObject })
                {
                    backAtGp.Add(humanoid.Id);
                }
            }
        }

        Assert.True(scanned.Count >= 2, $"Only {scanned.Count} patients were scanned.");
        Assert.NotEmpty(backAtGp);
        Assert.Contains(animations, id => id is 1560 or 3084);
        Assert.Contains(animations, id => id is 1614 or 3096);
        Assert.True(hospital.Casebook["diag_ultrascan"].Discovered);
        Assert.Contains(hospital.Transactions, t => t is { Kind: TransactionKind.Treatment, Subject: "diag_ultrascan" });

        // The doctor keeps to the room, meandering between patients.
        var doctor = simulation.Entities.OfType<Humanoid>().Single(h => h.Behaviour is DoctorBehaviour { RoomId: var id } && id == room.Id);
        Assert.True(Area.Contains(doctor.Tile), $"The doctor left the ultrascan for {doctor.Tile}.");

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        StepFor(simulation, 5 * GameDate.HoursPerDay);
        StepFor(loaded, 5 * GameDate.HoursPerDay);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    private static Room BuildUltrascan(Simulation simulation)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("ultrascan", Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "ultrascan");
        for (var y = Area.Y; y < Area.Bottom && !simulation.Entities.OfType<RoomObject>().Any(o => o.ObjectType == "ultrascanner"); y++)
        {
            for (var x = Area.X; x < Area.Right; x++)
            {
                if (ObjectPlacer.Check(simulation, "ultrascanner", new(x, y), Direction.North, 0) == ObjectPlacementProblem.None)
                {
                    Run(simulation, new PlaceObjectCommand("ultrascanner", new(x, y), Direction.North));
                    break;
                }
            }
        }

        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, new(14, 16)));
        return room;
    }
}
