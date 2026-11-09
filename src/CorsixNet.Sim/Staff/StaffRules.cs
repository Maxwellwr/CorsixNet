// Staff settings and their defaults ported from CorsixTH CorsixTH/Lua/base_config.lua (staff, gbv.SalaryAdd,
// gbv.SalaryAbilityDivisor, gbv.DoctorThreshold, gbv.ConsultantThreshold, staff_levels); how an entry of
// staff_levels is chosen for a month, and the fallback to earlier entries, from CorsixTH/Lua/world.lua
// (World:makeAvailableStaff) and CorsixTH/Lua/staff_profile.lua (StaffProfile:randomise).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Staff;

/// <summary>
/// One entry of the level files' <c>staff_levels</c>: from <see cref="Month"/> on, how many people of
/// each class are available for hire and how often a doctor has each ability. A value left out
/// (null) is taken from the latest earlier entry that gives it.
/// </summary>
/// <param name="Month">Month of the game (0 at the start) from which the entry applies.</param>
/// <param name="Doctors">Doctors available for hire.</param>
/// <param name="Nurses">Nurses available for hire.</param>
/// <param name="Handymen">Handymen available for hire.</param>
/// <param name="Receptionists">Receptionists available for hire.</param>
/// <param name="ShrkRate">One doctor in this many is a psychiatrist; 0 for none.</param>
/// <param name="SurgRate">One doctor in this many is a surgeon; 0 for none.</param>
/// <param name="RschRate">One doctor in this many is a researcher; 0 for none.</param>
/// <param name="JrRate">One doctor in this many is a junior; 0 for none.</param>
/// <param name="ConsRate">One doctor in this many is a consultant; 0 for none.</param>
public sealed record StaffLevel(
    int Month,
    int? Doctors = null,
    int? Nurses = null,
    int? Handymen = null,
    int? Receptionists = null,
    int? ShrkRate = null,
    int? SurgRate = null,
    int? RschRate = null,
    int? JrRate = null,
    int? ConsRate = null);

/// <summary>
/// A member of staff the hospital has from the start (an entry of CorsixTH's <c>start_staff</c>, which only CorsixTH's
/// level files give): their class, their skill in thousandths and, for a doctor, their abilities. Whether a doctor is a
/// junior or a consultant follows from the skill.
/// </summary>
public sealed record StartingStaff(StaffClass Class, int Skill, bool Psychiatrist = false, bool Surgeon = false, bool Researcher = false)
{
    internal void Write(SimWriter writer)
    {
        writer.WriteByte((byte)Class);
        writer.WriteInt32(Skill);
        writer.WriteBool(Psychiatrist);
        writer.WriteBool(Surgeon);
        writer.WriteBool(Researcher);
    }

    internal static StartingStaff Read(SimReader reader)
    {
        var staff = new StartingStaff((StaffClass)reader.ReadByte(), reader.ReadInt32(), reader.ReadBool(), reader.ReadBool(), reader.ReadBool());
        return Enum.IsDefined(staff.Class) && staff.Skill is >= 0 and <= StaffProfile.MaxSkill
            ? staff
            : throw new InvalidDataException($"Bad starting staff {staff}.");
    }
}

/// <summary>
/// The staff settings of a level: minimum salaries, salary additions per ability, the skill
/// thresholds for juniors and consultants, who is available for hire when, and who works in the hospital from the
/// start.
/// </summary>
/// <remarks>Skill thresholds are in thousandths, as in the level files.</remarks>
public sealed class StaffRules
{
    /// <summary>First and last index of the level files' <c>gbv.SalaryAdd</c> that CorsixTH uses.</summary>
    public const int FirstSalaryAdd = 3;

    public const int LastSalaryAdd = 8;

    /// <summary>Index of <c>gbv.SalaryAdd</c> for each doctor ability (CorsixTH's <c>ability_conf_id</c>).</summary>
    public const int JuniorAdd = 3;

    public const int DoctorAdd = 4;
    public const int SurgeonAdd = 5;
    public const int PsychiatristAdd = 6;
    public const int ConsultantAdd = 7;
    public const int ResearcherAdd = 8;

    private const int MaxLevels = 1024;

    private readonly int[] _minSalaries;
    private readonly int[] _salaryAdds;
    private readonly StaffLevel[] _levels;
    private readonly StartingStaff[] _startStaff;

    /// <param name="minSalaries">Minimum salary per <see cref="StaffClass"/>, in class order (<c>staff[n].MinSalary</c>).</param>
    /// <param name="salaryAdds">Salary additions <c>gbv.SalaryAdd[3]</c> to <c>gbv.SalaryAdd[8]</c>.</param>
    /// <param name="salaryAbilityDivisor">Skill (in thousandths) divided by this is added to the salary; must be above 0.</param>
    /// <param name="doctorThreshold">Doctors with at most this skill are juniors.</param>
    /// <param name="consultantThreshold">Doctors with at least this skill are consultants.</param>
    /// <param name="levels">The <c>staff_levels</c> entries; the first must give every value.</param>
    /// <param name="startStaff">The <c>start_staff</c> entries, in order; none by default.</param>
    public StaffRules(
        IReadOnlyList<int> minSalaries,
        IReadOnlyList<int> salaryAdds,
        int salaryAbilityDivisor,
        int doctorThreshold,
        int consultantThreshold,
        IReadOnlyList<StaffLevel> levels,
        IReadOnlyList<StartingStaff>? startStaff = null)
    {
        if (minSalaries.Count != StaffClasses.Count)
        {
            throw new ArgumentException($"There must be {StaffClasses.Count} minimum salaries.", nameof(minSalaries));
        }

        if (salaryAdds.Count != LastSalaryAdd - FirstSalaryAdd + 1)
        {
            throw new ArgumentException($"There must be {LastSalaryAdd - FirstSalaryAdd + 1} salary additions.", nameof(salaryAdds));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(salaryAbilityDivisor);
        if (levels.Count is 0 or > MaxLevels)
        {
            throw new ArgumentException($"There must be between 1 and {MaxLevels} staff levels.", nameof(levels));
        }

        if (levels[0] is not
            {
                Doctors: not null, Nurses: not null, Handymen: not null, Receptionists: not null,
                ShrkRate: not null, SurgRate: not null, RschRate: not null, JrRate: not null, ConsRate: not null,
            })
        {
            throw new ArgumentException("The first staff level must give every value.", nameof(levels));
        }

        _minSalaries = [.. minSalaries];
        _salaryAdds = [.. salaryAdds];
        SalaryAbilityDivisor = salaryAbilityDivisor;
        DoctorThreshold = doctorThreshold;
        ConsultantThreshold = consultantThreshold;
        _levels = [.. levels];
        _startStaff = [.. startStaff ?? []];
        if (_startStaff.Length > MaxLevels || _startStaff.Any(staff => staff.Skill is < 0 or > StaffProfile.MaxSkill))
        {
            throw new ArgumentException($"There must be at most {MaxLevels} starting staff, with skills from 0 to {StaffProfile.MaxSkill}.", nameof(startStaff));
        }
    }

    /// <summary>The original game's settings, as CorsixTH's base_config.lua gives them.</summary>
    public static StaffRules Default { get; } = new(
        [60, 75, 25, 20],
        [-30, 30, 40, 30, 100, 20],
        salaryAbilityDivisor: 10,
        doctorThreshold: 250,
        consultantThreshold: 750,
        [new StaffLevel(0, Doctors: 8, Nurses: 8, Handymen: 3, Receptionists: 2, ShrkRate: 10, SurgRate: 10, RschRate: 10, JrRate: 5, ConsRate: 10)]);

    /// <summary>
    /// The original game's staff for hire, but everyone works for nothing, as in CorsixTH's free build
    /// mode (<see cref="WithoutSalaries"/>).
    /// </summary>
    public static StaffRules Free { get; } = Default.WithoutSalaries();

    public int SalaryAbilityDivisor { get; }

    public int DoctorThreshold { get; }

    public int ConsultantThreshold { get; }

    /// <summary>The <c>staff_levels</c> entries in file order.</summary>
    public IReadOnlyList<StaffLevel> Levels => _levels;

    /// <summary>Who works in each player's hospital from the start (<c>start_staff</c>).</summary>
    public IReadOnlyList<StartingStaff> StartStaff => _startStaff;

    /// <summary>
    /// The same staff for hire, but everyone works for nothing, as CorsixTH's <c>StaffProfile:getFairWage</c> in free
    /// build mode: every salary and addition is 0, and the divisor is above the highest skill.
    /// </summary>
    public StaffRules WithoutSalaries() => new(
        [0, 0, 0, 0],
        [0, 0, 0, 0, 0, 0],
        salaryAbilityDivisor: StaffProfile.MaxSkill + 1,
        DoctorThreshold,
        ConsultantThreshold,
        _levels,
        _startStaff);

    /// <summary>The minimum salary of a class (<c>staff[n].MinSalary</c>).</summary>
    public int MinSalary(StaffClass staffClass) => _minSalaries[(int)staffClass];

    /// <summary>The salary addition <c>gbv.SalaryAdd[index]</c>, for an index from <see cref="FirstSalaryAdd"/> to <see cref="LastSalaryAdd"/>.</summary>
    public int SalaryAdd(int index) => _salaryAdds[index - FirstSalaryAdd];

    /// <summary>
    /// A value of the <c>staff_levels</c> entry for <paramref name="month"/>: the last entry, counting
    /// from the first, whose month has come; if it leaves the value out, the latest earlier entry
    /// that gives it.
    /// </summary>
    /// <remarks>As CorsixTH, this assumes the entries are in increasing month order.</remarks>
    public int LevelValue(int month, Func<StaffLevel, int?> value)
    {
        var entry = 0;
        while (entry + 1 < _levels.Length && _levels[entry + 1].Month <= month)
        {
            entry++;
        }

        for (var i = entry; i >= 0; i--)
        {
            if (value(_levels[i]) is { } found)
            {
                return found;
            }
        }

        // The first entry gives every value.
        throw new InvalidOperationException("Unreachable.");
    }

    /// <summary>How many people of a class are available for hire in <paramref name="month"/>.</summary>
    public int AvailableCount(StaffClass staffClass, int month)
    {
        return staffClass switch
        {
            StaffClass.Nurse => LevelValue(month, level => level.Nurses),
            StaffClass.Doctor => LevelValue(month, level => level.Doctors),
            StaffClass.Handyman => LevelValue(month, level => level.Handymen),
            _ => LevelValue(month, level => level.Receptionists),
        };
    }

    internal void Write(SimWriter writer)
    {
        foreach (var salary in _minSalaries)
        {
            writer.WriteInt32(salary);
        }

        foreach (var add in _salaryAdds)
        {
            writer.WriteInt32(add);
        }

        writer.WriteInt32(SalaryAbilityDivisor);
        writer.WriteInt32(DoctorThreshold);
        writer.WriteInt32(ConsultantThreshold);
        writer.WriteInt32(_levels.Length);
        foreach (var level in _levels)
        {
            writer.WriteInt32(level.Month);
            WriteOptional(writer, level.Doctors);
            WriteOptional(writer, level.Nurses);
            WriteOptional(writer, level.Handymen);
            WriteOptional(writer, level.Receptionists);
            WriteOptional(writer, level.ShrkRate);
            WriteOptional(writer, level.SurgRate);
            WriteOptional(writer, level.RschRate);
            WriteOptional(writer, level.JrRate);
            WriteOptional(writer, level.ConsRate);
        }

        writer.WriteInt32(_startStaff.Length);
        foreach (var staff in _startStaff)
        {
            staff.Write(writer);
        }
    }

    internal static StaffRules Read(SimReader reader)
    {
        var minSalaries = new int[StaffClasses.Count];
        for (var i = 0; i < minSalaries.Length; i++)
        {
            minSalaries[i] = reader.ReadInt32();
        }

        var salaryAdds = new int[LastSalaryAdd - FirstSalaryAdd + 1];
        for (var i = 0; i < salaryAdds.Length; i++)
        {
            salaryAdds[i] = reader.ReadInt32();
        }

        var divisor = reader.ReadInt32();
        var doctorThreshold = reader.ReadInt32();
        var consultantThreshold = reader.ReadInt32();
        var levels = new StaffLevel[reader.ReadCount(MaxLevels)];
        for (var i = 0; i < levels.Length; i++)
        {
            levels[i] = new StaffLevel(
                reader.ReadInt32(),
                ReadOptional(reader),
                ReadOptional(reader),
                ReadOptional(reader),
                ReadOptional(reader),
                ReadOptional(reader),
                ReadOptional(reader),
                ReadOptional(reader),
                ReadOptional(reader),
                ReadOptional(reader));
        }

        var startStaff = new StartingStaff[reader.ReadCount(MaxLevels)];
        for (var i = 0; i < startStaff.Length; i++)
        {
            startStaff[i] = StartingStaff.Read(reader);
        }

        try
        {
            return new StaffRules(minSalaries, salaryAdds, divisor, doctorThreshold, consultantThreshold, levels, startStaff);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Invalid staff settings.", exception);
        }
    }

    private static void WriteOptional(SimWriter writer, int? value)
    {
        writer.WriteBool(value.HasValue);
        if (value is { } present)
        {
            writer.WriteInt32(present);
        }
    }

    private static int? ReadOptional(SimReader reader) => reader.ReadBool() ? reader.ReadInt32() : null;
}
