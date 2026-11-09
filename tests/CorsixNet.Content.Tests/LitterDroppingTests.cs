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
        // How soon the first piece falls depends on how far the patients walk: give them up to 200 days.
        for (var day = 0; day < 200 && !simulation.Entities.OfType<Litter>().Any(piece => piece.IsRubbish); day++)
        {
            StepFor(simulation, GameDate.HoursPerDay);
        }

        var litter = simulation.Entities.OfType<Litter>().ToList();
        // Sick patients leave puke too (Patient:vomit); rubbish is dropped as they walk.
        Assert.Contains(litter, piece => piece.IsRubbish);
        Assert.All(litter, piece =>
        {
            Assert.True(piece.IsRubbish || piece.Type == LitterType.Puke);
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
