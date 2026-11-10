using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;
using static CorsixNet.Content.Tests.PatientFlowTests;

namespace CorsixNet.Content.Tests;

/// <summary>The scanner (CorsixTH's rooms/scanner_room.lua, objects/machines/scanner.lua).</summary>
public class ScannerTests
{
    private static readonly TileRect Area = new(3, 13, 6, 6);

    [Fact]
    public void PatientsUndressAreScannedWhileTheDoctorWorksTheConsoleAndDressAgain()
    {
        // The default rules have every room from the start; a weak GP sends patients on to diagnosis rooms.
        var simulation = NewStaffedHospital(weakDoctor: true);
        var room = BuildScanner(simulation);
        var hospital = simulation.Hospitals[0];
        var scanned = new HashSet<int>();
        var animations = new HashSet<int>();
        var kinds = new HashSet<HumanoidType>();

        for (var tick = 0; tick < 4 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is PatientBehaviour { Phase: PatientPhase.OnTrolley, Use: not null } patient && patient.TargetId == room.Id)
                {
                    scanned.Add(humanoid.Id);
                    kinds.Add(humanoid.Type);
                    if (humanoid.ActionAnimation is { } animation)
                    {
                        animations.Add(animation.Id);
                    }
                }

                if (humanoid.Behaviour is DoctorBehaviour { RoomId: var id } && id == room.Id && humanoid.ActionAnimation is { } readout)
                {
                    animations.Add(readout.Id);
                }
            }
        }

        Assert.True(scanned.Count >= 2, $"Only {scanned.Count} patients were scanned.");
        Assert.Contains(animations, id => id is 1426 or 1434 or 1466 or 1474 or 1406 or 1446);
        Assert.Contains(animations, id => id is 798 or 806 or 810 or 814);
        // Only standard patients undress for the scanner (do_change); slack ones go in dressed.
        Assert.All(kinds, kind => Assert.True(kind.ToString().Contains("Stripped", StringComparison.Ordinal) || kind.ToString().StartsWith("Slack", StringComparison.Ordinal), kind.ToString()));
        Assert.Contains(hospital.Transactions, t => t is { Kind: TransactionKind.Treatment, Subject: "diag_scanner" });
        Assert.DoesNotContain(simulation.Entities.OfType<Humanoid>(), h => h.Behaviour is PatientBehaviour { Phase: PatientPhase.GoingHome } && h.Type.ToString().Contains("Stripped", StringComparison.Ordinal));

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        StepFor(simulation, 5 * GameDate.HoursPerDay);
        StepFor(loaded, 5 * GameDate.HoursPerDay);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    private static Room BuildScanner(Simulation simulation)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("scanner", Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "scanner");
        Place(simulation, "scanner", fromBack: false);
        Place(simulation, "console", fromBack: false);
        Place(simulation, "screen", fromBack: true);
        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, new(14, 16)));
        return room;
    }

    private static void Place(Simulation simulation, string type, bool fromBack)
    {
        for (var i = 0; i < Area.Height; i++)
        {
            var y = fromBack ? Area.Y + i : Area.Bottom - 1 - i;
            for (var j = 0; j < Area.Width; j++)
            {
                var x = fromBack ? Area.X + j : Area.Right - 1 - j;
                foreach (var orientation in new[] { Direction.North, Direction.East })
                {
                    if (ObjectPlacer.Check(simulation, type, new(x, y), orientation, 0) == ObjectPlacementProblem.None)
                    {
                        Run(simulation, new PlaceObjectCommand(type, new(x, y), orientation));
                        return;
                    }
                }
            }
        }

        Assert.Fail($"Found no place for {type}.");
    }
}
