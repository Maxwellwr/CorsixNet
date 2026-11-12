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

/// <summary>The training room (CorsixTH's rooms/training.lua, entities/humanoids/staff/doctor.lua's trainSkills).</summary>
public class TrainingRoomTests
{
    private static readonly TileRect Area = new(3, 13, 6, 6);

    [Fact]
    public void AConsultantLecturesAndTheStudentsLearnSkillAndHisSpecialisms()
    {
        var simulation = NewHospital();
        var room = BuildTrainingRoom(simulation);
        var students = new[] { HireStudent(simulation, new(15, 15)), HireStudent(simulation, new(16, 15)) };
        var consultant = HireConsultant(simulation, new(17, 15));
        var skillsBefore = students.Select(s => s.Profile!.Skill).ToArray();

        foreach (var (doctor, index) in students.Select((d, i) => (d, i)).Append((consultant, 2)))
        {
            Run(simulation, new MoveStaffCommand(doctor.Id, new(Area.X + 1 + index, Area.Y + 2)));
        }

        var phases = new HashSet<DoctorPhase>();
        var animations = new HashSet<int>();
        for (var tick = 0; tick < 40 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            phases.Add(((DoctorBehaviour)consultant.Behaviour!).Phase);
            foreach (var doctor in students.Append(consultant))
            {
                if (doctor.ActionAnimation is { } animation)
                {
                    animations.Add(animation.Id);
                }
            }
        }

        Assert.Equal(consultant.Id, room.StaffId);
        Assert.All(students, s => Assert.Equal(DoctorPhase.Studying, ((DoctorBehaviour)s.Behaviour!).Phase));
        Assert.Contains(DoctorPhase.Lecturing, phases);
        Assert.Contains(DoctorPhase.UsingTrainingAid, phases);
        Assert.Contains(2602, animations);
        Assert.Contains(2622, animations);
        Assert.Contains(animations, id => id is 2414 or 2614);

        for (var i = 0; i < students.Length; i++)
        {
            var profile = students[i].Profile!;
            Assert.True(profile.Skill > skillsBefore[i], $"Student {i} did not learn: {skillsBefore[i]} -> {profile.Skill}.");
            Assert.True(profile.SpecialismProgress(Specialism.Surgeon) > 0);
        }

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        StepFor(simulation, 5 * GameDate.HoursPerDay);
        StepFor(loaded, 5 * GameDate.HoursPerDay);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    [Fact]
    public void AStudentWithoutAChairLeavesAndASecondConsultantTakesOverTheLecture()
    {
        var simulation = NewHospital();
        var room = BuildTrainingRoom(simulation);
        var students = new[] { HireStudent(simulation, new(15, 15)), HireStudent(simulation, new(16, 15)), HireStudent(simulation, new(18, 15)) };
        var first = HireConsultant(simulation, new(17, 15));
        var second = HireConsultant(simulation, new(19, 15));
        foreach (var (doctor, index) in students.Select((d, i) => (d, i)))
        {
            Run(simulation, new MoveStaffCommand(doctor.Id, new(Area.X + 1 + index, Area.Y + 2)));
        }

        Run(simulation, new MoveStaffCommand(first.Id, new(Area.X + 1, Area.Y + 3)));
        StepFor(simulation, 2 * GameDate.HoursPerDay);
        Assert.Equal(first.Id, room.StaffId);
        Run(simulation, new MoveStaffCommand(second.Id, new(Area.X + 2, Area.Y + 3)));
        StepFor(simulation, 2 * GameDate.HoursPerDay);

        // Two lecture chairs: the third student leaves; the second consultant sends the first away.
        Assert.Equal(2, students.Count(s => s.Behaviour is DoctorBehaviour { Phase: DoctorPhase.Studying }));
        Assert.Equal(second.Id, room.StaffId);
        Assert.NotEqual(room.Id, ((DoctorBehaviour)first.Behaviour!).RoomId);
    }

    [Fact]
    public void RestedStudentsAndTheirLecturerGoBackToTheTrainingRoom()
    {
        var simulation = NewHospital();
        var room = BuildTrainingRoom(simulation);
        StaffRoomTests.BuildStaffRoom(simulation, new TileRect(3, 3, 6, 6));
        var student = HireStudent(simulation, new(15, 15));
        var consultant = HireConsultant(simulation, new(17, 15));
        Run(simulation, new MoveStaffCommand(student.Id, new(Area.X + 1, Area.Y + 2)));
        Run(simulation, new MoveStaffCommand(consultant.Id, new(Area.X + 2, Area.Y + 3)));

        // Tired enough for the staff room after a day or so in the training room.
        Run(simulation, new SetPolicyCommand(PolicyKind.GoToStaffRoom, 20));
        var rested = new HashSet<int>();
        var returned = new HashSet<int>();
        var tiredMood = false;
        for (var tick = 0; tick < 30 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            foreach (var doctor in new[] { student, consultant })
            {
                tiredMood |= doctor.IsTiredMoodShown;
                if (doctor.Behaviour is RestingBehaviour resting)
                {
                    Assert.Equal(room.Id, resting.ReturnRoomId);
                    rested.Add(doctor.Id);
                }
                else if (rested.Contains(doctor.Id) && doctor.Behaviour is DoctorBehaviour { Phase: DoctorPhase.Studying or DoctorPhase.Lecturing or DoctorPhase.UsingTrainingAid } back)
                {
                    Assert.Equal(room.Id, back.RoomId);
                    returned.Add(doctor.Id);
                }
            }
        }

        Assert.Contains(student.Id, rested);
        Assert.Contains(student.Id, returned);
        Assert.Contains(consultant.Id, returned);

        // Rested at the threshold of 0.02, they never get very tired.
        Assert.False(tiredMood);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    [Fact]
    public void AVeryTiredDoctorShowsTheTiredMoodUntilRested()
    {
        var simulation = NewHospital();
        BuildTrainingRoom(simulation);
        var student = HireStudent(simulation, new(15, 15));
        Run(simulation, new MoveStaffCommand(student.Id, new(Area.X + 1, Area.Y + 2)));

        // No staff room: very tired (0.7) after about 7,800 hours, sped up here by starting tired.
        Run(simulation, new SetPolicyCommand(PolicyKind.GoToStaffRoom, 900));
        typeof(Humanoid).GetProperty(nameof(Humanoid.Fatigue))!.SetValue(student, 0.69m);
        StepFor(simulation, 5 * GameDate.HoursPerDay);
        Assert.True(student.IsTiredMoodShown);
    }

    [Fact]
    public void ADoctorLearnsOnTheJobWhileUsingTheGpsDesk()
    {
        var simulation = NewStaffedHospital(weakDoctor: true);
        var doctor = simulation.Entities.OfType<Humanoid>().Single(h => h.Profile?.Class == StaffClass.Doctor);
        var before = doctor.Profile!.Skill;
        var usingHours = 0;
        for (var tick = 0; tick < 60 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            if (doctor.ActionAnimation is not null && !doctor.IsCrossingDoor)
            {
                usingHours++;
            }
        }

        // 0.000003 of skill an hour at the desk or cabinet: thousandths of skill gained, at most one per 333 hours.
        Assert.True(usingHours > 1000, $"Only {usingHours} hours at the desk.");
        Assert.InRange(doctor.Profile.Skill - before, 1, (usingHours / 333) + 1);
    }

    [Fact]
    public void TheTrainingFactorGrowsWithTheTrainingAids()
    {
        var simulation = NewHospital();
        var room = BuildTrainingRoom(simulation);

        // A projector (10), a skeleton (15) and a bookcase (20): 45 over three kinds or three objects.
        Assert.Equal(15m, TrainingFactorOf(simulation, room));
    }

    private static decimal TrainingFactorOf(Simulation simulation, Room room)
    {
        var method = typeof(Simulation).Assembly.GetType("CorsixNet.Sim.Staff.Training")!
            .GetMethod("TrainingFactor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (decimal)method.Invoke(null, [simulation, room])!;
    }

    private static Humanoid HireStudent(Simulation simulation, TilePosition tile)
    {
        var candidates = simulation.AvailableStaff(StaffClass.Doctor);
        var index = candidates.Select((p, i) => (p, i)).First(c => c.p is { IsConsultant: false, IsSurgeon: false, Skill: < 700 }).i;
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, index, tile));
        return simulation.Entities.OfType<Humanoid>().Last(h => h.Profile?.Class == StaffClass.Doctor);
    }

    private static Humanoid HireConsultant(Simulation simulation, TilePosition tile)
    {
        // The super doctor cheat makes the doctors for hire consultants with every ability.
        Run(simulation, new CheatCommand(CheatKind.ToggleSuperDoctor));
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, tile));
        Run(simulation, new CheatCommand(CheatKind.ToggleSuperDoctor));
        var consultant = simulation.Entities.OfType<Humanoid>().Last(h => h.Profile?.Class == StaffClass.Doctor);
        Assert.True(consultant.Profile!.IsConsultant);
        return consultant;
    }

    internal static Room BuildTrainingRoom(Simulation simulation)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("training", Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "training");
        foreach (var type in new[] { "projector", "lecture_chair", "lecture_chair", "skeleton", "bookcase" })
        {
            Place(simulation, type);
        }

        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
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
