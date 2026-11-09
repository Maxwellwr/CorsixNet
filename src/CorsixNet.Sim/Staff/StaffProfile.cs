// Staff profiles ported from CorsixTH CorsixTH/Lua/staff_profile.lua (StaffProfile:initDoctor,
// StaffProfile:init, StaffProfile:randomise,
// StaffProfile:randomiseOrganical, StaffProfile:parseSkillLevel, StaffProfile:getFairWage).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Staff;

/// <summary>A class of staff, numbered as the level files' <c>staff[n]</c> (CorsixTH's <c>conf_id</c>).</summary>
public enum StaffClass : byte
{
    Nurse = 0,
    Doctor = 1,
    Handyman = 2,
    Receptionist = 3,
}

/// <summary>Helpers for <see cref="StaffClass"/>.</summary>
public static class StaffClasses
{
    /// <summary>Number of staff classes.</summary>
    public const int Count = 4;

    /// <summary>
    /// The classes in the order CorsixTH makes their candidates for hire (<c>staff_to_make</c> in
    /// world.lua), which decides the order random numbers are drawn in.
    /// </summary>
    public static IReadOnlyList<StaffClass> HireOrder { get; } = [StaffClass.Doctor, StaffClass.Nurse, StaffClass.Handyman, StaffClass.Receptionist];
}

/// <summary>
/// A member of staff as a candidate for hire and, once hired, as an employee: skill, abilities,
/// salary and looks.
/// </summary>
/// <remarks>
/// CorsixTH keeps skill as a number from 0 to 1. Here it is in thousandths, from 0 to 1000, like the
/// thresholds in the level files, so that the simulation stays free of floating point. Names and
/// descriptions come from the language files and are left to the client; <see cref="NameSeed"/>
/// picks the initial, as in CorsixTH.
/// </remarks>
public sealed class StaffProfile
{
    /// <summary>Skill is in thousandths: 1000 stands for CorsixTH's 1.0.</summary>
    public const int MaxSkill = 1000;

    public StaffProfile(StaffClass staffClass)
    {
        Class = staffClass;
    }

    public StaffClass Class { get; }

    /// <summary>Picks the initial from the language's list (CorsixTH's <c>name_seed</c>, 1 to 1009).</summary>
    public int NameSeed { get; private set; }

    /// <summary>Attention to detail in thousandths (0 to 999).</summary>
    public int AttentionToDetail { get; private set; }

    /// <summary>Skill in thousandths (0 to 1000).</summary>
    public int Skill { get; private set; }

    public bool IsPsychiatrist { get; private set; }

    public bool IsSurgeon { get; private set; }

    public bool IsResearcher { get; private set; }

    /// <summary>Skill at most <see cref="StaffRules.DoctorThreshold"/>. Set for every class, as CorsixTH does; it matters for doctors.</summary>
    public bool IsJunior { get; private set; }

    /// <summary>Skill at least <see cref="StaffRules.ConsultantThreshold"/>. Set for every class, as CorsixTH does; it matters for doctors.</summary>
    public bool IsConsultant { get; private set; }

    /// <summary>Monthly salary, which is also what hiring costs.</summary>
    public int Wage { get; internal set; }

    /// <summary>Picture parts for the face in the staff window.</summary>
    public int HairIndex { get; private set; }

    public int FaceIndex { get; private set; }

    public int ChinIndex { get; private set; }

    /// <summary>Variant of animation layer 5 (skin colour of doctors).</summary>
    public byte Layer5 { get; private set; } = 2;

    /// <summary>
    /// Makes a random candidate for hire in month <paramref name="month"/> of the game, as CorsixTH's
    /// <c>StaffProfile</c> constructor and <c>randomise</c> do.
    /// </summary>
    public static StaffProfile CreateRandom(StaffClass staffClass, int month, StaffRules rules, SimRandom random)
    {
        var profile = new StaffProfile(staffClass)
        {
            NameSeed = random.NextInclusive(1, 1009),
            AttentionToDetail = random.Next(MaxSkill),
        };
        profile.Randomise(month, rules, random);
        return profile;
    }

    /// <summary>
    /// Makes a member of staff with a given skill and abilities, as CorsixTH's <c>initDoctor</c> and
    /// <c>init</c> do (for starting staff): the skill decides whether a doctor is a junior or a
    /// consultant, the wage follows, and the looks are random.
    /// </summary>
    public static StaffProfile Create(
        StaffClass staffClass,
        int skill,
        StaffRules rules,
        SimRandom random,
        bool psychiatrist = false,
        bool surgeon = false,
        bool researcher = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skill);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(skill, MaxSkill);
        var profile = new StaffProfile(staffClass)
        {
            NameSeed = random.NextInclusive(1, 1009),
            AttentionToDetail = random.Next(MaxSkill),
            IsPsychiatrist = psychiatrist,
            IsSurgeon = surgeon,
            IsResearcher = researcher,
            Skill = skill,
        };
        profile.ParseSkillLevel(rules);
        profile.Wage = profile.FairWage(rules);
        profile.RandomiseLooks(random);
        return profile;
    }

    /// <summary>
    /// What the member of staff should earn (<c>getFairWage</c>): the class's minimum salary, plus
    /// skill divided by <see cref="StaffRules.SalaryAbilityDivisor"/>, plus for a doctor the
    /// additions for each ability (or the plain doctor's addition), and never below the minimum.
    /// </summary>
    public int FairWage(StaffRules rules)
    {
        var minimum = rules.MinSalary(Class);

        // CorsixTH floors the sum; the other terms are whole, so flooring the skill part is the same.
        var wage = minimum + (Skill / rules.SalaryAbilityDivisor);
        if (Class == StaffClass.Doctor)
        {
            wage += (IsJunior ? rules.SalaryAdd(StaffRules.JuniorAdd) : 0)
                + (IsSurgeon ? rules.SalaryAdd(StaffRules.SurgeonAdd) : 0)
                + (IsPsychiatrist ? rules.SalaryAdd(StaffRules.PsychiatristAdd) : 0)
                + (IsConsultant ? rules.SalaryAdd(StaffRules.ConsultantAdd) : 0)
                + (IsResearcher ? rules.SalaryAdd(StaffRules.ResearcherAdd) : 0);
            if (!IsJunior && !IsConsultant)
            {
                wage += rules.SalaryAdd(StaffRules.DoctorAdd);
            }
        }

        return Math.Max(wage, minimum);
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteByte((byte)Class);
        writer.WriteInt32(NameSeed);
        writer.WriteInt32(AttentionToDetail);
        writer.WriteInt32(Skill);
        writer.WriteBool(IsPsychiatrist);
        writer.WriteBool(IsSurgeon);
        writer.WriteBool(IsResearcher);
        writer.WriteBool(IsJunior);
        writer.WriteBool(IsConsultant);
        writer.WriteInt32(Wage);
        writer.WriteInt32(HairIndex);
        writer.WriteInt32(FaceIndex);
        writer.WriteInt32(ChinIndex);
        writer.WriteByte(Layer5);
    }

    internal static StaffProfile Read(SimReader reader)
    {
        var staffClass = reader.ReadByte();
        if (staffClass >= StaffClasses.Count)
        {
            throw new InvalidDataException($"Unknown staff class {staffClass}.");
        }

        return new StaffProfile((StaffClass)staffClass)
        {
            NameSeed = reader.ReadInt32(),
            AttentionToDetail = reader.ReadInt32(),
            Skill = reader.ReadInt32(),
            IsPsychiatrist = reader.ReadBool(),
            IsSurgeon = reader.ReadBool(),
            IsResearcher = reader.ReadBool(),
            IsJunior = reader.ReadBool(),
            IsConsultant = reader.ReadBool(),
            Wage = reader.ReadInt32(),
            HairIndex = reader.ReadInt32(),
            FaceIndex = reader.ReadInt32(),
            ChinIndex = reader.ReadInt32(),
            Layer5 = reader.ReadByte(),
        };
    }

    private void Randomise(int month, StaffRules rules, SimRandom random)
    {
        // CorsixTH: self.skill = math.random(), a number in [0, 1).
        Skill = random.Next(MaxSkill);
        if (Class == StaffClass.Doctor)
        {
            // "One in x" for each ability; CorsixTH tests math.random() < 1 / x.
            IsPsychiatrist = OneIn(rules.LevelValue(month, level => level.ShrkRate), random);
            IsSurgeon = OneIn(rules.LevelValue(month, level => level.SurgRate), random);
            IsResearcher = OneIn(rules.LevelValue(month, level => level.RschRate), random);
            IsJunior = OneIn(rules.LevelValue(month, level => level.JrRate), random);
            IsConsultant = !IsJunior && OneIn(rules.LevelValue(month, level => level.ConsRate), random);

            // Put the doctor in the right skill band.
            var junior = rules.DoctorThreshold;
            var consultant = rules.ConsultantThreshold;
            if (IsJunior)
            {
                Skill = junior * Skill / MaxSkill;
            }
            else if (IsConsultant)
            {
                Skill = consultant + ((MaxSkill - consultant) * Skill / MaxSkill);
            }
            else
            {
                Skill = junior + ((consultant - junior) * Skill / MaxSkill);
            }
        }

        // As in CorsixTH, the wage is worked out from the rolled abilities, before the skill
        // decides again whether the doctor counts as a junior or a consultant.
        Wage = FairWage(rules);
        ParseSkillLevel(rules);
        RandomiseLooks(random);
    }

    /// <summary>Whether the skill makes a junior or a consultant (<c>parseSkillLevel</c>).</summary>
    private void ParseSkillLevel(StaffRules rules)
    {
        IsJunior = Skill <= rules.DoctorThreshold;
        IsConsultant = Skill >= rules.ConsultantThreshold;
    }

    /// <summary>Picks the face parts and skin colour (<c>randomiseOrganical</c>).</summary>
    private void RandomiseLooks(SimRandom random)
    {
        var (first, last) = Class switch
        {
            StaffClass.Doctor => (0, 4),
            StaffClass.Nurse => (10, 12),
            StaffClass.Receptionist => (13, 14),
            _ => (15, 17),
        };

        if (Class == StaffClass.Doctor && random.Next(2) == 0)
        {
            (first, last) = (5, 9);
            Layer5 = 4;
        }

        HairIndex = random.NextInclusive(first, last);
        FaceIndex = random.NextInclusive(first, last);
        ChinIndex = random.NextInclusive(first, last);
    }

    /// <summary>True with probability 1 in <paramref name="rate"/>; never for 0 (or less).</summary>
    private static bool OneIn(int rate, SimRandom random) => rate > 0 && random.Next(rate) == 0;
}
