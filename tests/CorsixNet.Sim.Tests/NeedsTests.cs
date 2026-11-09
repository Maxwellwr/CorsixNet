using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;

namespace CorsixNet.Sim.Tests;

/// <summary>Patients' thirst and need for the toilet (CorsixTH's Patient:tickDay, _dailyBowelChecks, _handleExcessThirst).</summary>
public class NeedsTests
{
    [Fact]
    public void PatientsComeALittleThirstyExceptAliens()
    {
        var simulation = NewSimulation();
        var patients = Enumerable.Range(0, 100).Select(_ => AddPatient(simulation, "uncommon_cold").Patient).ToList();
        Assert.All(patients, patient =>
        {
            Assert.InRange(patient.Thirst, 0m, 0.2m);
            Assert.InRange(patient.ToiletNeed, 0m, 0.2m);
        });
        Assert.Contains(patients, patient => patient.Thirst > 0.1m);

        var alien = AddPatient(simulation, "alien_dna").Patient;
        Assert.Equal(0m, alien.Thirst);
        Assert.Equal(0m, alien.ToiletNeed);
    }

    [Fact]
    public void EachDayThirstGrowsWithWarmthAndTheSquitsSendToTheToiletSooner()
    {
        var simulation = NewSimulation();
        var (cold, coldPatient) = AddPatient(simulation, "uncommon_cold");
        var (squits, squitsPatient) = AddPatient(simulation, "the_squits");
        var thirst = coldPatient.Thirst;
        var coldNeed = coldPatient.ToiletNeed;
        var squitsNeed = squitsPatient.ToiletNeed;

        Feelings.OnEndDay(simulation, cold);
        Feelings.OnEndDay(simulation, squits);

        // Warmth is updated first in the day: from 0.29 to 0.75 × 0.29 at a cold tile.
        var warmth = cold.Warmth;
        Assert.InRange(coldPatient.Thirst - thirst, (warmth * 0.02m) + 0.004m, (warmth * 0.02m) + 0.008m);
        Assert.InRange(coldPatient.ToiletNeed - coldNeed, 0.002m, 0.008m);
        Assert.InRange(squitsPatient.ToiletNeed - squitsNeed, 0.008m, 0.026m);
    }

    [Fact]
    public void EachDayShowsWhetherThePatientIsSadAndAddsToTheHealthGraph()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, "uncommon_cold");
        humanoid.Happiness = 0.29m;

        Feelings.OnEndDay(simulation, humanoid);

        // The mood is set before the day changes happiness; health goes into the graph before the day's loss.
        Assert.True(patient.IsSad);
        Assert.Equal([0.9m], patient.HealthHistory);
        Assert.Equal(0.896m, patient.Health);

        for (var day = 0; day < 25; day++)
        {
            humanoid.Happiness = 0.3m;
            Feelings.OnEndDay(simulation, humanoid);
        }

        Assert.False(patient.IsSad);
        Assert.Equal(PatientBehaviour.HealthHistorySize, patient.HealthHistory.Count);
        Assert.Equal(0.9m - (6 * 0.004m), patient.HealthHistory[0]);
        Assert.Equal(0.9m - (25 * 0.004m), patient.HealthHistory[^1]);
    }

    [Fact]
    public void AThirstyPatientIsUnhappyAndLooksForADrinksMachineEveryFewDays()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, "uncommon_cold");
        patient.Thirst = 0.8m;
        humanoid.Happiness = 0.5m;

        Feelings.OnEndDay(simulation, humanoid);

        Assert.True(patient.IsThirsty);

        // Comfortable (0.005), standing about (−0.00002), thirsty (−0.002).
        Assert.Equal(0.50298m, humanoid.Happiness);

        // No machine: they look again in two to four days.
        Assert.InRange(patient.DrinkTimeout, 2, 4);
        var timeout = patient.DrinkTimeout;
        Feelings.OnEndDay(simulation, humanoid);
        Assert.Equal(timeout - 1, patient.DrinkTimeout);

        // An emergency patient's needs do not change.
        var (emergency, emergencyPatient) = AddPatient(simulation, "uncommon_cold");
        emergencyPatient.StartEmergency(simulation, emergency, 1);
        var before = emergencyPatient.Thirst;
        Feelings.OnEndDay(simulation, emergency);
        Assert.Equal(before, emergencyPatient.Thirst);
    }

    [Fact]
    public void NeedsAreSaved()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, "uncommon_cold");
        patient.Thirst = 0.8m;
        Feelings.OnEndDay(simulation, humanoid);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        var copy = (PatientBehaviour)restored.Entities.OfType<Humanoid>().Single().Behaviour!;
        Assert.Equal(patient.Thirst, copy.Thirst);
        Assert.Equal(patient.ToiletNeed, copy.ToiletNeed);
        Assert.Equal(patient.DrinkTimeout, copy.DrinkTimeout);
        Assert.True(copy.IsThirsty);
    }

    private static (Humanoid Humanoid, PatientBehaviour Patient) AddPatient(Simulation simulation, string diseaseId)
    {
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, new TilePosition(2, 2), simulation.Random, PatientLook.Standard, 0));
        var patient = new PatientBehaviour { Health = 0.9m };
        patient.SetDisease(DiseaseCatalog.Find(diseaseId)!, 0, simulation.Random);
        humanoid.Behaviour = patient;
        return (humanoid, patient);
    }

    private static Simulation NewSimulation()
    {
        var map = TestMaps.FromRows("........", "........", "........", "........", "........", "........", "........", "........");
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Parcel = 1;
                map[x, y].Set(TileFlags.Buildable | TileFlags.Hospital, true);
            }
        }

        return new Simulation(map, seed: 1, rules: new LevelRules(10_000, [], []));
    }
}
