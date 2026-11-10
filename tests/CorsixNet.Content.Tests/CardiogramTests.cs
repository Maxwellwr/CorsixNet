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

/// <summary>The cardiogram (CorsixTH's rooms/cardiogram.lua, objects/machines/cardio.lua).</summary>
public class CardiogramTests
{
    private static readonly TileRect Area = new(3, 13, 6, 6);

    [Fact]
    public void PatientsUndressAreMonitoredWithTheDoctorAndDressAgain()
    {
        // The default rules have every room from the start; a weak GP sends patients on to diagnosis rooms.
        var simulation = NewStaffedHospital(weakDoctor: true);
        var room = BuildCardiogram(simulation);
        var hospital = simulation.Hospitals[0];
        var monitored = new HashSet<int>();
        var animations = new HashSet<int>();
        var kinds = new HashSet<HumanoidType>();

        for (var tick = 0; tick < 4 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is PatientBehaviour { Phase: PatientPhase.OnTrolley, TrolleyUse: not null } patient && patient.TargetId == room.Id)
                {
                    monitored.Add(humanoid.Id);
                    kinds.Add(humanoid.Type);
                    if (humanoid.ActionAnimation is { } animation)
                    {
                        animations.Add(animation.Id);
                    }
                }

                if (humanoid.Behaviour is DoctorBehaviour { RoomId: var id } && id == room.Id && humanoid.ActionAnimation is { } readout)
                {
                    animations.Add(readout.Id);
                }
            }
        }

        Assert.True(monitored.Count >= 2, $"Only {monitored.Count} patients were monitored.");
        Assert.Contains(animations, id => id is 1018 or 3008);
        Assert.Contains(animations, id => id is 652 or 2840);
        Assert.Contains(656, animations);
        Assert.All(kinds, kind => Assert.Contains("Stripped", kind.ToString(), StringComparison.Ordinal));
        Assert.Contains(hospital.Transactions, t => t is { Kind: TransactionKind.Treatment, Subject: "diag_cardiogram" });
        Assert.DoesNotContain(simulation.Entities.OfType<Humanoid>(), h => h.Behaviour is PatientBehaviour { Phase: PatientPhase.GoingHome } && h.Type.ToString().Contains("Stripped", StringComparison.Ordinal));

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        StepFor(simulation, 5 * GameDate.HoursPerDay);
        StepFor(loaded, 5 * GameDate.HoursPerDay);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    private static Room BuildCardiogram(Simulation simulation)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("cardiogram", Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "cardiogram");
        Place(simulation, "cardio", fromBack: false);
        Place(simulation, "screen", fromBack: true);
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
