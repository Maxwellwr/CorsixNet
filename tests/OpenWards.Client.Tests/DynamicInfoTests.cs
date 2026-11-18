using OpenWards.Client.Ui;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Diseases;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.People;
using OpenWards.Sim.Staff;

namespace OpenWards.Client.Tests;

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

    [Fact]
    public void ThePanelShowsTheDiagnosisAndTirednessAsBars()
    {
        var host = new WindowTestHost();
        var simulation = host.Simulation;
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, new TilePosition(10, 10), simulation.Random, PatientLook.Standard, 0));
        var patient = new PatientBehaviour();
        patient.SetDisease(DiseaseCatalog.Find("uncommon_cold")!, 0, simulation.Random);
        patient.DiagnosisProgress = 420;
        humanoid.Behaviour = patient;
        var info = Assert.IsType<DynamicInfoLines>(DynamicInfo.Panel(simulation, humanoid, null));
        Assert.Equal("Diagnosis progress", info.Lines[^1]);
        Assert.Equal(0.42m, info.Progress);

        patient.IsDiagnosed = true;
        Assert.Null(DynamicInfo.Panel(simulation, humanoid, null)!.Progress);

        host.Submit(new HireStaffCommand(StaffClass.Doctor, 0, new(5, 2)));
        var doctor = simulation.Entities.OfType<Humanoid>().Single(h => h.Profile is { Class: StaffClass.Doctor });
        doctor.Fatigue = 0.25m;
        var staff = DynamicInfo.Panel(simulation, doctor, null)!;
        Assert.Equal(("Tiredness", 0.25m), (staff.Lines[^1], staff.Progress));
        Assert.Equal([0.6m], staff.Dividers);
        Assert.Equal(24, OriginalBottomPanel.ProgressWidth(0.25m));
    }
}
