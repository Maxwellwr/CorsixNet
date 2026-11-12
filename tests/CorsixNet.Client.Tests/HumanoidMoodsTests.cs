using CorsixNet.Client.Maps;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Client.Tests;

/// <summary>Mood icons over humanoids (CorsixTH's humanoid.lua moods and Humanoid:setMood's priorities).</summary>
public class HumanoidMoodsTests
{
    [Fact]
    public void APatientShowsTheMoodOfTheHighestPriority()
    {
        var host = new WindowTestHost();
        var simulation = host.Simulation;
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, new TilePosition(10, 10), simulation.Random, PatientLook.Standard, 0));
        var patient = new PatientBehaviour();
        patient.SetDisease(DiseaseCatalog.Find("uncommon_cold")!, 0, simulation.Random);
        humanoid.Behaviour = patient;
        Assert.Null(HumanoidMoods.Icon(simulation, humanoid));

        patient.IsThirsty = true;
        Assert.Equal(3986, HumanoidMoods.Icon(simulation, humanoid));
        patient.IsNeedingToilet = true;
        Assert.Equal(3996, HumanoidMoods.Icon(simulation, humanoid));
        patient.IsSad = true;
        Assert.Equal(4578, HumanoidMoods.Icon(simulation, humanoid));

        // dying1 to dying5 as health falls.
        patient.Health = 0.2m;
        Assert.Equal(4000, HumanoidMoods.Icon(simulation, humanoid));
        patient.Health = 0.05m;
        Assert.Equal(4008, HumanoidMoods.Icon(simulation, humanoid));

        patient.Health = 0m;
        patient.Die(simulation, humanoid);
        Assert.Equal(4046, HumanoidMoods.Icon(simulation, humanoid));
    }

    [Fact]
    public void APatientSentHomeShowsTheExit()
    {
        var host = new WindowTestHost();
        var simulation = host.Simulation;
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, new TilePosition(10, 10), simulation.Random, PatientLook.Standard, 0));
        var patient = new PatientBehaviour();
        patient.SetDisease(DiseaseCatalog.Find("uncommon_cold")!, 0, simulation.Random);
        humanoid.Behaviour = patient;
        patient.SendHome(simulation, humanoid);
        Assert.Equal(PatientPhase.GoingHome, patient.Phase);
        Assert.Equal(4052, HumanoidMoods.Icon(simulation, humanoid));
    }

    [Fact]
    public void TheGpWaitsForThePatientAndThenHasAnIdeaOrADoubt()
    {
        var host = new WindowTestHost();
        var simulation = host.Simulation;
        var area = new TileRect(4, 4, 4, 4);
        host.Submit(new BuildRoomCommand(new RoomPlan("gp", area, new DoorPlacement(new(5, 4), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>());
        foreach (var type in new[] { "desk", "cabinet", "chair" })
        {
            Place(host, type, area);
        }

        host.Submit(new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        host.Submit(new HireStaffCommand(StaffClass.Doctor, 0, new(5, 2)));
        var doctor = simulation.Entities.OfType<Humanoid>().Single(h => h.Behaviour is DoctorBehaviour);
        for (var tick = 0; tick < 2000 && doctor.Behaviour is not DoctorBehaviour { RoomId: not 0 }; tick++)
        {
            simulation.Step();
        }

        // Called in from the door queue (Room:tryAdvanceQueue), the patient walks in while the doctor waits.
        var patientHumanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, new TilePosition(5, 2), simulation.Random, PatientLook.Standard, 0));
        var patient = new PatientBehaviour();
        patient.SetDisease(DiseaseCatalog.Find("uncommon_cold")!, 0, simulation.Random);
        patientHumanoid.Behaviour = patient;
        room.PatientId = patientHumanoid.Id;
        patient.LetIn(simulation, patientHumanoid, room, (RoomDoor)simulation.FindEntity(room.DoorId)!);
        Assert.Equal(4054, HumanoidMoods.Icon(simulation, doctor));

        var seen = new HashSet<int?>();
        for (var tick = 0; tick < 3000 && patient.Phase != PatientPhase.SeekRoom && patient.Phase != PatientPhase.GoingHome; tick++)
        {
            simulation.Step();
            seen.Add(HumanoidMoods.Icon(simulation, doctor));
        }

        // GPRoom:dealtWithPatient: a bulb once diagnosed, a doubt when sent on to a diagnosis room, until the patient is out.
        Assert.Contains(seen, icon => icon is 4044 or 4020);
        Assert.Null(HumanoidMoods.Icon(simulation, doctor));
    }

    private static void Place(WindowTestHost host, string type, TileRect area)
    {
        for (var y = area.Bottom - 1; y >= area.Y; y--)
        {
            for (var x = area.Right - 1; x >= area.X; x--)
            {
                foreach (var orientation in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
                {
                    if (ObjectPlacer.Check(host.Simulation, type, new(x, y), orientation, 0) == ObjectPlacementProblem.None)
                    {
                        host.Submit(new PlaceObjectCommand(type, new(x, y), orientation));
                        return;
                    }
                }
            }
        }

        Assert.Fail($"Found no place for {type}.");
    }
}
