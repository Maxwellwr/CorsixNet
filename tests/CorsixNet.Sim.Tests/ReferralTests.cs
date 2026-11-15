using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;

namespace CorsixNet.Sim.Tests;

/// <summary>The network game's referrals of patients to another hospital (the original's Patient Redirection Panel).</summary>
public class ReferralTests
{
    [Fact]
    public void APatientBetweenRoomsGoesToTheOtherHospitalsReception()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, PatientPhase.WalkingToRoom);

        // Only their own hospital can refer them, and not to itself.
        Run(simulation, new ReferPatientCommand(humanoid.Id, 1), player: 1);
        Run(simulation, new ReferPatientCommand(humanoid.Id, 0), player: 0);
        Assert.Equal(0, humanoid.HospitalPlayer);

        Run(simulation, new ReferPatientCommand(humanoid.Id, 1), player: 0);
        Assert.Equal(1, humanoid.HospitalPlayer);
        Assert.Equal(PatientPhase.SeekReception, patient.Phase);
    }

    [Fact]
    public void APatientAtReceptionOrAnEmergencyIsNotReferred()
    {
        var simulation = NewSimulation();
        var (atReception, _) = AddPatient(simulation, PatientPhase.SeekReception);
        Run(simulation, new ReferPatientCommand(atReception.Id, 1), player: 0);
        Assert.Equal(0, atReception.HospitalPlayer);

        var (emergency, patient) = AddPatient(simulation, PatientPhase.WalkingToRoom);
        typeof(PatientBehaviour).GetProperty(nameof(PatientBehaviour.EmergencyIndex))!.SetValue(patient, 1);
        Run(simulation, new ReferPatientCommand(emergency.Id, 1), player: 0);
        Assert.Equal(0, emergency.HospitalPlayer);
    }

    private static (Humanoid Humanoid, PatientBehaviour Patient) AddPatient(Simulation simulation, PatientPhase phase)
    {
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, new TilePosition(5, 5), simulation.Random, PatientLook.Standard, 0));
        var patient = new PatientBehaviour { Health = 0.9m };
        patient.SetDisease(DiseaseCatalog.Find("uncommon_cold")!, 0, simulation.Random);
        typeof(PatientBehaviour).GetProperty(nameof(PatientBehaviour.Phase))!.SetValue(patient, phase);
        humanoid.Behaviour = patient;
        return (humanoid, patient);
    }

    private static void Run(Simulation simulation, Command command, int player)
    {
        simulation.Schedule(player, command);
        simulation.Step();
    }

    private static Simulation NewSimulation()
    {
        var map = new LevelMap(10, 10, playerCount: 2);
        map.ParcelCount = 3;
        map.SetParcelOwner(1, 1);
        map.SetParcelOwner(2, 2);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                ref var tile = ref map[x, y];
                tile.Floor = 1;
                tile.Set(TileFlags.Passable, true);
                tile.Parcel = (ushort)(x < 5 ? 1 : 2);
                tile.Set(TileFlags.Buildable | TileFlags.Hospital, true);
            }
        }

        return new Simulation(map, seed: 1, rules: new LevelRules(10_000, [], []));
    }
}
