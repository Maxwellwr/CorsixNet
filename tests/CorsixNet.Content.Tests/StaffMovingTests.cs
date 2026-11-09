using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Serialization;
using CorsixNet.Sim.Staff;
using static CorsixNet.Content.Tests.PatientFlowTests;

namespace CorsixNet.Content.Tests;

/// <summary>Picking up a member of staff and putting them down elsewhere (CorsixTH's pickup action and UIPlaceStaff).</summary>
public class StaffMovingTests
{
    [Fact]
    public void AReceptionistTakenOffHerDeskFreesItAndGoesBackToIt()
    {
        var simulation = NewStaffedHospital();
        var desk = Desk(simulation);
        var receptionist = simulation.Entities.OfType<Humanoid>().Single(h => h.Type == HumanoidType.Receptionist);
        StepFor(simulation, 400);
        Assert.Equal(receptionist.Id, desk.Desk!.ReceptionistId);

        // Her own desk counts as free for her, as CorsixTH frees it when she is picked up.
        Assert.Equal(StaffHireProblem.None, StaffMoving.Check(simulation, receptionist.Id, desk.Tile, 0));

        Run(simulation, new MoveStaffCommand(receptionist.Id, new(3, 20)));

        Assert.NotEqual(receptionist.Id, desk.Desk.ReceptionistId);
        Assert.NotEqual(desk.Tile, receptionist.Tile);

        // Staff:onPlaceInCorridor: she looks for a desk afresh and goes back to the free one.
        StepFor(simulation, 600);
        Assert.Equal(receptionist.Id, desk.Desk.ReceptionistId);
        Assert.Equal(desk.Tile, receptionist.Tile);
    }

    [Fact]
    public void ADoctorTakenOutOfHisRoomLeavesItAndStartsAfresh()
    {
        var simulation = NewStaffedHospital();
        var room = simulation.Entities.OfType<Room>().Single();
        var doctor = simulation.Entities.OfType<Humanoid>().Single(h => h.Type == HumanoidType.Doctor);
        StepFor(simulation, 400);
        Assert.Equal(doctor.Id, room.StaffId);
        Assert.True(room.StaffPresent);

        var target = new TilePosition(18, 18);
        simulation.Schedule(0, new MoveStaffCommand(doctor.Id, target));
        simulation.Step();

        Assert.False(room.StaffPresent);
        Assert.False(doctor.IsHidden);
        Assert.Null(doctor.ActionAnimation);
        Assert.IsType<DoctorBehaviour>(doctor.Behaviour);

        // The room sends for a doctor again, and he is the only one.
        StepFor(simulation, 600);
        Assert.Equal(doctor.Id, room.StaffId);
        Assert.True(room.StaffPresent);
    }

    [Fact]
    public void StaffMayBePutInARoomWhoseDoorTheyCanReach()
    {
        var simulation = NewStaffedHospital();
        var nurse = Hire(simulation, StaffClass.Nurse, new(17, 17));
        var inside = FreeTileInRoom(simulation);

        Assert.Equal(StaffHireProblem.None, StaffMoving.Check(simulation, nurse.Id, inside, 0));
        Run(simulation, new MoveStaffCommand(nurse.Id, inside));

        Assert.Equal(inside, nurse.Tile);
    }

    [Fact]
    public void OnlyTheirOwnStaffCanBeMovedAndOnlyWhereStaffCanBePlaced()
    {
        var simulation = NewStaffedHospital();
        var nurse = Hire(simulation, StaffClass.Nurse, new(17, 17));
        var receptionist = simulation.Entities.OfType<Humanoid>().Single(h => h.Type == HumanoidType.Receptionist);

        Assert.Equal(StaffHireProblem.NoSuchStaff, StaffMoving.Check(simulation, nurse.Id, new(18, 18), 1));
        Assert.Equal(StaffHireProblem.NoSuchStaff, StaffMoving.Check(simulation, -5, new(18, 18), 0));
        Assert.Equal(StaffHireProblem.NoSuchStaff, StaffMoving.Check(simulation, Desk(simulation).Id, new(18, 18), 0));
        Assert.Equal(StaffHireProblem.NotInHospital, StaffMoving.Check(simulation, nurse.Id, new(0, 0), 0));
        Assert.Equal(StaffHireProblem.NoRoomsForReceptionists, StaffMoving.Check(simulation, receptionist.Id, FreeTileInRoom(simulation), 0));

        Run(simulation, new MoveStaffCommand(nurse.Id, new(0, 0)));
        Assert.NotEqual(new TilePosition(0, 0), nurse.Tile);

        Run(simulation, new FireStaffCommand(nurse.Id));
        Assert.Equal(StaffHireProblem.NoSuchStaff, StaffMoving.Check(simulation, nurse.Id, new(18, 18), 0));
    }

    [Fact]
    public void AHandymanKeepsHisParcel()
    {
        var simulation = NewStaffedHospital();
        var handyman = Hire(simulation, StaffClass.Handyman, new(17, 17));
        Run(simulation, new SetHandymanParcelCommand(handyman.Id, 1));

        Run(simulation, new MoveStaffCommand(handyman.Id, new(18, 18)));

        Assert.Equal(1, Assert.IsType<HandymanBehaviour>(handyman.Behaviour).Parcel);
    }

    [Fact]
    public void TheCommandSurvivesTheWire()
    {
        var command = new MoveStaffCommand(42, new(7, 9));
        using var stream = new MemoryStream();
        command.Write(new SimWriter(stream));
        stream.Position = 0;

        Assert.Equal(command, Command.Read(new SimReader(stream)));
    }

    /// <summary>A passable tile of the GP's office, not taken by its objects.</summary>
    private static TilePosition FreeTileInRoom(Simulation simulation)
    {
        var room = simulation.Entities.OfType<Room>().Single();
        for (var y = room.Area.Y; y < room.Area.Bottom; y++)
        {
            for (var x = room.Area.X; x < room.Area.Right; x++)
            {
                if (simulation.Map[x, y].Has(TileFlags.Passable))
                {
                    return new TilePosition(x, y);
                }
            }
        }

        throw new InvalidOperationException("The room has no free tile.");
    }

    private static Humanoid Hire(Simulation simulation, StaffClass staffClass, TilePosition tile)
    {
        Run(simulation, new HireStaffCommand(staffClass, 0, tile));
        return simulation.Entities.OfType<Humanoid>().Last(h => h.Profile?.Class == staffClass);
    }
}
