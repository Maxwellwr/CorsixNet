using CorsixNet.Client.Maps;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;

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
}
