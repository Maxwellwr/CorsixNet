using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Sim.Tests;

/// <summary>How patients with a disease look (CorsixTH's diseases/*.lua <c>initPatient</c>).</summary>
public class PatientLookTests
{
    [Fact]
    public void PregnantPatientsAreAlwaysWomen()
    {
        Assert.Equal(PatientLook.Pregnant, DiseaseCatalog.Find("pregnant")!.Look);
        var random = new SimRandom(5);
        for (var i = 0; i < 200; i++)
        {
            var patient = Humanoid.CreatePatient(i + 1, new TilePosition(0, 0), random, PatientLook.Pregnant);
            Assert.Equal(HumanoidType.StandardFemalePatient, patient.Type);
            Assert.InRange(patient.Layers[0], 2, 8);
            Assert.Equal(0, patient.Layers[0] % 2);
            Assert.InRange(patient.Layers[1], 0, 6);
            Assert.Equal(0, patient.Layers[2] + patient.Layers[3] + patient.Layers[4]);
        }
    }
}
