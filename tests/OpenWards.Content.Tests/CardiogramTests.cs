using OpenWards.Sim;
using OpenWards.Sim.Calendar;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Objects;
using OpenWards.Sim.People;
using OpenWards.Sim.Rooms;
using OpenWards.Sim.Staff;
using static OpenWards.Content.Tests.PatientFlowTests;

namespace OpenWards.Content.Tests;

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
        var speeds = new HashSet<int>();
        var rounds = new Dictionary<int, int>();
        var roundStarts = new Dictionary<int, long>();

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
                        if (animation.Id is 652 or 2840)
                        {
                            speeds.Add(humanoid.ActionAnimationSpeed);
                            if (roundStarts.GetValueOrDefault(humanoid.Id, -1) != humanoid.ActionAnimationStart)
                            {
                                roundStarts[humanoid.Id] = humanoid.ActionAnimationStart;
                                rounds[humanoid.Id] = rounds.GetValueOrDefault(humanoid.Id) + 1;
                            }
                        }
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

        // cardiogram_cardio_loop_callback: the doctor stands by (1030) between readings, the monitor runs one, two and
        // three frames a tick, and each examination is 31 rounds of the in-use animation (five spells of six, and one).
        Assert.Contains(1030, animations);
        Assert.Equal([1, 2, 3], speeds.Order());
        Assert.Contains(31, rounds.Values);
        Assert.All(rounds.Values, count => Assert.InRange(count, 1, 31));
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
