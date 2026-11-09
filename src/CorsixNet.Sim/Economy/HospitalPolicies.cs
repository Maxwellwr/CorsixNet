// A hospital's policy settings, ported from CorsixTH CorsixTH/Lua/hospital.lua (Hospital:Hospital's policies) and
// dialogs/fullscreen/hospital_policy.lua (UIPolicy, the sliders' ranges).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Economy;

/// <summary>One of the hospital policy window's settings.</summary>
public enum PolicyKind : byte
{
    /// <summary>Below this diagnosis, with no diagnosis room left, a patient is sent home (<c>send_home</c>).</summary>
    SendHome = 1,

    /// <summary>From this diagnosis on, a known disease is guessed when no diagnosis room is left (<c>guess_cure</c>).</summary>
    GuessCure = 2,

    /// <summary>Diagnosis stops at this, from 1.0 to 2.0 (<c>stop_procedure</c>).</summary>
    StopProcedure = 3,

    /// <summary>Staff this tired go to the staff room (<c>goto_staffroom</c>).</summary>
    GoToStaffRoom = 4,

    /// <summary>Whether staff may leave their room when called elsewhere (<c>staff_allowed_to_move</c>); 0 or 1.</summary>
    StaffAllowedToMove = 5,

    /// <summary>Whether raises are granted when staff tire of waiting for one (<c>grant_wage_increase</c>); 0 or 1.</summary>
    GrantWageIncrease = 6,
}

/// <summary>
/// A hospital's policies, CorsixTH's <c>hospital.policies</c>. The sliders' values are kept in thousandths (CorsixTH
/// keeps the slider's fraction of its 299 or 250 pixels), the switches as booleans.
/// </summary>
public sealed class HospitalPolicies
{
    /// <summary>CorsixTH's default <c>send_home</c>, 0.1.</summary>
    public const int DefaultSendHome = 100;

    /// <summary>CorsixTH's default <c>guess_cure</c>, 0.9.</summary>
    public const int DefaultGuessCure = 900;

    /// <summary>CorsixTH's default <c>stop_procedure</c>, 1.0, which is also its lowest.</summary>
    public const int DefaultStopProcedure = 1000;

    /// <summary>The highest <c>stop_procedure</c>, 2.0.</summary>
    public const int MaxStopProcedure = 2000;

    /// <summary>The most a slider of 0 to 1 can be set to.</summary>
    public const int MaxFraction = 1000;

    /// <param name="goToStaffRoom">The level's <c>gbv.Tired</c>, which CorsixTH starts <c>goto_staffroom</c> at.</param>
    public HospitalPolicies(int goToStaffRoom = 600) => GoToStaffRoom = Math.Clamp(goToStaffRoom, 0, MaxFraction);

    public int SendHome { get; private set; } = DefaultSendHome;

    public int GuessCure { get; private set; } = DefaultGuessCure;

    public int StopProcedure { get; private set; } = DefaultStopProcedure;

    public int GoToStaffRoom { get; private set; }

    public bool StaffAllowedToMove { get; private set; } = true;

    /// <summary>CorsixTH takes it from its config file, where it is off by default.</summary>
    public bool GrantWageIncrease { get; private set; }

    /// <summary>
    /// Sets a policy, kept within its slider's range as the window does: sending home and guessing cannot pass each
    /// other, stopping is from 1.0 to 2.0, the rest from 0 to 1.
    /// </summary>
    public void Set(PolicyKind kind, int value)
    {
        switch (kind)
        {
            case PolicyKind.SendHome:
                SendHome = Math.Clamp(value, 0, GuessCure);
                break;
            case PolicyKind.GuessCure:
                GuessCure = Math.Clamp(value, SendHome, MaxFraction);
                break;
            case PolicyKind.StopProcedure:
                StopProcedure = Math.Clamp(value, DefaultStopProcedure, MaxStopProcedure);
                break;
            case PolicyKind.GoToStaffRoom:
                GoToStaffRoom = Math.Clamp(value, 0, MaxFraction);
                break;
            case PolicyKind.StaffAllowedToMove:
                StaffAllowedToMove = value != 0;
                break;
            case PolicyKind.GrantWageIncrease:
                GrantWageIncrease = value != 0;
                break;
        }
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(SendHome);
        writer.WriteInt32(GuessCure);
        writer.WriteInt32(StopProcedure);
        writer.WriteInt32(GoToStaffRoom);
        writer.WriteBool(StaffAllowedToMove);
        writer.WriteBool(GrantWageIncrease);
    }

    internal static HospitalPolicies Read(SimReader reader)
    {
        var policies = new HospitalPolicies(0);
        policies.SendHome = reader.ReadInt32();
        policies.GuessCure = reader.ReadInt32();
        policies.StopProcedure = reader.ReadInt32();
        policies.GoToStaffRoom = reader.ReadInt32();
        policies.StaffAllowedToMove = reader.ReadBool();
        policies.GrantWageIncrease = reader.ReadBool();
        if (policies.SendHome < 0 || policies.SendHome > policies.GuessCure || policies.GuessCure > MaxFraction
            || policies.StopProcedure is < DefaultStopProcedure or > MaxStopProcedure || policies.GoToStaffRoom is < 0 or > MaxFraction)
        {
            throw new InvalidDataException("Hospital policies are out of range.");
        }

        return policies;
    }
}
