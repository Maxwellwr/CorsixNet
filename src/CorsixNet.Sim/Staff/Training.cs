// Training doctors, ported from CorsixTH CorsixTH/Lua/rooms/training.lua (calculateTrainingFactor, getStaffCount) and
// entities/humanoids/staff/doctor.lua (tickDay, isLearning, trainSkills, _calculateTrainingPerformance, updateSkill).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;

namespace CorsixNet.Sim.Staff;

/// <summary>What doctors learn each day in a training room.</summary>
internal static class Training
{
    /// <summary>The training room's type (rooms/training.lua).</summary>
    internal const string RoomType = "training";

    /// <summary>A student always learns at least this much a day (<c>MIN_LEARN_VALUE</c>).</summary>
    private const decimal MinimumLearning = 0.001m;

    /// <summary>
    /// <c>Doctor:tickDay</c> for each doctor learning (<c>isLearning</c>: a student on a lecture chair of a training room
    /// that has its consultant): they learn from the consultant (<c>trainSkills</c>).
    /// </summary>
    internal static void OnEndDay(Simulation simulation)
    {
        foreach (var entity in simulation.Entities.ToList())
        {
            if (entity is not Humanoid { Behaviour: DoctorBehaviour { Phase: DoctorPhase.Studying } student, Profile: { IsConsultant: false } profile, IsDespawning: false } humanoid
                || simulation.FindEntity(student.RoomId) is not Room { RoomType: RoomType } room
                || simulation.FindEntity(room.StaffId) is not Humanoid { Profile: { } consultant }
                || humanoid.Employer < 0)
            {
                continue;
            }

            // When counting the room's occupancy, the consultant does not count.
            var students = StaffIn(simulation, room) - 1;
            Train(simulation, humanoid, profile, consultant, TrainingFactor(simulation, room), students);
        }
    }

    /// <summary><c>TrainingRoom:getStaffCount</c>: the staff in the room.</summary>
    private static int StaffIn(Simulation simulation, Room room) =>
        simulation.Entities.Count(e => e is Humanoid { Profile: not null, IsDespawning: false } h && room.Area.Contains(h.Tile));

    /// <summary>
    /// <c>calculateTrainingFactor</c>: the projectors, skeletons and bookcases, each kind worth (1 + ln of how many) times its
    /// value; the total over the three kinds or over the objects, whichever is more.
    /// </summary>
    internal static decimal TrainingFactor(Simulation simulation, Room room)
    {
        var rules = simulation.Rules.Staff.Training;
        var factors = new (int Count, int Value)[]
        {
            (Count(simulation, room, "projector"), rules.ProjectorValue),
            (Count(simulation, room, "skeleton"), rules.SkeletonValue),
            (Count(simulation, room, "bookcase"), rules.BookcaseValue),
        };
        var objects = factors.Sum(f => f.Count);
        var value = factors.Sum(f => f.Count > 0 ? (DecimalMath.Ln(f.Count) + 1) * f.Value : 0);
        return objects == 0 ? 0 : Math.Max(value / factors.Length, value / objects);
    }

    private static int Count(Simulation simulation, Room room, string type) =>
        simulation.Entities.Count(e => e is RoomObject { IsDestroyed: false } o && o.RoomId == room.Id && string.Equals(o.ObjectType, type, StringComparison.Ordinal));

    /// <summary>
    /// <c>Doctor:trainSkills</c>: the general skill always grows, by the training rate shared among the skills taught, times
    /// the class's performance; each specialism the consultant has and the student lacks grows by the room's training factor
    /// over the level's threshold for it.
    /// </summary>
    private static void Train(Simulation simulation, Humanoid humanoid, StaffProfile student, StaffProfile consultant, decimal roomFactor, int students)
    {
        var rules = simulation.Rules.Staff;
        var training = rules.Training;
        var taught = Taught(student, consultant).ToList();
        var skillsFactor = 1m / (1 + taught.Count);
        var performance = Performance(simulation, student.AttentionToDetail / (decimal)StaffProfile.MaxSkill, consultant.Skill / (decimal)StaffProfile.MaxSkill, students);

        var wasConsultant = student.IsConsultant;
        student.TrainSkill(Math.Max(training.Rate * skillsFactor * performance / 10000, MinimumLearning), rules);

        foreach (var specialism in taught)
        {
            var threshold = specialism switch
            {
                Specialism.Surgeon => training.SurgeonThreshold,
                Specialism.Psychiatrist => training.PsychiatristThreshold,
                _ => training.ResearcherThreshold,
            };
            var amount = (roomFactor / Math.Max(1, threshold)) * skillsFactor * performance / 12;
            student.TrainSpecialism(specialism, Math.Max(amount, MinimumLearning));
        }

        if (!wasConsultant && student.IsConsultant)
        {
            // updateSkill: a new consultant no longer learns; they leave the training room.
            StaffEmployment.Detach(simulation, humanoid);
            humanoid.Behaviour = StaffHiring.NewBehaviour(student.Class);
            RoomVisits.SettleIdleStaff(simulation, humanoid);
        }

    }

    /// <summary>The specialisms the consultant has and the student lacks (<c>countSkillsTaught</c>).</summary>
    private static IEnumerable<Specialism> Taught(StaffProfile student, StaffProfile consultant)
    {
        foreach (var specialism in new[] { Specialism.Surgeon, Specialism.Psychiatrist, Specialism.Researcher })
        {
            if (consultant.Has(specialism) && !student.Has(specialism))
            {
                yield return specialism;
            }
        }
    }

    /// <summary>
    /// <c>_calculateTrainingPerformance</c>: the average of the student's attention to detail and the consultant's skill
    /// squared, each turned into 90% to 110%, and a random 90% to 110%; with three students or more, times 2 over one less
    /// than the class.
    /// </summary>
    internal static decimal Performance(Simulation simulation, decimal attention, decimal consultantSkill, int students)
    {
        static decimal Clamp(decimal value) => 0.9m + (value * 0.2m);
        var impact = (Clamp(attention) + Clamp(consultantSkill * consultantSkill) + VipVisits.TriangularRandom(simulation.Random, 0.9m, 1m, 1.1m)) / 3;
        var classImpact = students >= 3 ? 2m / (students - 1) : 1;
        return impact * classImpact;
    }
}
