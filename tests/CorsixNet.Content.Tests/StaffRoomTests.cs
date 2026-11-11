using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Rooms;
using static CorsixNet.Content.Tests.PatientFlowTests;

namespace CorsixNet.Content.Tests;

/// <summary>The staff room (CorsixTH's rooms/staff_room.lua, seek_staffroom.lua, use_staffroom.lua).</summary>
public class StaffRoomTests
{
    private static readonly TileRect Area = new(3, 13, 6, 6);

    [Fact]
    public void TiredStaffRestInTheStaffRoomAndGoBackToWork()
    {
        var simulation = NewStaffedHospital();
        var room = BuildStaffRoom(simulation);

        // Tired enough for the staff room after a few hours of work.
        Run(simulation, new SetPolicyCommand(PolicyKind.GoToStaffRoom, 2));
        var rested = new HashSet<int>();
        var backAtWork = new HashSet<int>();
        var animations = new HashSet<int>();
        var visitorsBefore = room.DoorQueue?.VisitorCount ?? 0;

        for (var tick = 0; tick < 20 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is RestingBehaviour { Phase: RestingPhase.UsingObject })
                {
                    rested.Add(humanoid.Id);
                    if (humanoid.ActionAnimation is { } animation)
                    {
                        animations.Add(animation.Id);
                    }
                }
                else if (rested.Contains(humanoid.Id) && humanoid.Behaviour is DoctorBehaviour or NurseBehaviour or HandymanBehaviour)
                {
                    backAtWork.Add(humanoid.Id);
                }
            }
        }

        Assert.NotEmpty(rested);
        Assert.NotEmpty(backAtWork);
        Assert.Contains(animations, id => id is 2178 or 2180 or 2186 or 2188 or 2208 or 2210 or 1234 or 3700);
        Assert.True((room.DoorQueue?.VisitorCount ?? 0) > visitorsBefore);

        // Receptionists never go.
        Assert.DoesNotContain(simulation.Entities.OfType<Humanoid>(), h => h.Behaviour is RestingBehaviour && h.Profile!.Class == CorsixNet.Sim.Staff.StaffClass.Receptionist);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        StepFor(simulation, 3 * GameDate.HoursPerDay);
        StepFor(loaded, 3 * GameDate.HoursPerDay);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    internal static Room BuildStaffRoom(Simulation simulation) => BuildStaffRoom(simulation, Area);

    internal static Room BuildStaffRoom(Simulation simulation, TileRect area)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("staff_room", area, new DoorPlacement(new(area.X + 2, area.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "staff_room");
        foreach (var type in new[] { "sofa", "pool_table", "video_game" })
        {
            Place(simulation, type, area);
        }

        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        return room;
    }

    private static void Place(Simulation simulation, string type, TileRect area)
    {
        for (var y = area.Bottom - 1; y >= area.Y; y--)
        {
            for (var x = area.Right - 1; x >= area.X; x--)
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
