using CorsixNet.Content;
using CorsixNet.Sim;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;

namespace CorsixNet.Client.Tests;

/// <summary>A dead patient falls and flies up to heaven (CorsixTH's humanoid_actions/die.lua).</summary>
public class DyingTests
{
    /// <summary>Women, who never go to hell.</summary>
    [Theory]
    [InlineData(HumanoidType.StandardFemalePatient)]
    [InlineData(HumanoidType.AlienFemalePatient)]
    [InlineData(HumanoidType.SlackFemalePatient)]
    public void ADeadPatientFallsRisesAndFliesAway(HumanoidType type)
    {
        var host = new WindowTestHost();
        var simulation = host.Simulation;
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, new TilePosition(10, 10), simulation.Random, PatientLook.Standard, 0));
        humanoid.Type = type;
        var patient = new PatientBehaviour { Health = 0m };
        patient.SetDisease(DiseaseCatalog.Find("uncommon_cold")!, 0, simulation.Random);
        humanoid.Behaviour = patient;
        patient.Die(simulation, humanoid);
        Assert.Equal(PatientPhase.Dying, patient.Phase);
        Assert.Equal(1, simulation.Hospitals[0].Deaths);

        var animations = simulation.Content.FindDeathAnimations(humanoid.Type)!;
        var seen = new List<(DyingStage Stage, int? Animation)>();
        var saved = false;
        for (var tick = 0; tick < 2000 && !humanoid.IsDespawning; tick++)
        {
            simulation.Step();
            if (seen.Count == 0 || seen[^1].Stage != patient.DyingStage)
            {
                seen.Add((patient.DyingStage, humanoid.ActionAnimation?.Id));
            }

            if (!saved && patient.DyingStage == DyingStage.Wings)
            {
                // The trip goes on the same after a save.
                saved = true;
                using var stream = new MemoryStream();
                simulation.Save(stream);
                stream.Position = 0;
                var loaded = Simulation.Load(stream, BaseGameContent.Create());
                Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
            }
        }

        Assert.True(humanoid.IsDespawning);
        var expected = new List<(DyingStage, int?)> { (DyingStage.Falling, animations.Fall.Id) };
        if (animations.Extra is { } extra)
        {
            expected.Add((DyingStage.Extra, extra.Id));
        }

        expected.AddRange([(DyingStage.Rising, animations.Rise.Id), (DyingStage.Wings, animations.Wings.Id), (DyingStage.Hands, animations.Hands.Id), (DyingStage.Flying, animations.Fly.Id)]);
        Assert.Equal(expected, seen.SkipWhile(s => s.Stage == DyingStage.SteppingAside).ToList());
    }

    [Fact]
    public void SomeDeadMenAreTakenToHellByTheGrimReaper()
    {
        var host = new WindowTestHost();
        var simulation = host.Simulation;
        var stages = new List<DyingStage>();
        var reaperPhases = new List<GrimReaperPhase>();
        var saved = false;
        Humanoid? man = null;
        for (var attempt = 0; attempt < 20 && man is null; attempt++)
        {
            var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, new TilePosition(8, 6), simulation.Random, PatientLook.Hairyitis, 0));
            var patient = new PatientBehaviour { Health = 0m };
            patient.SetDisease(DiseaseCatalog.Find("uncommon_cold")!, 0, simulation.Random);
            humanoid.Behaviour = patient;
            patient.Die(simulation, humanoid);
            for (var tick = 0; tick < 200 && patient.ReaperId == 0 && !humanoid.IsDespawning; tick++)
            {
                simulation.Step();
            }

            if (patient.ReaperId != 0)
            {
                man = humanoid;
            }
            else
            {
                for (var tick = 0; tick < 300 && !humanoid.IsDespawning; tick++)
                {
                    simulation.Step();
                }
            }
        }

        Assert.NotNull(man);
        var dead = (PatientBehaviour)man.Behaviour!;
        var reaper = Assert.IsType<Humanoid>(simulation.FindEntity(dead.ReaperId));
        var grim = Assert.IsType<GrimReaperBehaviour>(reaper.Behaviour);
        Assert.Equal(HumanoidType.GrimReaper, reaper.Type);
        Assert.Equal(HumanoidType.StandardMalePatient, man.Type);
        for (var tick = 0; tick < 3000 && !reaper.IsDespawning; tick++)
        {
            simulation.Step();
            if (!man.IsDespawning && (stages.Count == 0 || stages[^1] != dead.DyingStage))
            {
                stages.Add(dead.DyingStage);
            }

            if (reaperPhases.Count == 0 || reaperPhases[^1] != grim.Phase)
            {
                reaperPhases.Add(grim.Phase);
            }

            if (!saved && dead.DyingStage == DyingStage.WalkingToHole)
            {
                saved = true;
                using var stream = new MemoryStream();
                simulation.Save(stream);
                stream.Position = 0;
                var loaded = Simulation.Load(stream, BaseGameContent.Create());
                Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
            }
        }

        Assert.True(man.IsDespawning || simulation.FindEntity(man.Id) is null);
        Assert.True(reaper.IsDespawning);
        Assert.Equal([DyingStage.OnGround, DyingStage.AwaitingRise, DyingStage.RisingToHell, DyingStage.WalkingToHole, DyingStage.FallingIn], stages);
        Assert.Equal([GrimReaperPhase.Spawning, GrimReaperPhase.Standing, GrimReaperPhase.Walking, GrimReaperPhase.Waiting, GrimReaperPhase.Swiping, GrimReaperPhase.Leaving, GrimReaperPhase.Closing], reaperPhases.SkipWhile(p => p == GrimReaperPhase.Spawning).Prepend(GrimReaperPhase.Spawning).ToList());
    }
}

/// <summary>Patients knocked over by an earthquake (CorsixTH's Patient:falling and humanoid_actions/falling.lua).</summary>
public class FallingTests
{
    [Fact]
    public void AnEarthquakeKnocksAPatientOverAndTheyGetUpAgain()
    {
        var host = new WindowTestHost();
        var simulation = host.Simulation;
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, new TilePosition(10, 10), simulation.Random, PatientLook.Standard, 0));
        humanoid.Type = HumanoidType.StandardMalePatient;
        var patient = new PatientBehaviour { Health = 0.9m };
        patient.SetDisease(DiseaseCatalog.Find("uncommon_cold")!, 0, simulation.Random);
        humanoid.Behaviour = patient;
        var happiness = humanoid.Happiness;

        patient.Fall(simulation, humanoid);
        Assert.Equal(PatientSpotAction.Falling, patient.SpotAction);
        Assert.Equal(2, patient.HasFallen);
        Assert.Equal(happiness - 0.05m, humanoid.Happiness);

        var seen = new List<PatientSpotAction>();
        for (var tick = 0; tick < 200 && patient.SpotAction != PatientSpotAction.None; tick++)
        {
            patient.Update(simulation, humanoid);
            if (patient.SpotAction != PatientSpotAction.None && (seen.Count == 0 || seen[^1] != patient.SpotAction))
            {
                seen.Add(patient.SpotAction);
            }
        }

        Assert.Equal([PatientSpotAction.Falling, PatientSpotAction.OnGround, PatientSpotAction.GettingUp], seen.Where(s => s != PatientSpotAction.ShakingFist).ToList());

        // Not again for two days.
        patient.Fall(simulation, humanoid);
        Assert.Equal(PatientSpotAction.None, patient.SpotAction);
        patient.OnEndDayFall();
        patient.OnEndDayFall();
        Assert.Equal(1, patient.HasFallen);
    }
}
