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

/// <summary>The X-ray (CorsixTH's rooms/x_ray_room.lua, objects/machines/x_ray.lua, radiation_shield.lua).</summary>
public class XRayTests
{
    private static readonly TileRect Area = new(3, 13, 6, 6);

    [Fact]
    public void PatientsAreXRayedWhileTheDoctorSitsAtTheShieldAndGoBackToTheGp()
    {
        // The default rules have every room from the start, as a level where the X-ray has been researched.
        var simulation = NewStaffedHospital(weakDoctor: true);
        var room = BuildXRay(simulation);

        // A handyman keeps the machine repaired; without one it wears out and explodes (Machine:machineUsed).
        Run(simulation, new HireStaffCommand(StaffClass.Handyman, 0, new(14, 15)));
        var hospital = simulation.Hospitals[0];
        var scanned = new HashSet<int>();
        var backAtGp = new HashSet<int>();
        var animations = new HashSet<int>();

        for (var tick = 0; tick < 4 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is DoctorBehaviour { Phase: DoctorPhase.UsingShield } && humanoid.ActionAnimation is { } console)
                {
                    animations.Add(console.Id);
                }

                if (humanoid.Behaviour is not PatientBehaviour patient)
                {
                    continue;
                }

                // In the X-ray the patient has their own animation, the doctor theirs at the console.
                if (patient is { Phase: PatientPhase.OnTrolley, Use: not null } && patient.TargetId == room.Id)
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
        Assert.Contains(animations, id => id is 1964 or 2948);
        Assert.Contains(animations, id => id is 1960 or 2952);
        Assert.Contains(animations, id => id is 798 or 806 or 810 or 814);
        Assert.True(hospital.Casebook["diag_x_ray"].Discovered);
        Assert.Contains(hospital.Transactions, t => t is { Kind: TransactionKind.Treatment, Subject: "diag_x_ray" });

        // The doctor keeps to the room, meandering between patients.
        var doctor = simulation.Entities.OfType<Humanoid>().Single(h => h.Behaviour is DoctorBehaviour { RoomId: var id } && id == room.Id);
        Assert.True(Area.Contains(doctor.Tile), $"The doctor left the X-ray for {doctor.Tile}.");

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        StepFor(simulation, 5 * GameDate.HoursPerDay);
        StepFor(loaded, 5 * GameDate.HoursPerDay);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    private static Room BuildXRay(Simulation simulation)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("x_ray", Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "x_ray");
        Place(simulation, "x_ray", fromBack: true);
        Place(simulation, "radiation_shield", fromBack: false);

        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, new(14, 16)));
        return room;
    }

    private static void Place(Simulation simulation, string type, bool fromBack)
    {
        for (var i = 0; i < Area.Height; i++)
        {
            var y = fromBack ? Area.Y + i : Area.Bottom - 1 - i;
            for (var j = 0; j < Area.Width; j++)
            {
                var x = fromBack ? Area.X + j : Area.Right - 1 - j;
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
