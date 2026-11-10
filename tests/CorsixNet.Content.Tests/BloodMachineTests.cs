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

/// <summary>The blood machine (CorsixTH's rooms/blood_machine_room.lua, objects/machines/blood_machine.lua).</summary>
public class BloodMachineTests
{
    private static readonly TileRect Area = new(3, 13, 6, 6);

    [Fact]
    public void TheDoctorWorksTheMachineWithThePatientInsideAndTheyGoBackToTheGp()
    {
        // The default rules have every room from the start; a weak GP sends patients on to diagnosis rooms.
        var simulation = NewStaffedHospital(weakDoctor: true);
        var room = BuildBloodMachine(simulation);
        var hospital = simulation.Hospitals[0];
        var tested = new HashSet<int>();
        var hidden = new HashSet<int>();
        var doctorAnimations = new HashSet<int>();
        var loops = 0;

        for (var tick = 0; tick < 4 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is PatientBehaviour { Phase: PatientPhase.OnTrolley, TrolleyUse: { } use } patient && patient.TargetId == room.Id)
                {
                    tested.Add(humanoid.Id);
                    if (humanoid.IsHidden)
                    {
                        hidden.Add(humanoid.Id);
                    }

                    loops = Math.Max(loops, use.InUseRounds);
                }

                if (humanoid.Behaviour is DoctorBehaviour { RoomId: var id } && id == room.Id && humanoid.ActionAnimation is { } animation)
                {
                    doctorAnimations.Add(animation.Id);
                }
            }
        }

        Assert.True(tested.Count >= 2, $"Only {tested.Count} patients were tested.");
        Assert.NotEmpty(hidden);
        Assert.Contains(2566, doctorAnimations);
        Assert.Contains(doctorAnimations, id => id is 2554 or 4614);
        Assert.InRange(loops, 1, 4);
        Assert.Contains(hospital.Transactions, t => t is { Kind: TransactionKind.Treatment, Subject: "diag_blood_machine" });

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        StepFor(simulation, 5 * GameDate.HoursPerDay);
        StepFor(loaded, 5 * GameDate.HoursPerDay);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    private static Room BuildBloodMachine(Simulation simulation)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("blood_machine", Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "blood_machine");
        var placed = false;
        for (var y = Area.Bottom - 1; y >= Area.Y; y--)
        {
            for (var x = Area.Right - 1; x >= Area.X; x--)
            {
                foreach (var orientation in new[] { Direction.North, Direction.East })
                {
                    if (!placed && ObjectPlacer.Check(simulation, "blood_machine", new(x, y), orientation, 0) == ObjectPlacementProblem.None)
                    {
                        Run(simulation, new PlaceObjectCommand("blood_machine", new(x, y), orientation));
                        placed = true;
                    }
                }
            }
        }

        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, new(14, 16)));
        return room;
    }
}
