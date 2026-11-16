using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Sim.Import;

/// <summary>A room of an imported save.</summary>
/// <param name="RoomType">Id of the room's definition.</param>
/// <param name="Area">The room's tiles.</param>
/// <param name="Door">Where its door is.</param>
public sealed record ImportedRoom(string RoomType, TileRect Area, DoorPlacement Door);

/// <summary>An object of an imported save, in a room or in a corridor.</summary>
/// <param name="ObjectType">Id of the object's definition.</param>
/// <param name="Tile">Tile it stands on.</param>
/// <param name="Orientation">Way it faces.</param>
/// <param name="Strength">Its strength, for machines.</param>
/// <param name="TimesUsed">How many times it has been used, for machines.</param>
public sealed record ImportedObject(string ObjectType, TilePosition Tile, Direction Orientation, int Strength, int TimesUsed);

/// <summary>A member of staff of an imported save.</summary>
/// <param name="Profile">Who they are (<see cref="StaffProfile.Import"/>).</param>
/// <param name="Tile">Where they stand.</param>
/// <param name="Happiness">Morale, from 0 to 1.</param>
/// <param name="Fatigue">Tiredness, from 0 to 1.</param>
public sealed record ImportedStaff(StaffProfile Profile, TilePosition Tile, decimal Happiness, decimal Fatigue);

/// <summary>A patient of an imported save; they start again from reception with what they have.</summary>
/// <param name="DiseaseId">Their disease.</param>
/// <param name="Tile">Where they stand.</param>
/// <param name="Health">How well they are, from 0 to 1.</param>
/// <param name="Happiness">From 0 to 1.</param>
/// <param name="Thirst">From 0 (not thirsty) to 1.</param>
/// <param name="Warmth">From 0 to 1.</param>
/// <param name="DiagnosisProgress">How certain the diagnosis is, in thousandths.</param>
public sealed record ImportedPatient(string DiseaseId, TilePosition Tile, decimal Health, decimal Happiness, decimal Thirst, decimal Warmth, int DiagnosisProgress);

/// <summary>A casebook entry of an imported save.</summary>
/// <param name="DiseaseId">The disease.</param>
/// <param name="Discovered">Whether it shows in the casebook.</param>
/// <param name="Effectiveness">Drug effectiveness in percent.</param>
/// <param name="Recoveries">Patients cured of it.</param>
/// <param name="Fatalities">Patients who died of it.</param>
/// <param name="TurnedAway">Patients turned away.</param>
/// <param name="MoneyEarned">Money it brought in.</param>
public sealed record ImportedCase(string DiseaseId, bool Discovered, int Effectiveness, int Recoveries, int Fatalities, int TurnedAway, long MoneyEarned);

/// <summary>
/// The state of a hospital taken from a save of another game (the original Theme Hospital), to be rebuilt on a
/// simulation started on the same level.
/// </summary>
public sealed record ImportedGame
{
    /// <summary>Month of the game, from 1.</summary>
    public int Month { get; init; } = 1;

    /// <summary>Day of the month, from 1.</summary>
    public int Day { get; init; } = 1;

    public long Balance { get; init; }

    public long Loan { get; init; }

    /// <summary>Parcels the player owns, besides those the level starts with.</summary>
    public IReadOnlyList<int> OwnedParcels { get; init; } = [];

    public IReadOnlyList<ImportedRoom> Rooms { get; init; } = [];

    public IReadOnlyList<ImportedObject> Objects { get; init; } = [];

    /// <summary>Policy values as <see cref="HospitalPolicies"/> keeps them; missing ones are left as they are.</summary>
    public IReadOnlyList<(PolicyKind Kind, int Value)> Policies { get; init; } = [];

    /// <summary>Treatment prices in percent of the standard price, by disease id.</summary>
    public IReadOnlyList<(string DiseaseId, int Percent)> Prices { get; init; } = [];

    /// <summary>The hospital's staff; the level's starting staff are let go for them.</summary>
    public IReadOnlyList<ImportedStaff> Staff { get; init; } = [];

    public int Visitors { get; init; }

    public int Cured { get; init; }

    public int Deaths { get; init; }

    /// <summary>Reputation, or null to keep the level's.</summary>
    public int? Reputation { get; init; }

    /// <summary>Hospital value, or null to keep what rebuilding cost.</summary>
    public long? Value { get; init; }

    /// <summary>The graphs' monthly figures, oldest first.</summary>
    public IReadOnlyList<MonthStatistics> Months { get; init; } = [];

    /// <summary>The bank statement, oldest first.</summary>
    public IReadOnlyList<Transaction> Statement { get; init; } = [];

    /// <summary>The patients in the hospital.</summary>
    public IReadOnlyList<ImportedPatient> Patients { get; init; } = [];

    /// <summary>The casebook: what is known of each disease, by disease id.</summary>
    public IReadOnlyList<ImportedCase> Casebook { get; init; } = [];

    /// <summary>Research shares in percent, by area; missing areas are left as they are.</summary>
    public IReadOnlyList<(Research.ResearchCategory Category, int Share)> ResearchShares { get; init; } = [];

    /// <summary>Radiator heat in tenths, or null to keep the level's.</summary>
    public int? RadiatorHeat { get; init; }
}

/// <summary>What could not be rebuilt from an imported save.</summary>
/// <param name="Problems">One line for each room or object left out.</param>
public sealed record ImportReport(IReadOnlyList<string> Problems);

/// <summary>
/// Rebuilds an imported hospital on a simulation freshly started on the save's level: the player buys the parcels,
/// builds and furnishes the rooms and places the corridor objects through the usual commands, so the result holds
/// together as if it had been played; the money, date and settings are then set to the save's.
/// </summary>
public static class GameImport
{
    /// <summary>Rebuilds <paramref name="game"/> for player 0 of <paramref name="simulation"/>, which must not have run yet.</summary>
    public static ImportReport Apply(Simulation simulation, ImportedGame game)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(game);
        const int player = 0;
        var problems = new List<string>();
        var hospital = simulation.Hospitals[player];

        // Enough money to build everything again; the save's own balance is set at the end.
        hospital.ImportFinances(long.MaxValue / 4, 0);

        foreach (var parcel in game.OwnedParcels)
        {
            if (simulation.Map.GetParcelOwner(parcel) == 0)
            {
                simulation.SetParcelOwner(parcel, player + 1);
            }
        }

        var placed = new HashSet<ImportedObject>();
        foreach (var room in game.Rooms)
        {
            hospital.DiscoverRoom(room.RoomType);
            new BuildRoomCommand(new RoomPlan(room.RoomType, room.Area, room.Door)).Execute(simulation, player);
            var built = simulation.Entities.OfType<Room>().LastOrDefault(r => r.Area == room.Area && r.RoomType == room.RoomType);
            if (built is null)
            {
                problems.Add($"room {room.RoomType} at {room.Area} could not be built");
                continue;
            }

            // The objects the room needs go first, small ones before big ones: the original let objects stand closer
            // together than CorsixNet's footprints allow, so whichever comes later may not fit; better an extra object
            // left out than the room unfinished.
            var needed = simulation.Content.FindRoom(room.RoomType)?.NeededObjects;
            foreach (var obj in game.Objects.Where(o => room.Area.Contains(o.Tile)).OrderBy(o => needed?.ContainsKey(o.ObjectType) == true ? 0 : 1).ThenBy(o => FootprintSize(simulation, o)))
            {
                if (Place(simulation, obj, player))
                {
                    placed.Add(obj);
                }
                else
                {
                    problems.Add($"{obj.ObjectType} at {obj.Tile} facing {obj.Orientation} in room {room.RoomType} could not be placed: {Objects.ObjectPlacer.Check(simulation, obj.ObjectType, obj.Tile, obj.Orientation, player)}");
                }
            }

            new ConfirmRoomCommand(built.Id).Execute(simulation, player);
            if (!built.IsActive)
            {
                problems.Add($"room {room.RoomType} at {room.Area} could not be finished");
            }
        }

        foreach (var obj in game.Objects.Where(o => !placed.Contains(o) && !game.Rooms.Any(r => r.Area.Contains(o.Tile))))
        {
            if (!Place(simulation, obj, player))
            {
                problems.Add($"{obj.ObjectType} at {obj.Tile} facing {obj.Orientation} could not be placed: {Objects.ObjectPlacer.Check(simulation, obj.ObjectType, obj.Tile, obj.Orientation, player)}");
            }
        }

        // The level's starting staff make way for the save's.
        foreach (var starter in simulation.Entities.OfType<Humanoid>().Where(h => h.Profile is not null && h.Employer == player).ToList())
        {
            StaffEmployment.Detach(simulation, starter);
            simulation.RemoveEntity(starter.Id);
        }

        foreach (var member in game.Staff)
        {
            var staff = StaffHiring.Place(simulation, member.Profile, member.Tile, player);
            staff.Happiness = Math.Clamp(member.Happiness, 0m, 1m);
            staff.Fatigue = Math.Clamp(member.Fatigue, 0m, 1m);
        }

        foreach (var (kind, value) in game.Policies)
        {
            hospital.Policies.Set(kind, value);
        }

        foreach (var (diseaseId, percent) in game.Prices)
        {
            new SetTreatmentPriceCommand(diseaseId, percent).Execute(simulation, player);
        }

        if (game.RadiatorHeat is { } heat)
        {
            hospital.SetRadiatorHeat(heat);
        }

        foreach (var imported in game.Patients)
        {
            // Someone sitting on a bench stands up next to it.
            if (Diseases.DiseaseCatalog.Find(imported.DiseaseId) is not { } disease || NearestPassable(simulation.Map, imported.Tile) is not { } tile)
            {
                problems.Add($"patient with {imported.DiseaseId} at {imported.Tile} could not be placed");
                continue;
            }

            var patient = simulation.AddEntity(id => Humanoid.CreatePatient(id, tile, simulation.Random, disease.Look, player));
            var behaviour = new People.PatientBehaviour { Health = Math.Clamp(imported.Health, 0.01m, 1m) };
            behaviour.SetDisease(disease, People.PatientCare.ChooseInsuranceCompany(simulation.Random), simulation.Random);
            behaviour.DiagnosisProgress = Math.Clamp(imported.DiagnosisProgress, 0, 1000);
            behaviour.Thirst = Math.Clamp(imported.Thirst, 0m, 1m);
            patient.Behaviour = behaviour;
            patient.Happiness = Math.Clamp(imported.Happiness, 0m, 1m);
            patient.Warmth = Math.Clamp(imported.Warmth, 0m, 1m);
        }

        foreach (var entry in game.Casebook)
        {
            if (Diseases.DiseaseCatalog.Find(entry.DiseaseId) is { } disease)
            {
                var book = hospital.CaseOf(disease, simulation.Rules.Diseases);
                book.Discovered |= entry.Discovered;
                book.CureEffectiveness = Math.Clamp(entry.Effectiveness, 0, DiseaseCase.FullEffectiveness);
                book.Recoveries = entry.Recoveries;
                book.Fatalities = entry.Fatalities;
                book.TurnedAway = entry.TurnedAway;
                book.MoneyEarned = entry.MoneyEarned;
            }
        }

        foreach (var (category, share) in game.ResearchShares)
        {
            hospital.Research.Area(category).Share = Math.Clamp(share, 0, 100);
        }

        hospital.ImportFinances(game.Balance, game.Loan);
        hospital.ImportRecords(game.Visitors, game.Cured, game.Deaths, game.Reputation ?? hospital.Reputation, game.Value ?? hospital.Value, game.Months, game.Statement);
        if (game.Month > 1 || game.Day > 1)
        {
            simulation.SkipToLastHour(new GameDate(1, game.Month, game.Day, 0));
        }

        return new ImportReport(problems);
    }

    /// <summary>Tiles an object takes up, so that small objects against the walls go before big ones in the middle.</summary>
    private static int FootprintSize(Simulation simulation, ImportedObject obj) =>
        simulation.Content.FindObject(obj.ObjectType)?.Orientations.Values.Select(o => o.Footprint.Count).DefaultIfEmpty(0).Max() ?? 0;

    /// <summary>The tile itself if it can be walked on, else the nearest such tile up to two tiles away, or null.</summary>
    private static TilePosition? NearestPassable(LevelMap map, TilePosition tile)
    {
        for (var radius = 0; radius <= 2; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    var (x, y) = (tile.X + dx, tile.Y + dy);
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == radius && map.Contains(x, y) && map[x, y].Has(TileFlags.Passable))
                    {
                        return new TilePosition(x, y);
                    }
                }
            }
        }

        return null;
    }

    private static bool Place(Simulation simulation, ImportedObject obj, int player)
    {
        // The original's direction codes do not always match CorsixNet's footprints: take the save's way if the object
        // fits so, else the opposite way, else any way it fits on the same tile.
        var orientation = obj.Orientation;
        var tile = obj.Tile;
        Direction[] tries = [obj.Orientation, (Direction)(((int)obj.Orientation + 2) % 4), (Direction)(((int)obj.Orientation + 3) % 4), (Direction)(((int)obj.Orientation + 1) % 4)];

        // Failing that, the object moves to a neighbouring tile: the original let use positions overlap, which
        // CorsixNet's placement rules (CorsixTH's) do not.
        (int Dx, int Dy)[] offsets = [(0, 0), (0, 1), (1, 0), (0, -1), (-1, 0)];
        var found = false;
        foreach (var (dx, dy) in offsets)
        {
            foreach (var candidate in tries)
            {
                var at = new TilePosition(obj.Tile.X + dx, obj.Tile.Y + dy);
                if (Objects.ObjectPlacer.Check(simulation, obj.ObjectType, at, candidate, player) == Objects.ObjectPlacementProblem.None)
                {
                    (orientation, tile, found) = (candidate, at, true);
                    break;
                }
            }

            if (found)
            {
                break;
            }
        }

        var before = simulation.Entities.Count;
        new PlaceObjectCommand(obj.ObjectType, tile, orientation).Execute(simulation, player);
        var created = simulation.Entities.Skip(before).OfType<RoomObject>().FirstOrDefault(o => o.Tile == tile && o.ObjectType == obj.ObjectType);
        if (created is null)
        {
            return false;
        }

        if (created.Machine is { } machine)
        {
            machine.Strength = obj.Strength;
            machine.TimesUsed = obj.TimesUsed;
        }

        return true;
    }
}
