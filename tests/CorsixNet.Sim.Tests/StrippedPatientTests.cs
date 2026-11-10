using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;

namespace CorsixNet.Sim.Tests;

/// <summary>The three kinds of stripped patient (CorsixTH's use_screen.lua).</summary>
public class StrippedPatientTests
{
    [Theory]
    [InlineData(HumanoidType.StrippedMalePatient, HumanoidType.StandardMalePatient)]
    [InlineData(HumanoidType.StrippedMalePatient2, HumanoidType.StandardMalePatient)]
    [InlineData(HumanoidType.StrippedMalePatient3, HumanoidType.StandardMalePatient)]
    [InlineData(HumanoidType.StrippedFemalePatient, HumanoidType.StandardFemalePatient)]
    [InlineData(HumanoidType.StrippedFemalePatient2, HumanoidType.StandardFemalePatient)]
    [InlineData(HumanoidType.StrippedFemalePatient3, HumanoidType.StandardFemalePatient)]
    [InlineData(HumanoidType.Doctor, HumanoidType.Doctor)]
    public void EveryKindDressesAsTheStandardPatient(HumanoidType stripped, HumanoidType dressed)
    {
        Assert.Equal(dressed, PatientBehaviour.Dressed(stripped));
    }
}
