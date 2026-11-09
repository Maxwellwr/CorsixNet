using CorsixNet.Sim.Disasters;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;

namespace CorsixNet.Sim.Tests;

/// <summary>Which patients are contagious and catching another disease (CorsixTH's determineIfContagious, changeDisease).</summary>
public class EpidemicContagionTests
{
    [Fact]
    public void PatientsAreContagiousOnlyAfterTheLevelsMonthsAndVisitors()
    {
        var simulation = NewSimulation(contagiousRate: 1);
        var hospital = simulation.Hospitals[0];
        var (humanoid, patient) = NewPatient(simulation, "broken_wind");

        // Month 0 and no visitors: not yet.
        Epidemics.DetermineIfContagious(simulation, hospital, humanoid, patient);
        Assert.Equal(0, hospital.Epidemics.Count);

        hospital.Visitors = 21;
        while (simulation.Date.MonthOfGame <= 14)
        {
            simulation.Step();
        }

        Epidemics.DetermineIfContagious(simulation, hospital, humanoid, patient);
        var epidemic = Assert.Single(hospital.Epidemics.Future);
        Assert.Equal([humanoid.Id], epidemic.InfectedIds);
        Assert.True(patient.IsInfected);

        // The limit of one epidemic at a time keeps another disease out.
        var (other, otherPatient) = NewPatient(simulation, "chronic_nosehair");
        Epidemics.DetermineIfContagious(simulation, hospital, other, otherPatient);
        Assert.Single(hospital.Epidemics.Future);
        Assert.False(otherPatient.IsInfected);
    }

    [Fact]
    public void ANonContagiousDiseaseNeverIs()
    {
        var simulation = NewSimulation(contagiousRate: 1);
        var hospital = simulation.Hospitals[0];
        hospital.Visitors = 100;
        var disease = DiseaseCatalog.All.First(disease => !disease.IsContagious && !disease.IsPseudo && disease.ExpertiseId > 1);
        var (humanoid, patient) = NewPatient(simulation, disease.Id);

        Epidemics.DetermineIfContagious(simulation, hospital, humanoid, patient);

        Assert.Equal(0, hospital.Epidemics.Count);
    }

    [Fact]
    public void ACaughtDiseaseBringsItsDiagnosisRooms()
    {
        var simulation = NewSimulation(contagiousRate: 0);
        var (_, patient) = NewPatient(simulation, "broken_wind");
        var nosehair = DiseaseCatalog.Find("chronic_nosehair")!;

        patient.ChangeDisease(nosehair);

        Assert.Equal("chronic_nosehair", patient.DiseaseId);
        Assert.Equal(nosehair.DiagnosisRooms, patient.AvailableDiagnosisRooms);
    }

    private static (Humanoid Humanoid, PatientBehaviour Patient) NewPatient(Simulation simulation, string diseaseId)
    {
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, new TilePosition(1, 1), simulation.Random, PatientLook.Standard, 0));
        var patient = new PatientBehaviour();
        patient.SetDisease(DiseaseCatalog.Find(diseaseId)!, 0, simulation.Random);
        humanoid.Behaviour = patient;
        return (humanoid, patient);
    }

    private static Simulation NewSimulation(int contagiousRate)
    {
        var rates = Enumerable.Repeat(contagiousRate, DiseaseRules.ExpertiseCount + 1).ToArray();
        var map = new LevelMap(10, 10, playerCount: 1);
        return new Simulation(map, seed: 1, rules: new LevelRules(10_000, [], [], epidemics: new EpidemicRules(rates)));
    }
}
