using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.People;
using static CorsixNet.Content.Tests.PatientFlowTests;

namespace CorsixNet.Content.Tests;

/// <summary>Patients dropping litter in the corridors (CorsixTH's Patient:setTile) and people minding it.</summary>
public class LitterDroppingTests
{
    [Fact]
    public void PatientsDropRubbishOnFreeCorridorFloor()
    {
        var simulation = NewStaffedHospital();
        StepFor(simulation, 90 * GameDate.HoursPerDay);

        var litter = simulation.Entities.OfType<Litter>().ToList();
        Assert.NotEmpty(litter);
        Assert.All(litter, piece =>
        {
            Assert.True(piece.IsRubbish);
            var cell = simulation.Map[piece.Tile.X, piece.Tile.Y];
            Assert.Equal(0, cell.RoomId);
            Assert.True(cell.Has(TileFlags.Hospital));
        });
        Assert.Equal(litter.Count, litter.Select(piece => piece.Tile).Distinct().Count());

        // Patients in the hospital have their countdown.
        Assert.Contains(simulation.Entities, entity => entity is Humanoid { Behaviour: PatientBehaviour { LitterCountdown: > 0 } });

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        StepFor(simulation, 10 * GameDate.HoursPerDay);
        StepFor(loaded, 10 * GameDate.HoursPerDay);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());

        // An object placed on litter clears it (World:prepareFootprintTilesForBuild).
        var piece = simulation.Entities.OfType<Litter>().First(p => ObjectPlacer.Check(simulation, "plant", p.Tile, Direction.North, 0) == ObjectPlacementProblem.None);
        Run(simulation, new PlaceObjectCommand("plant", piece.Tile, Direction.North));
        Assert.DoesNotContain(simulation.Entities, entity => entity is Litter { Tile: var tile } && tile == piece.Tile);
    }
}
