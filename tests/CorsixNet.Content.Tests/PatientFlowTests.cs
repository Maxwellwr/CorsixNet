using CorsixNet.Content.Levels;
using CorsixNet.Formats.Tests;
using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.Pathfinding;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Content.Tests;

/// <summary>Patients arriving, queueing at reception and seeing the GP, with the original game's content.</summary>
public class PatientFlowTests
{
    private const int Size = 24;
    private static readonly TileRect GpArea = new(5, 5, 4, 4);
    private static readonly TileRect PharmacyArea = new(12, 5, 4, 4);
    private static readonly TileRect GeneralDiagArea = new(3, 13, 5, 5);

    [Fact]
    public void NoPatientsComeWithoutAStaffedReceptionDesk()
    {
        var simulation = NewHospital();

        StepFor(simulation, 40 * GameDate.HoursPerDay);

        Assert.DoesNotContain(simulation.Entities, entity => entity is Humanoid { Behaviour: PatientBehaviour });
    }

    [Fact]
    public void AReceptionistGoesToANewDeskAndStandsOnIt()
    {
        var simulation = NewHospital();
        Run(simulation, new HireStaffCommand(StaffClass.Receptionist, 0, new(3, 20)));
        var desk = PlaceReceptionDesk(simulation);
        var receptionist = simulation.Entities.OfType<Humanoid>().Single(h => h.Type == HumanoidType.Receptionist);

        Assert.Equal(receptionist.Id, desk.Desk!.ReservedForId);
        StepFor(simulation, 400);

        Assert.Equal(receptionist.Id, desk.Desk.ReceptionistId);
        Assert.Equal(0, desk.Desk.ReservedForId);
        Assert.Equal(desk.Tile, receptionist.Tile);
    }

    [Fact]
    public void AReceptionistCanBeHiredStraightOntoADeskWithoutOne()
    {
        var simulation = NewHospital();
        var desk = PlaceReceptionDesk(simulation);

        // The desk's tile is not passable: only a receptionist may be put there, and only while the desk has none.
        Assert.Equal(StaffHireProblem.NotPassable, StaffHiring.CheckPlacement(simulation, StaffClass.Doctor, desk.Tile, 0));
        Assert.Equal(StaffHireProblem.None, StaffHiring.CheckPlacement(simulation, StaffClass.Receptionist, desk.Tile, 0));
        Run(simulation, new HireStaffCommand(StaffClass.Receptionist, 0, desk.Tile));
        var receptionist = simulation.Entities.OfType<Humanoid>().Single(h => h.Type == HumanoidType.Receptionist);
        StepFor(simulation, 400);

        Assert.Equal(receptionist.Id, desk.Desk!.ReceptionistId);
        Assert.Equal(StaffHireProblem.NotPassable, StaffHiring.CheckPlacement(simulation, StaffClass.Receptionist, desk.Tile, 0));
    }

    [Fact]
    public void ReceptionDesksAreBoughtWhenPlaced()
    {
        var simulation = NewHospital();
        var balance = simulation.Hospitals[0].Balance;

        PlaceReceptionDesk(simulation);

        Assert.Equal(balance - simulation.Rules.ObjectCost(ReceptionDesks.ObjectId), simulation.Hospitals[0].Balance);
    }

    [Fact]
    public void PatientsQueueAtReceptionSeeTheGpAndGoHome()
    {
        var simulation = NewStaffedHospital();
        var seenByGp = new HashSet<int>();
        var everArrived = new HashSet<int>();
        var maxReceptionQueue = 0;

        for (var tick = 0; tick < 3 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is PatientBehaviour patient)
                {
                    everArrived.Add(humanoid.Id);
                    if (patient.IsBeingSeen)
                    {
                        seenByGp.Add(humanoid.Id);
                    }
                }
            }

            maxReceptionQueue = Math.Max(maxReceptionQueue, Desk(simulation).Desk!.Queue.Count);
        }

        var stillHere = simulation.Entities.OfType<Humanoid>().Where(h => h.Behaviour is PatientBehaviour).Select(h => h.Id).ToHashSet();
        var gp = simulation.Entities.OfType<Room>().Single();

        Assert.True(everArrived.Count >= 10, $"Only {everArrived.Count} patients arrived.");
        Assert.True(Desk(simulation).Desk!.Queue.VisitorCount >= 10);
        Assert.True(gp.DoorQueue!.VisitorCount >= 5, $"Only {gp.DoorQueue.VisitorCount} patients went into the GP's office.");
        Assert.True(seenByGp.Count >= 5);
        Assert.Contains(seenByGp, id => !stillHere.Contains(id));
        Assert.True(maxReceptionQueue >= 1);
    }

    [Fact]
    public void PatientsComeWithADiseasePayTheGpAndGetDiagnosed()
    {
        var simulation = NewStaffedHospital();
        var hospital = simulation.Hospitals[0];

        // The unheated hospital grows cold and its doctor miserable; granting raises keeps them from quitting.
        Run(simulation, new SetPolicyCommand(PolicyKind.GrantWageIncrease, 1));
        var diagnosed = new HashSet<string>();
        var waited = new HashSet<int>();
        var balanceBefore = hospital.Balance;

        for (var tick = 0; tick < 3 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is PatientBehaviour patient)
                {
                    Assert.NotNull(patient.Disease);
                    Assert.False(patient.Disease.IsPseudo);
                    if (patient.IsDiagnosed)
                    {
                        diagnosed.Add(patient.DiseaseId);
                    }

                    if (patient.Phase == PatientPhase.Waiting)
                    {
                        waited.Add(humanoid.Id);
                    }
                }
            }
        }

        var gp = hospital.Casebook[DiseaseCatalog.GpDiagnosis];
        var visits = simulation.Entities.OfType<Room>().Single().DoorQueue!.VisitorCount;
        Assert.True(gp.MoneyEarned >= 100 * (visits - 1), $"The GP earned {gp.MoneyEarned} from {visits} visits.");
        Assert.Contains(hospital.Transactions, t => t is { Kind: TransactionKind.Treatment, Subject: DiseaseCatalog.GpDiagnosis, Amount: 100 });
        Assert.True(hospital.Visitors >= 10);
        Assert.NotEmpty(diagnosed);
        Assert.NotEmpty(waited);

        // Each diagnosed disease is now known to the hospital.
        Assert.All(diagnosed, id => Assert.True(hospital.Casebook[id].Discovered));

        // Nobody is cured without treatment rooms. Each patient sent home costs 3 reputation, each put off by the price 2.
        var turnedAway = hospital.Casebook.Values.Sum(c => c.TurnedAway);
        Assert.True(hospital.NotCured > 0);
        Assert.Equal(Hospital.AverageReputation - (3 * turnedAway) - (2 * (hospital.NotCured - turnedAway)), hospital.Reputation);
    }

    [Fact]
    public void OnLevelOneTheGpDiagnosesPatientsAndIsPaid()
    {
        var install = RealGameData.Require();
        var rules = LevelRulesLoader.LoadOriginal(install, "LEVEL.L1", BaseGameContent.Create());
        var simulation = NewStaffedHospital(rules);
        var levelDiseases = rules.Diseases.AvailableDiseases().Select(d => d.Id).ToHashSet();
        var seen = new Dictionary<int, PatientBehaviour>();

        for (var tick = 0; tick < 3 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is PatientBehaviour { DiagnosisProgress: > 0 } patient)
                {
                    seen[humanoid.Id] = patient;
                }
            }
        }

        Assert.True(seen.Count >= 5, $"Only {seen.Count} patients saw the GP.");
        Assert.All(seen.Values, patient => Assert.Contains(patient.DiseaseId, levelDiseases));

        // Level one's diseases are easy to diagnose (MaxDiagDiff 100 to 300): the GP gets at least 0.28 of the way
        // at once, and often all of it. Diagnosed patients go on to wait for a treatment room, which cannot be built
        // yet; the others wait for a diagnosis room, or for the player's decision.
        Assert.Contains(seen.Values, p => p is { IsDiagnosed: true, Seeking: RoomSeek.Treatment, DiagnosisProgress: PatientCare.FullDiagnosis });
        Assert.All(seen.Values.Where(p => !p.IsDiagnosed), p => Assert.InRange(p.DiagnosisProgress, 280, PatientCare.FullDiagnosis - 1));
        Assert.True(simulation.Hospitals[0].Casebook[DiseaseCatalog.GpDiagnosis].MoneyEarned >= 100 * (seen.Count - 1));
    }

    [Fact]
    public void OnLevelOneTheNurseCuresPatientsInThePharmacy()
    {
        var install = RealGameData.Require();
        var rules = LevelRulesLoader.LoadOriginal(install, "LEVEL.L1", BaseGameContent.Create());
        var simulation = NewStaffedHospital(rules);
        BuildPharmacy(simulation);
        var hospital = simulation.Hospitals[0];
        var pharmacyDiseases = rules.Diseases.AvailableDiseases().Where(d => d.TreatmentRooms is ["pharmacy"]).Select(d => d.Id).ToHashSet();
        var treatedTogether = false;

        for (var tick = 0; tick < 4 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                // While the nurse gets the drink with the patient, her animation shows them both and the patient is hidden.
                if (humanoid.Behaviour is NurseBehaviour { Phase: NursePhase.Treating, Use: { } use }
                    && simulation.FindEntity(use.UseWithId) is Humanoid { IsHidden: true } patient
                    && humanoid.ActionAnimation?.Id is 1630 or 3104)
                {
                    Assert.Equal(PatientPhase.StandingAtObject, ((PatientBehaviour)patient.Behaviour!).Phase);
                    treatedTogether = true;
                }
            }
        }

        Assert.True(treatedTogether);
        Assert.True(hospital.Cured >= 3, $"Only {hospital.Cured} patients were cured.");
        var cures = hospital.Casebook.Values.Where(c => pharmacyDiseases.Contains(c.DiseaseId)).ToList();
        Assert.Equal(hospital.Cured + hospital.Deaths, cures.Sum(c => c.Recoveries + c.Fatalities));
        Assert.All(cures, c => Assert.Equal((100, 100), (c.CureEffectiveness, c.DrugCost)));
        Assert.Contains(cures, c => c.MoneyEarned >= rules.Diseases.StartPrice(DiseaseCatalog.Find(c.DiseaseId)!.ExpertiseId));
        Assert.Contains(hospital.Transactions, t => t is { Kind: TransactionKind.DrugCost, Amount: -100 });
    }

    [Fact]
    public void PatientsWaitingForTreatmentGoToAPharmacyBuiltLater()
    {
        var install = RealGameData.Require();
        var rules = LevelRulesLoader.LoadOriginal(install, "LEVEL.L1", BaseGameContent.Create());
        var simulation = NewStaffedHospital(rules);
        Humanoid? waiting = null;
        for (var tick = 0; tick < 4 * 31 * GameDate.HoursPerDay && waiting is null; tick++)
        {
            simulation.Step();
            waiting = simulation.Entities.OfType<Humanoid>().FirstOrDefault(h => h.Behaviour is PatientBehaviour
            {
                Phase: PatientPhase.Waiting, Seeking: RoomSeek.Treatment, Disease.TreatmentRooms: ["pharmacy"],
            });
        }

        Assert.NotNull(waiting);
        BuildPharmacy(simulation);
        var pharmacy = simulation.Entities.OfType<Room>().Single(r => r.RoomType == "pharmacy");
        var patient = (PatientBehaviour)waiting.Behaviour!;
        for (var tick = 0; tick < 20 * GameDate.HoursPerDay && patient.TargetId != pharmacy.Id; tick++)
        {
            simulation.Step();
        }

        Assert.Equal(pharmacy.Id, patient.TargetId);
    }

    [Fact]
    public void ASavedGameWithATreatmentGoingOnContinuesIdentically()
    {
        var install = RealGameData.Require();
        var simulation = NewStaffedHospital(LevelRulesLoader.LoadOriginal(install, "LEVEL.L1", BaseGameContent.Create()));
        BuildPharmacy(simulation);
        for (var tick = 0; tick < 4 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            if (simulation.Entities.Any(e => e is Humanoid { Behaviour: NurseBehaviour { Phase: NursePhase.Treating } }))
            {
                break;
            }
        }

        Assert.Contains(simulation.Entities, e => e is Humanoid { Behaviour: NurseBehaviour { Phase: NursePhase.Treating } });
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());

        StepFor(simulation, 5 * GameDate.HoursPerDay);
        StepFor(loaded, 5 * GameDate.HoursPerDay);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    [Fact]
    public void OnLevelOneGeneralDiagnosisExaminesPatientsAndSendsThemBackToTheGp()
    {
        var install = RealGameData.Require();
        var rules = LevelRulesLoader.LoadOriginal(install, "LEVEL.L1", BaseGameContent.Create());
        var simulation = NewStaffedHospital(rules);
        BuildGeneralDiagnosis(simulation);
        var hospital = simulation.Hospitals[0];
        var examined = new HashSet<int>();
        var undressedWalking = false;
        var clothesHung = false;
        var backAtGp = new HashSet<int>();

        for (var tick = 0; tick < 4 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            clothesHung |= simulation.Entities.OfType<RoomObject>().Any(o => o is { ObjectType: "screen", HangingClothes: not null });
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is not PatientBehaviour patient)
                {
                    continue;
                }

                undressedWalking |= humanoid.IsMoving && humanoid.Type is HumanoidType.StrippedMalePatient or HumanoidType.StrippedFemalePatient;

                // On the trolley the patient's animation shows the doctor, who is hidden meanwhile.
                if (patient is { Phase: PatientPhase.OnTrolley, TrolleyUse: { } use } && simulation.FindEntity(use.UseWithId) is Humanoid { IsHidden: true, Type: HumanoidType.Doctor })
                {
                    examined.Add(humanoid.Id);
                }

                if (examined.Contains(humanoid.Id) && patient is { Seeking: RoomSeek.Gp, Phase: PatientPhase.UsingObject })
                {
                    Assert.Contains(humanoid.Type, new[] { HumanoidType.StandardMalePatient, HumanoidType.StandardFemalePatient });
                    backAtGp.Add(humanoid.Id);
                }
            }
        }

        Assert.True(examined.Count >= 3, $"Only {examined.Count} patients were examined.");
        Assert.True(undressedWalking);
        Assert.True(clothesHung);
        Assert.NotEmpty(backAtGp);
        Assert.True(hospital.Casebook["diag_general_diag"].MoneyEarned >= 150 * (examined.Count - 1));
        Assert.Contains(hospital.Transactions, t => t is { Kind: TransactionKind.Treatment, Subject: "diag_general_diag" });
    }

    [Fact]
    public void WithGeneralDiagnosisAndAPharmacyMorePatientsAreCured()
    {
        var install = RealGameData.Require();
        int Cured(bool generalDiagnosis)
        {
            var simulation = NewStaffedHospital(LevelRulesLoader.LoadOriginal(install, "LEVEL.L1", BaseGameContent.Create()), weakDoctor: true);
            BuildPharmacy(simulation);
            if (generalDiagnosis)
            {
                BuildGeneralDiagnosis(simulation);
            }

            StepFor(simulation, 5 * 31 * GameDate.HoursPerDay);
            return simulation.Hospitals[0].Cured;
        }

        var without = Cured(generalDiagnosis: false);
        var with = Cured(generalDiagnosis: true);
        Assert.True(with > without, $"{with} cured with general diagnosis, {without} without.");
    }

    [Fact]
    public void ASavedGameWithAPatientOnTheTrolleyContinuesIdentically()
    {
        var install = RealGameData.Require();
        var simulation = NewStaffedHospital(LevelRulesLoader.LoadOriginal(install, "LEVEL.L1", BaseGameContent.Create()));
        BuildGeneralDiagnosis(simulation);
        for (var tick = 0; tick < 4 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            if (simulation.Entities.Any(e => e is Humanoid { Behaviour: PatientBehaviour { Phase: PatientPhase.OnTrolley } }))
            {
                break;
            }
        }

        Assert.Contains(simulation.Entities, e => e is Humanoid { Behaviour: PatientBehaviour { Phase: PatientPhase.OnTrolley } });
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());

        StepFor(simulation, 5 * GameDate.HoursPerDay);
        StepFor(loaded, 5 * GameDate.HoursPerDay);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    [Fact]
    public void AQueueLinesUpOnNeighbouringTiles()
    {
        var simulation = NewHospital();
        var map = simulation.Map;
        var start = new TilePosition(12, 12);
        var pathfinder = new Pathfinder();
        var tiles = Enumerable.Range(0, 4).Select(i => pathfinder.TryFindIdleTile(map, start, i, out var tile) ? tile : default).ToList();

        Assert.Equal(start, tiles[0]);
        Assert.Equal(4, tiles.Distinct().Count());
        for (var i = 1; i < tiles.Count; i++)
        {
            Assert.Equal(1, Math.Abs(tiles[i].X - tiles[i - 1].X) + Math.Abs(tiles[i].Y - tiles[i - 1].Y));
        }
    }

    [Fact]
    public void ASavedGameWithPatientsContinuesIdentically()
    {
        var simulation = NewStaffedHospital();
        StepFor(simulation, 45 * GameDate.HoursPerDay);
        Assert.Contains(simulation.Entities, entity => entity is Humanoid { Behaviour: PatientBehaviour });

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());

        StepFor(simulation, 20 * GameDate.HoursPerDay);
        StepFor(loaded, 20 * GameDate.HoursPerDay);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    [Fact]
    public void PoissonRandomNumbersHaveTheRequestedMean()
    {
        var random = new SimRandom(5);
        var total = 0;
        for (var i = 0; i < 2000; i++)
        {
            total += PatientArrivals.PoissonRandom(random, 7.75m);
        }

        Assert.InRange(total / 2000m, 7.4m, 8.1m);
    }

    internal static RoomObject Desk(Simulation simulation) => simulation.Entities.OfType<RoomObject>().Single(o => o.Desk is not null);

    /// <summary>A hospital with a GP's office, a doctor, a receptionist and a staffed reception desk.</summary>
    internal static Simulation NewStaffedHospital(LevelRules? rules = null, bool weakDoctor = false)
    {
        var simulation = NewHospital(rules);
        Run(simulation, new BuildRoomCommand(new RoomPlan("gp", GpArea, new DoorPlacement(new(6, GpArea.Bottom), EdgeOrientation.North))));
        var room = simulation.Entities.OfType<Room>().Single();
        foreach (var type in new[] { "desk", "cabinet", "chair" })
        {
            PlaceAnywhere(simulation, type, GpArea);
        }

        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        // A weak GP leaves level one's patients short of a diagnosis, so that they need a diagnosis room.
        var doctors = simulation.AvailableStaff(StaffClass.Doctor);
        var doctor = weakDoctor ? doctors.Select((profile, index) => (profile.Skill, index)).Min().index : 0;
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, doctor, new(15, 15)));
        Run(simulation, new HireStaffCommand(StaffClass.Receptionist, 0, new(16, 15)));
        PlaceReceptionDesk(simulation);
        return simulation;
    }

    /// <summary>A pharmacy east of the GP's office, with a nurse.</summary>
    internal static void BuildPharmacy(Simulation simulation)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("pharmacy", PharmacyArea, new DoorPlacement(new(13, PharmacyArea.Bottom), EdgeOrientation.North))));
        var room = simulation.Entities.OfType<Room>().Single(r => r.RoomType == "pharmacy");
        PlaceAnywhere(simulation, "pharmacy_cabinet", PharmacyArea);
        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        Run(simulation, new HireStaffCommand(StaffClass.Nurse, 0, new(15, 16)));
    }

    /// <summary>A second GP's office where the pharmacy would go, without a doctor of its own.</summary>
    internal static Room BuildSecondGp(Simulation simulation)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("gp", PharmacyArea, new DoorPlacement(new(13, PharmacyArea.Bottom), EdgeOrientation.North))));
        var room = simulation.Entities.OfType<Room>().Single(r => r.RoomType == "gp" && !r.IsActive);
        foreach (var type in new[] { "desk", "cabinet", "chair" })
        {
            PlaceAnywhere(simulation, type, PharmacyArea);
        }

        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        return room;
    }

    /// <summary>General diagnosis south-west of the GP's office, with a second doctor.</summary>
    private static void BuildGeneralDiagnosis(Simulation simulation)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("general_diag", GeneralDiagArea, new DoorPlacement(new(5, GeneralDiagArea.Y), EdgeOrientation.North))));
        var room = simulation.Entities.OfType<Room>().Single(r => r.RoomType == "general_diag");
        PlaceAnywhere(simulation, "screen", GeneralDiagArea);
        PlaceAnywhere(simulation, "crash_trolley", GeneralDiagArea);
        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, new(14, 16)));
    }

    private static RoomObject PlaceReceptionDesk(Simulation simulation)
    {
        PlaceAnywhere(simulation, ReceptionDesks.ObjectId, new TileRect(12, 14, 4, 4));
        return Desk(simulation);
    }

    private static void PlaceAnywhere(Simulation simulation, string type, TileRect area)
    {
        for (var y = area.Y; y < area.Bottom; y++)
        {
            for (var x = area.X; x < area.Right; x++)
            {
                foreach (var orientation in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
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

    /// <summary>
    /// A map of grass with a hospital building in the middle (tiles 2 to 21), all of it the player's;
    /// patients come from the edges.
    /// </summary>
    private static Simulation NewHospital(LevelRules? rules = null)
    {
        var map = new LevelMap(Size, Size, playerCount: 1);
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                ref var tile = ref map[x, y];
                tile.Floor = 1;
                tile.Set(TileFlags.Passable, true);
                if (x is >= 2 and < Size - 2 && y is >= 2 and < Size - 2)
                {
                    tile.Parcel = 1;
                    tile.Set(TileFlags.Buildable | TileFlags.Hospital | TileFlags.BuildableNorth | TileFlags.BuildableEast | TileFlags.BuildableSouth | TileFlags.BuildableWest, true);
                }
            }
        }

        map.UpdatePathfinding();
        map.UpdateShadows();
        rules ??= new LevelRules(1_000_000, [KeyValuePair.Create("gp", 2500)], [KeyValuePair.Create(ReceptionDesks.ObjectId, 600)]);
        var simulation = new Simulation(map, seed: 3, BaseGameContent.Create(), rules);

        // Open at once rather than after the hundred days of the opening watch.
        simulation.Schedule(0, new OpenHospitalCommand());
        return simulation;
    }

    internal static void Run(Simulation simulation, Command command)
    {
        simulation.Schedule(0, command);
        simulation.Step();
    }

    internal static void StepFor(Simulation simulation, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            simulation.Step();
        }
    }
}
