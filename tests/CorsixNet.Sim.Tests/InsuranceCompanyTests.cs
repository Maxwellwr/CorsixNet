using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Economy;

namespace CorsixNet.Sim.Tests;

/// <summary>The three insurance companies a hospital picks (<c>Hospital:Hospital</c>).</summary>
public class InsuranceCompanyTests
{
    [Fact]
    public void AHospitalPicksThreeDifferentCompaniesAndKeepsThemInASave()
    {
        var seen = new HashSet<int>();
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var insurers = NewSimulation(seed).Hospitals[0].Insurers;
            Assert.Equal(Hospital.InsuranceCompanies, insurers.Distinct().Count());
            Assert.All(insurers, insurer => Assert.InRange(insurer, 1, Hospital.InsuranceCompanyNames));
            seen.UnionWith(insurers);
        }

        // Each is drawn from the first quarter of those left half the time, else from all of them.
        Assert.True(seen.Count > 6);

        var simulation = NewSimulation(7);
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, GameContent.Empty);
        Assert.Equal(simulation.Hospitals[0].Insurers, loaded.Hospitals[0].Insurers);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    private static Simulation NewSimulation(ulong seed)
    {
        var map = TestMaps.FromRows("....", "....", "....", "....");
        return new Simulation(map, seed);
    }
}
