using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;

namespace CorsixNet.Sim.Tests;

/// <summary>Setting prices in the casebook, and which diagnoses it shows.</summary>
public class CasebookTests
{
    private static readonly RoomDefinition GeneralDiagnosis = new("general_diag", "General Diagnosis", 3, 18, new WallSet(82, 83, 90, 91));
    private static readonly GameContent Content = new([GeneralDiagnosis], [114], []);
    private static readonly RoomPlan Plan = new("general_diag", new TileRect(2, 2, 3, 3), new DoorPlacement(new(3, 5), EdgeOrientation.North));

    [Fact]
    public void PricesAreHeldBetweenHalfAndDoubleAndOnlyForKnownDiseases()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];

        // Without level files only general practice is known.
        Run(simulation, new SetTreatmentPriceCommand(DiseaseCatalog.GpDiagnosis, 150));
        Assert.Equal(150, hospital.Casebook[DiseaseCatalog.GpDiagnosis].PricePercent);
        Run(simulation, new SetTreatmentPriceCommand(DiseaseCatalog.GpDiagnosis, 250));
        Assert.Equal(DiseaseCase.MaxPrice, hospital.Casebook[DiseaseCatalog.GpDiagnosis].PricePercent);
        Run(simulation, new SetTreatmentPriceCommand(DiseaseCatalog.GpDiagnosis, 10));
        Assert.Equal(DiseaseCase.MinPrice, hospital.Casebook[DiseaseCatalog.GpDiagnosis].PricePercent);

        Run(simulation, new SetTreatmentPriceCommand("bloaty_head", 150));
        Run(simulation, new SetTreatmentPriceCommand("no_such_disease", 150));
        Assert.Equal(DiseaseCase.StandardPrice, hospital.Casebook["bloaty_head"].PricePercent);

        // The price goes into what patients pay.
        var gp = DiseaseCatalog.Find(DiseaseCatalog.GpDiagnosis)!;
        Assert.Equal(DiseaseRules.Default.CurePrice(gp) / 2, hospital.TreatmentPrice(gp, DiseaseRules.Default));

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, Content);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        Assert.Equal(DiseaseCase.MinPrice, loaded.Hospitals[0].Casebook[DiseaseCatalog.GpDiagnosis].PricePercent);
    }

    [Fact]
    public void BuildingADiagnosisRoomPutsItsDiagnosisInTheCasebook()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        Run(simulation, new SetTreatmentPriceCommand("diag_general_diag", 150));
        Assert.False(hospital.Casebook["diag_general_diag"].Discovered);

        Run(simulation, new BuildRoomCommand(Plan));
        var room = Assert.Single(simulation.Entities.OfType<Room>());
        Assert.False(hospital.Casebook["diag_general_diag"].Discovered);
        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);

        Assert.True(hospital.Casebook["diag_general_diag"].Discovered);
        Run(simulation, new SetTreatmentPriceCommand("diag_general_diag", 150));
        Assert.Equal(150, hospital.Casebook["diag_general_diag"].PricePercent);
    }

    private static void Run(Simulation simulation, Command command)
    {
        simulation.Schedule(0, command);
        simulation.Step();
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
                map[x, y].Set(TileFlags.Buildable | TileFlags.Hospital | TileFlags.BuildableNorth | TileFlags.BuildableEast | TileFlags.BuildableSouth | TileFlags.BuildableWest, true);
            }
        }

        map.UpdatePathfinding();
        map.UpdateShadows();
        return new Simulation(map, seed: 3, Content);
    }
}
