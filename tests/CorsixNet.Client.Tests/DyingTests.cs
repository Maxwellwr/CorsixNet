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
    [Theory]
    [InlineData(PatientLook.Standard)]
    [InlineData(PatientLook.Alien)]
    [InlineData(PatientLook.Hairyitis)]
    public void ADeadPatientFallsRisesAndFliesAway(PatientLook look)
    {
        var host = new WindowTestHost();
        var simulation = host.Simulation;
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, new TilePosition(10, 10), simulation.Random, look, 0));
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
}
