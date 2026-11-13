using CorsixNet.Client.Ui;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Client.Tests;

/// <summary>The bottom panel's dynamic info (CorsixTH's Patient/Staff:updateDynamicInfo), with English texts.</summary>
public class DynamicInfoTests
{
    [Fact]
    public void APatientShowsTheDiagnosisAndAMemberOfStaffTheirTiredness()
    {
        var host = new WindowTestHost();
        var simulation = host.Simulation;
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, new TilePosition(10, 10), simulation.Random, PatientLook.Standard, 0));
        var patient = new PatientBehaviour();
        patient.SetDisease(DiseaseCatalog.Find("uncommon_cold")!, 0, simulation.Random);
        patient.DiagnosisProgress = 420;
        humanoid.Behaviour = patient;
        Assert.Equal("Diagnosis progress 42 %", DynamicInfo.Of(simulation, humanoid, null));

        patient.IsDiagnosed = true;
        patient.DiagnosisProgress = 1000;
        Assert.Equal("Diagnosed: uncommon_cold", DynamicInfo.Of(simulation, humanoid, null));

        host.Submit(new HireStaffCommand(StaffClass.Doctor, 0, new(5, 2)));
        var doctor = simulation.Entities.OfType<Humanoid>().Single(h => h.Profile is { Class: StaffClass.Doctor });
        doctor.Fatigue = 0.25m;
        Assert.EndsWith("Tiredness 25 %", DynamicInfo.Of(simulation, doctor, null), StringComparison.Ordinal);
    }
}
