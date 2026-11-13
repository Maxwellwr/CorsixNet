using CorsixNet.Client.Audio;
using CorsixNet.Content.Interface;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;

namespace CorsixNet.Client.Tests;

/// <summary>The gates to hell's lava sounds (CorsixTH's PlayerHospital:showGatesToHell and Audio:playEntitySounds).</summary>
public class GatesToHellSoundsTests
{
    [Fact]
    public void LavaSoundsFollowEachOtherWithSilencesWhileTheGatesAreOpen()
    {
        var host = new WindowTestHost();
        var simulation = host.Simulation;
        var patient = simulation.AddEntity(id => Humanoid.CreatePatient(id, new TilePosition(10, 10), simulation.Random, PatientLook.Standard, 0));
        patient.Type = HumanoidType.StandardMalePatient;
        patient.Behaviour = new PatientBehaviour();
        var grim = GrimReaperBehaviour.TrySpawn(simulation, patient);
        Assert.NotNull(grim);
        var hole = ((GrimReaperBehaviour)grim.Behaviour!).HoleTile;

        var sounds = new GatesToHellSounds();
        var played = new List<long>();
        var now = 0L;
        TimeSpan Play(TilePosition at)
        {
            Assert.Equal(hole, at);
            played.Add(now);
            return TimeSpan.FromMilliseconds(500);
        }

        for (now = 0; now < 10_000; now += 50)
        {
            sounds.Update(simulation, GameSpeed.Normal, now, Play);
        }

        // At normal speed a sound of half a second, then 1150 to 1250 milliseconds of silence.
        Assert.True(played.Count >= 5, $"Only {played.Count} sounds.");
        Assert.All(played.Zip(played.Skip(1)), pair => Assert.InRange(pair.Second - pair.First, 1650, 1800));

        // Paused, none.
        var before = played.Count;
        for (; now < 20_000; now += 50)
        {
            sounds.Update(simulation, GameSpeed.Pause, now, Play);
        }

        Assert.Equal(before, played.Count);
    }
}
