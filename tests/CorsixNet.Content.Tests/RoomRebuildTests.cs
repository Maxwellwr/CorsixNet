using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.Rooms;
using static CorsixNet.Content.Tests.PatientFlowTests;

namespace CorsixNet.Content.Tests;

/// <summary>A finished room taken back to its blueprint while edited (CorsixTH's dialogs/edit_room.lua, returnToDoorPhase).</summary>
public class RoomRebuildTests
{
    [Fact]
    public void ARoomTakenBackToItsBlueprintIsBuiltAgainWithoutPayingForTheRoom()
    {
        var simulation = NewStaffedHospital(InflationTests.Rules());
        var room = InflationTests.BuildInflation(simulation);
        var hospital = simulation.Hospitals[0];
        var inflator = simulation.Entities.OfType<RoomObject>().Single(o => o.ObjectType == "inflator");
        for (var tick = 0; tick < 6 * 31 * GameDate.HoursPerDay && inflator.Machine!.TimesUsed < 2; tick++)
        {
            simulation.Step();
        }

        var (timesUsed, totalUsage) = (inflator.Machine!.TimesUsed, inflator.Machine.TotalUsage);
        Assert.True(timesUsed >= 2, $"Used {timesUsed} times.");
        var inflatorCost = hospital.Research.ObjectCost(simulation.Rules, "inflator");
        var area = room.Area;
        EmptyForEditing(simulation, room);

        var balance = hospital.Balance;
        Run(simulation, new ReturnToBlueprintCommand(room.Id));

        // The room is gone and its objects sold at full price; the hospital remembers the room is paid for.
        Assert.Null(simulation.FindEntity(room.Id));
        Assert.Equal(0, simulation.Map[area.X + 1, area.Y + 1].RoomId);
        Assert.True(hospital.Balance >= balance + inflatorCost);
        Assert.NotNull(hospital.Rebuild);
        Assert.Equal("inflation", hospital.Rebuild.RoomType);

        // Saved and loaded meanwhile.
        simulation = SaveAndLoad(simulation);
        hospital = simulation.Hospitals[0];
        Assert.NotNull(hospital.Rebuild);

        // Built again with the door moved: only its objects are bought again.
        balance = hospital.Balance;
        Run(simulation, new BuildRoomCommand(new RoomPlan("inflation", area, new DoorPlacement(new(area.X + 1, area.Y), EdgeOrientation.North))));
        var rebuilt = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "inflation");
        Assert.True(rebuilt.IsPaid);
        var definition = simulation.Content.FindRoom("inflation")!;
        var objectsCost = RoomPurchases.BuildCost(simulation, hospital, definition) - simulation.Rules.RoomCost("inflation");
        Assert.Equal(balance - objectsCost, hospital.Balance);

        PlaceInflator(simulation, rebuilt);
        var placed = simulation.Entities.OfType<RoomObject>().Single(o => o.ObjectType == "inflator");
        Assert.Equal(timesUsed, placed.Machine!.TimesUsed);
        Assert.Equal(totalUsage, placed.Machine.TotalUsage);

        // Confirming costs nothing more, and the rebuild is over.
        var beforeConfirm = hospital.Balance;
        Run(simulation, new ConfirmRoomCommand(rebuilt.Id));
        Assert.True(rebuilt.IsActive);
        Assert.Equal(beforeConfirm, hospital.Balance);
        Assert.Null(hospital.Rebuild);
    }

    [Fact]
    public void GivingUpTheBlueprintPaysBackHalfTheRoom()
    {
        var simulation = NewStaffedHospital(InflationTests.Rules());
        var room = InflationTests.BuildInflation(simulation);
        var hospital = simulation.Hospitals[0];
        EmptyForEditing(simulation, room);
        Run(simulation, new ReturnToBlueprintCommand(room.Id));
        var balance = hospital.Balance;

        Run(simulation, new AbandonRebuildCommand());

        // UIEditRoom:abortRoom: half the room's cost; its objects were sold already.
        var definition = simulation.Content.FindRoom("inflation")!;
        var expected = (RoomPurchases.BuildCost(simulation, hospital, definition) / 2) - (hospital.Research.ObjectCost(simulation.Rules, "inflator") / 2);
        Assert.Equal(balance + expected, hospital.Balance);
        Assert.Null(hospital.Rebuild);
    }

    private static void EmptyForEditing(Simulation simulation, Room room)
    {
        Run(simulation, new EditRoomCommand(room.Id));
        for (var tick = 0; tick < 2000 && (room.IsActive || room.IsClosing); tick++)
        {
            simulation.Step();
        }

        Assert.False(room.IsActive || room.IsClosing);
    }

    private static void PlaceInflator(Simulation simulation, Room room)
    {
        for (var y = room.Area.Bottom - 1; y >= room.Area.Y; y--)
        {
            for (var x = room.Area.Right - 1; x >= room.Area.X; x--)
            {
                foreach (var orientation in new[] { Direction.North, Direction.East })
                {
                    if (ObjectPlacer.Check(simulation, "inflator", new(x, y), orientation, 0) == ObjectPlacementProblem.None)
                    {
                        Run(simulation, new PlaceObjectCommand("inflator", new(x, y), orientation));
                        return;
                    }
                }
            }
        }

        Assert.Fail("Found no place for the inflator.");
    }

    private static Simulation SaveAndLoad(Simulation simulation)
    {
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        return loaded;
    }
}
